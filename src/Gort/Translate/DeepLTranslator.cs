using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gort.Translate;

/// <summary>
/// Tradutor comercial europeu (VI.6, RF-271/272): POST JSON com vetor de
/// textos + idiomas, chave no cabeçalho; endpoints gratuito/pago.
/// Chinês normalizado para código genérico (RF-272 🔒).
/// </summary>
public sealed class DeepLTranslator : HttpTranslator
{
    public override string Id => "commercial-eu";
    public override string Display => "Tradutor comercial por chave (EU)";
    public override string DefaultToken => RemoteDefaults.DefaultToken;

    private readonly Func<string> _key;
    private readonly Func<string> _endpoint;

    public DeepLTranslator(Func<string> key, Func<string> endpoint,
        HttpMessageHandler? handler = null)
        : base(handler, 10000)
    {
        _key = key; _endpoint = endpoint;
    }

    internal static string BaseUrl(string endpoint) =>
        endpoint == "paid"
            ? "https://api.deepl.com/v2/translate"
            : "https://api-free.deepl.com/v2/translate";

    /// <summary>RF-272: códigos de chinês → genérico.</summary>
    internal static string NormZh(string code) =>
        code.StartsWith("ZH", StringComparison.OrdinalIgnoreCase) ? "ZH" : code;

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        string key = _key();
        if (string.IsNullOrWhiteSpace(key)) return Fail("Informe a chave do tradutor comercial (EU).");
        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["text"] = texts.Count > 0 ? new[] { texts[0] } : Array.Empty<string>(),
                ["source_lang"] = NormZh(srcCode),
                ["target_lang"] = NormZh(dstCode),
            });
            using var resp = await PostJsonAsync(BaseUrl(_endpoint()), body,
                new Dictionary<string, string>
                {
                    ["Authorization"] = "DeepL-Auth-Key " + key,
                }, ct).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string msg = json;
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("message", out var m))
                        msg = m.GetString() ?? msg;
                }
                catch { }
                return Fail(msg);
            }
            using var ok = JsonDocument.Parse(json);
            if (!ok.RootElement.TryGetProperty("translations", out var trs)
                || trs.ValueKind != JsonValueKind.Array || trs.GetArrayLength() == 0)
                return Fail("Resposta inesperada do tradutor.");
            var tr = trs[0].GetProperty("text");
            string out0 = tr.ValueKind == JsonValueKind.Array
                ? string.Concat(ConcatAll(tr)) : tr.GetString() ?? "";
            return new ServiceResult { Translations = new List<string> { out0 } };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Fail("Falha de processamento: " + ex.Message); }
    }

    private static IEnumerable<string> ConcatAll(JsonElement arr)
    {
        foreach (var el in arr.EnumerateArray())
            yield return el.ValueKind == JsonValueKind.String
                ? el.GetString() ?? "" : el.ToString();
    }
}
