using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;

namespace Gort.Translate;

/// <summary>
/// Tradutor web gratuito (VI.1, RF-244..RF-248): GET ao endpoint público do
/// Google (translate.googleapis.com) com o texto na URL; traduções do
/// primeiro vetor, concatenadas com espaço.
/// É o tradutor padrão do programa (RF-225): Google Tradutor.
/// Cliente de alta qualidade; 429/403 → baixa + 1 repetição; baixa dura P-53.
/// Padrão do programa (RF-225). Sem ponte (RF-239: só planilha declara).
/// </summary>
public sealed class WebFreeTranslator : ITranslationService
{
    public string Id => "web-free";
    public string Display => "Google Tradutor (web gratuito)";
    public string DefaultToken => RemoteDefaults.DefaultToken;   // RF-232
    public bool SupportsBridge => false;
    public bool UsesResultMemory => true;
    public bool UsesCollectanea => true;

    private static readonly HttpClient Http = new();
    private DateTime _lowUntil = DateTime.MinValue;
    private readonly object _gate = new();

    /// <summary>
    /// Escolha manual de qualidade (perfil): "auto" (padrão), "high" ou
    /// "low". Injetada pelo registro de serviços a partir do perfil.
    /// </summary>
    public Func<string> QualityProvider { get; set; } = () => "auto";

    public bool IsLowQuality
    {
        get { lock (_gate) return DateTime.UtcNow < _lowUntil; }
    }

    internal void EnterLowQuality()    // testável
    {
        lock (_gate) _lowUntil = DateTime.UtcNow.AddHours(Params.P53_LowQualityHours);  // 🔒
    }

    public async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        if (texts.Count == 0) return new ServiceResult { Translations = new() };
        using var timeout = new CancellationTokenSource(Params.P54_WebTimeoutMs);  // 🔒
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            string mode = QualityProvider().ToLowerInvariant();
            // high = sempre alta; low = sempre baixa; auto = alta com
            // queda automática para baixa em 429/403 (RF-245, padrão).
            bool low = mode == "low" || (mode != "high" && IsLowQuality);
            string raw = await GetAsync(texts[0], srcCode, dstCode,
                low ? RemoteDefaults.WebLowClient : RemoteDefaults.WebHighClient,
                mode == "high", linked.Token).ConfigureAwait(false);
            return new ServiceResult
            {
                Translations = new List<string>
                    { (low ? RemoteDefaults.LowQualityPrefix : "") + raw },
            };
        }
        catch (QuotaException ex)
        {
            // Já estava em baixa: cota horária esgotada (RF-245).
            return new ServiceResult { Error = ex.Message };
        }
        catch (OperationCanceledException)
        {
            throw;   // RF-238: cancelamento não é erro
        }
        catch (Exception ex)
        {
            return new ServiceResult { Error = "Falha de processamento: " + ex.Message };
        }
    }

    private async Task<string> GetAsync(string text, string src, string dst,
        string client, bool forceHigh, CancellationToken ct)
    {
        string url = BuildUrl(text, src, dst, client);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Content-Type",
            "application/x-www-form-urlencoded; charset=UTF-8");
        req.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
        using var resp = await Http.SendAsync(req,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests
            || resp.StatusCode == HttpStatusCode.Forbidden)   // RF-245: 429 cota, 403 bloqueio
        {
            // Escolha manual "high": respeita e devolve o erro em vez de
            // trocar sozinho; nos demais modos cai para baixa uma vez.
            if (forceHigh || client == RemoteDefaults.WebLowClient)
                throw new QuotaException(
                    resp.StatusCode == HttpStatusCode.Forbidden
                    ? "Google bloqueou o acesso temporariamente (HTTP 403). Aguarde ou troque de serviço."
                    : "Cota horária do tradutor gratuito esgotada. Aguarde ou troque de serviço.");
            EnterLowQuality();                                // P-53 🔒
            return await GetAsync(text, src, dst,
                RemoteDefaults.WebLowClient, false, ct).ConfigureAwait(false);
        }
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}");
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseResponse(body);
    }

    internal static string BuildUrl(string text, string src, string dst, string client) =>
        $"{RemoteDefaults.WebEndpoint}?client={Uri.EscapeDataString(client)}" +
        $"&sl={Uri.EscapeDataString(src)}&tl={Uri.EscapeDataString(dst)}" +
        $"&dt=t&q={Uri.EscapeDataString(text)}";

    /// <summary>
    /// RF-244: primeiro elemento = vetor de segmentos; de cada segmento que é
    /// vetor cujo primeiro item é texto, extrai e concatena com espaço.
    /// </summary>
    internal static string ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            throw new InvalidOperationException("Resposta inesperada do tradutor.");
        var parts = new List<string>();
        foreach (var seg in root[0].EnumerateArray())
        {
            if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0
                && seg[0].ValueKind == JsonValueKind.String)
                parts.Add(seg[0].GetString() ?? "");
        }
        return string.Join(" ", parts);
    }

    private sealed class QuotaException : Exception
    {
        public QuotaException(string m) : base(m) { }
    }
}
