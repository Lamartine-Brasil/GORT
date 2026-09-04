using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;

namespace Gort.Translate;

/// <summary>
/// Tradutor web sem chave do mesmo fornecedor (VI.3, RF-254): POST formulário
/// com cabeçalhos de navegador; após cada requisição, bloqueio aleatório de
/// até P-56 antes da próxima (RF-254 🔒).
/// </summary>
public sealed class NoKeyTranslator : HttpTranslator
{
    public override string Id => "web-nokey";
    public override string Display => "Tradutor web sem chave";
    public override string DefaultToken => RemoteDefaults.DefaultToken;

    public const string Endpoint = "https://papago.naver.com/apis/n2mt/translate";

    private readonly SemaphoreSlim _gap = new(1, 1);
    private readonly Random _rand = new();

    public NoKeyTranslator(HttpMessageHandler? handler = null)
        : base(handler, 10000) { }

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        await _gap.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var resp = await PostFormAsync(Endpoint,
                new Dictionary<string, string>
                {
                    ["dict"] = "false",
                    ["honorific"] = "false",
                    ["glossary"] = "false",
                    ["source"] = srcCode,
                    ["target"] = dstCode,
                    ["text"] = texts.Count > 0 ? texts[0] : "",
                },
                new Dictionary<string, string>
                {
                    ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)",
                    ["Referer"] = "https://papago.naver.com/",
                    ["Accept"] = "application/json",
                    ["Accept-Language"] = "pt-BR,pt;q=0.9",
                }, ct).ConfigureAwait(false);
            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if ((int)resp.StatusCode != 200)
                return Fail($"HTTP {(int)resp.StatusCode}");
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("translatedText", out var tt))
                    return new ServiceResult
                    {
                        Translations = new List<string> { tt.GetString() ?? "" },
                    };
                return Fail("JSON inválido.");
            }
            catch { return Fail("JSON inválido."); }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Fail("Falha de processamento: " + ex.Message); }
        finally
        {
            // RF-254: espaça chamadas contra bloqueio por automação.
            try { await Task.Delay(_rand.Next(Params.P56_PostRequestJitterMs + 1), ct)
                .ConfigureAwait(false); } catch { }
            _gap.Release();
        }
    }
}
