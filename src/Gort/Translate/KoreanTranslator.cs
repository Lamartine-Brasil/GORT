using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Config;
using Gort.Core;
using Gort.Store;

namespace Gort.Translate;

/// <summary>
/// Tradutor comercial por chave KR (VI.2, RF-249..253): POST formulário com
/// cabeçalhos de credencial; múltiplas chaves com rodízio (até P-55);
/// ao trocar, anexa nota (RF-251); estados por chave, gratuitas primeiro
/// (RF-252). Gerenciamento de chaves na Etapa 17 (RF-253).
/// </summary>
public sealed class KoreanTranslator : HttpTranslator
{
    public override string Id => "commercial-kr";
    public override string Display => "Tradutor comercial por chave (KR)";
    public override string DefaultToken => RemoteDefaults.DefaultToken;

    public const string Endpoint = "https://openapi.naver.com/v1/papago/n2mt";

    private readonly object _gate = new();
    private readonly Dictionary<string, KeyState> _states = new();

    internal enum KeyState { Normal, Error, Limit }

    public KoreanTranslator(HttpMessageHandler? handler = null)
        : base(handler, 10000) { }

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        var keys = OrderedKeys();
        if (keys.Count == 0)
            return Fail("Cadastre ao menos uma chave do tradutor comercial.");
        string? lastError = null;
        foreach (var (id, secret, plan, keyId) in keys)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var resp = await PostFormAsync(Endpoint,
                    new Dictionary<string, string>
                    {
                        ["source"] = srcCode, ["target"] = dstCode,
                        ["text"] = texts.Count > 0 ? texts[0] : "",
                    },
                    new Dictionary<string, string>
                    {
                        ["X-Naver-Client-Id"] = id,
                        ["X-Naver-Client-Secret"] = secret,
                        ["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8",
                        ["Cache-Control"] = "no-cache",
                    }, ct).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = Parse(body, (int)resp.StatusCode);
                if (parsed.Ok)
                {
                    SetState(keyId, KeyState.Normal);
                    return new ServiceResult { Translations = new List<string> { parsed.Text! } };
                }
                lastError = parsed.Error;
                SetState(keyId, parsed.Limited ? KeyState.Limit : KeyState.Error);
                if (keys.Count > 1) continue;   // RF-250: tenta a próxima
                return Fail(lastError ?? "Erro desconhecido.");
            }
            catch (OperationCanceledException)
            {
                // Pedido do usuário aborta; timeout tenta a próxima (RF-250).
                if (ct.IsCancellationRequested) throw;
                lastError = "Tempo esgotado. Verifique a rede ou troque de serviço.";
            }
            catch (Exception ex) { lastError = "Falha de processamento: " + ex.Message; }
        }
        string note = keys.Count > 1 ? $" (última tentativa: {lastError})" : "";
        // RF-251: informa qual chave passou a valer — aqui, todas falharam.
        return Fail((lastError ?? "Falha.") + note);
    }

    internal List<(string Id, string Secret, string Plan, string KeyId)> OrderedKeys()
    {
        var all = ConfigService.LoadCreds("commercial-kr")
            .Take(Params.P55_MaxApiKeys).ToList();                    // RF-250: até 20
        lock (_gate)
            foreach (var k in all)
                if (!_states.ContainsKey(k.Id)) _states[k.Id] = KeyState.Normal;
        // RF-252: gratuitas antes das pagas, começando pela primeira normal.
        var free = new List<CredentialRecord>();
        var paid = new List<CredentialRecord>();
        foreach (var k in all)
            if (k.Plan == "paid") paid.Add(k); else free.Add(k);
        var ordered = new List<(string, string, string, string)>();
        foreach (var k in free.Concat(paid))
        {
            KeyState st;
            lock (_gate) st = _states[k.Id];
            if (st == KeyState.Normal)
                ordered.Add((k.Id, k.Secret, k.Plan, k.Id));
        }
        foreach (var k in free.Concat(paid))
        {
            KeyState st;
            lock (_gate) st = _states[k.Id];
            if (st != KeyState.Normal)
                ordered.Add((k.Id, k.Secret, k.Plan, k.Id));
        }
        return ordered;
    }

    internal void SetState(string keyId, KeyState st)
    {
        lock (_gate) _states[keyId] = st;
    }

    /// <summary>
    /// VI.2: sucesso em message.result.translatedText; falha em errorMessage
    /// (gratuito) ou error.message (pago). Códigos de limite conhecidos.
    /// </summary>
    internal static (bool Ok, string? Text, string? Error, bool Limited) Parse(
        string body, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var msg)
                && msg.TryGetProperty("result", out var res)
                && res.TryGetProperty("translatedText", out var tt))
                return (true, tt.GetString() ?? "", null, false);
            if (root.TryGetProperty("errorMessage", out var em))
            {
                string m = em.GetString() ?? "";
                string code = root.TryGetProperty("errorCode", out var ec)
                    ? ec.GetString() ?? "" : "";
                bool limited = code is "010" or "011" or "SAT000" || status == 429;
                return (false, null, $"[{code}] {m}", limited);
            }
            if (root.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var mm))
                return (false, null, mm.GetString() ?? "Erro", status == 429 || status == 403);
            return (false, null, $"HTTP {status}: formato inesperado.", false);
        }
        catch (Exception ex)
        {
            return (false, null, "JSON inválido: " + ex.Message, false);
        }
    }
}
