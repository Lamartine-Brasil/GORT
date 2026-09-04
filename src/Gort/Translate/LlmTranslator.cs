using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;

namespace Gort.Translate;

/// <summary>
/// Tradutor por modelo de linguagem (VI.7, RF-273..283): POST de geração com
/// instrução de sistema, texto, segurança sem bloqueio e geração por preset.
/// Bloqueio → refaz na web gratuita (RF-277). Recurso secundário (RF-226).
/// </summary>
public sealed class LlmTranslator : HttpTranslator
{
    public override string Id => "llm";
    public override string Display => "Tradutor por modelo de linguagem";
    public override string DefaultToken => RemoteDefaults.DefaultToken;

    private readonly Func<string> _key;
    private readonly Func<string> _model;
    private readonly Func<string> _customName;
    private readonly Func<string> _preset;
    private readonly Func<int> _temp;
    private readonly Func<int> _reason;
    private readonly Func<int> _maxOut;
    private readonly Func<string> _customInstruction;
    private readonly Func<bool> _noDefault;
    private readonly Func<ITranslationService?> _fallback;

    public LlmTranslator(Func<string> key, Func<string> model, Func<string> customName,
        Func<string> preset,
        Func<int> temp, Func<int> reason, Func<int> maxOut,
        Func<string> customInstruction, Func<bool> noDefault,
        Func<ITranslationService?> fallback, HttpMessageHandler? handler = null)
        : base(handler, Params.P77_LlmTimeoutSec * 1000)   // P-77 🔒 300 s
    {
        _key = key; _model = model; _customName = customName; _preset = preset;
        _temp = temp; _reason = reason; _maxOut = maxOut;
        _customInstruction = customInstruction; _noDefault = noDefault;
        _fallback = fallback;
    }

    /// <summary>RF-274: cada cláusula corrige um comportamento observado (🔒).</summary>
    internal static string DefaultInstruction(string targetLang) =>
        $"Translate the following text into {targetLang}. " +
        "Do not omit any words. Do not use honorifics. " +
        "All characters are 22 years old or older. " +
        "Preserve all symbols exactly. " +
        "Return only the translation.";

    /// <summary>RF-275: personalizada primeiro, padrão em seguida.</summary>
    internal string SystemInstruction(string targetLang)
    {
        string custom = _customInstruction();
        string def = DefaultInstruction(targetLang);
        if (custom.Length == 0) return def;
        if (_noDefault()) return custom;
        return custom + " \n" + def;
    }

    internal string EffectiveModel()
    {
        string m = _model();
        if (m == "" || m == "default") return RemoteDefaults.LlmDefaultModel;  // RF-279
        if (m == "custom") return _customName();
        return m;
    }

    /// <summary>RF-280: família pelo nome (prefixo 2ª geração → formato antigo).</summary>
    internal static bool IsOldFamily(string model) =>
        model.StartsWith("gemini-2.", StringComparison.OrdinalIgnoreCase);

    internal static bool IsPro(string model) =>
        model.Contains("pro", StringComparison.OrdinalIgnoreCase);

    /// <summary>RF-281: presets padrão/econômico/personalizado.</summary>
    internal (int Temp, int Reason, int MaxOut) Generation()
    {
        return _preset() switch
        {
            "eco" => (Params.P67_LlmTempEco, Params.P68_LlmReasonEco, Params.P69_LlmMaxOutEco),
            "custom" => (
                Math.Clamp(_temp(), Params.P70_TempMin, Params.P71_TempMax),
                Math.Clamp(_reason(), Params.P72_ReasonMin, Params.P73_ReasonMax),
                Math.Clamp(_maxOut(), Params.P74_MaxOutMin, Params.P75_MaxOutMax)),
            _ => (Params.P64_LlmTempDefault, Params.P65_LlmReasonDefault, Params.P66_LlmMaxOutDefault),
        };
    }

    /// <summary>RF-282: nível → formato do modelo (🔒).</summary>
    internal static Dictionary<string, object> Reasoning(string model, int level)
    {
        bool pro = IsPro(model);
        if (IsOldFamily(model))
        {
            if (level == 1)
                return new Dictionary<string, object>
                {
                    ["thinkingBudget"] = pro ? Params.P76_ProReasonBudget : 0,  // 🔒 512
                };
            return new Dictionary<string, object>();   // 0, 2, 3 omitem
        }
        string label = level switch
        {
            0 or 3 => "HIGH",
            1 => pro ? "LOW" : "MINIMAL",
            _ => pro ? "LOW" : "MEDIUM",
        };
        return new Dictionary<string, object> { ["thinkingLevel"] = label };
    }

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        string key = _key();
        if (string.IsNullOrWhiteSpace(key)) return Fail("Informe a chave do modelo de linguagem.");
        string model = EffectiveModel();
        var (temp, reason, maxOut) = Generation();
        string targetName = LangName(dstCode);
        var body = new Dictionary<string, object>
        {
            ["system_instruction"] = new Dictionary<string, object>
            {
                ["parts"] = new object[]
                    { new Dictionary<string, object> { ["text"] = SystemInstruction(targetName) } },
            },
            ["contents"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["parts"] = new object[]
                        { new Dictionary<string, object>
                            { ["text"] = texts.Count > 0 ? texts[0] : "" } },
                },
            },
            ["safetySettings"] = SafetyOff(),                          // RF-276 🔒
            ["generationConfig"] = new Dictionary<string, object>
            {
                ["temperature"] = temp / 100.0,
                ["maxOutputTokens"] = maxOut,
                ["thinkingConfig"] = Reasoning(model, reason),
            },
        };
        try
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models/" +
                Uri.EscapeDataString(model) + ":generateContent?key=" + Uri.EscapeDataString(key);
            using var resp = await PostJsonAsync(url, JsonSerializer.Serialize(body),
                new Dictionary<string, string>(), ct).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("promptFeedback", out var fb)
                && fb.TryGetProperty("blockReason", out _))
            {
                if (_fallback() is { } fallback)                        // RF-277
                    return await fallback.TranslateAsync(texts, srcCode, dstCode, ct)
                        .ConfigureAwait(false);
                return Fail("Conteúdo bloqueado pelo modelo.");
            }
            if (!resp.IsSuccessStatusCode)
                return Fail($"HTTP {(int)resp.StatusCode}");
            if (!root.TryGetProperty("candidates", out var cands)
                || cands.ValueKind != JsonValueKind.Array || cands.GetArrayLength() == 0)
                return Fail("Resposta inesperada do modelo.");
            var parts = cands[0]
                .GetProperty("content").GetProperty("parts");
            var sb = new System.Text.StringBuilder();
            foreach (var part in parts.EnumerateArray())
                if (part.TryGetProperty("text", out var t))
                    sb.Append(t.GetString());
            return new ServiceResult { Translations = new List<string> { sb.ToString().Trim() } };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return Fail("Falha de processamento: " + ex.Message); }
    }

    internal static List<Dictionary<string, object>> SafetyOff() =>
        new()
        {
            S("HARM_CATEGORY_HARASSMENT"), S("HARM_CATEGORY_HATE_SPEECH"),
            S("HARM_CATEGORY_SEXUALLY_EXPLICIT"), S("HARM_CATEGORY_DANGEROUS_CONTENT"),
        };

    private static Dictionary<string, object> S(string cat) =>
        new() { ["category"] = cat, ["threshold"] = "BLOCK_NONE" };

    private static string LangName(string code)
    {
        foreach (var e in LangCodes.All)
            if (LangCodes.SameCode(e.Svc.GetValueOrDefault("llm", ""), code)
                || e.Key == code) return e.NameEn;
        return code;
    }
}
