using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Config;

namespace Gort.Translate;

/// <summary>
/// Motor de templates da API personalizada (RF-296..301): substituição com
/// escape JSON, sintaxe relaxada `chave = valor` → JSON, auto-chaves,
/// validação, descoberta recursiva da chave de resultado, cabeçalhos.
/// </summary>
public static class TemplateEngine
{
    public static string JsonEscape(string s) =>
        JsonSerializer.Serialize(s)[1..^1];   // entre aspas → miolo escapado

    /// <summary>RF-296: {OCR_TEXT} escapado, {SOURCE_CODE}, {RESULT_CODE}.</summary>
    public static string Substitute(string template, string text, string src, string dst) =>
        template.Replace("{OCR_TEXT}", JsonEscape(text))
            .Replace("{SOURCE_CODE}", src)
            .Replace("{RESULT_CODE}", dst);

    /// <summary>
    /// RF-297/298/299: aceita JSON ou `chave = valor` por vírgulas; preserva
    /// aspas, booleanos, números e nulo; vetores elemento a elemento; envolve
    /// com chaves se preciso; valida antes do envio.
    /// </summary>
    public static (bool Ok, string Json, string? Error) BuildJson(string template)
    {
        string t = template.Trim();
        if (t.StartsWith("{") || t.StartsWith("["))              // JSON válido?
        {
            try { using var _ = JsonDocument.Parse(t); return (true, t, null); }
            catch (Exception ex)
            { return (false, "", "Modelo inválido: " + ex.Message); }  // RF-299
        }
        try
        {
            var parts = SplitTop(t);
            var sb = new System.Text.StringBuilder("{");
            bool first = true;
            foreach (var part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                string key = part[..eq].Trim().Trim('"');
                string val = part[(eq + 1)..].Trim();
                if (!first) sb.Append(',');
                first = false;
                sb.Append(JsonSerializer.Serialize(key));
                sb.Append(':');
                sb.Append(ConvertValue(val));
            }
            sb.Append('}');
            string json = sb.ToString();
            using var _ = JsonDocument.Parse(json);
            return (true, json, null);
        }
        catch (Exception ex)
        {
            return (false, "", "Falha de conversão do modelo: " + ex.Message);  // RF-299
        }
    }

    internal static string ConvertValue(string v)
    {
        string t = v.Trim();
        if (t.Length >= 2 && t.StartsWith("\"") && t.EndsWith("\"")) return t;
        if (t is "true" or "false" or "null") return t;
        if (t.StartsWith("[") && t.EndsWith("]"))
        {
            var inner = SplitTop(t[1..^1]);
            return "[" + string.Join(",", inner.Select(ConvertValue)) + "]";
        }
        if (double.TryParse(t, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _)) return t;
        return JsonSerializer.Serialize(t);
    }

    /// <summary>Divide por vírgulas de topo, respeitando aspas e []{}.</summary>
    internal static List<string> SplitTop(string s)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inStr = false;
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) inStr = !inStr;
            else if (!inStr && (c == '[' || c == '{')) depth++;
            else if (!inStr && (c == ']' || c == '}')) depth--;
            else if (!inStr && c == ',' && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }

    /// <summary>
    /// RF-300: descobre a chave de {RESULT_TEXT} no modelo e procura
    /// recursivamente na resposta real, em qualquer nível.
    /// </summary>
    public static string? DiscoverKey(string resTemplate)
    {
        var m = System.Text.RegularExpressions.Regex.Match(resTemplate,
            "\"([^\"]+)\"\\s*:\\s*\"\\{RESULT_TEXT\\}\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    public static string? FindRecursive(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Name == key) return JsonText(prop.Value);
                var inner = FindRecursive(prop.Value, key);
                if (inner is not null) return inner;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var inner = FindRecursive(item, key);
                if (inner is not null) return inner;
            }
        }
        return null;
    }

    /// <summary>Texto do elemento: string sem as aspas do JSON.</summary>
    internal static string JsonText(JsonElement el) =>
        el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.ToString();

    /// <summary>RF-301: `nome: valor`; malformadas registradas e ignoradas.</summary>
    public static Dictionary<string, string> ParseHeaders(IEnumerable<string> lines)
    {
        var headers = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            int c = line.IndexOf(':');
            // Sem o valor no registro (pode conter segredo).
            if (c <= 0) { System.Diagnostics.Trace.WriteLine("GORT header: linha malformada ignorada"); continue; }
            headers[line[..c].Trim()] = line[(c + 1)..].Trim();
        }
        return headers;
    }
}

/// <summary>
/// Serviço de preset de API personalizada (RF-295..306): corpo por modelo,
/// resposta por chave recursiva, cabeçalhos. Formato padrão RF-292..294
/// quando o preset é nulo (nome, texto, códigos; erro ≠ "0" falha; vetor
/// concatena).
/// </summary>
public sealed class CustomApiService : HttpTranslator
{
    public override string Id => "custom";
    public override string DefaultToken => RemoteDefaults.DefaultToken;

    private readonly CustomPreset? _preset;
    private readonly Func<string> _baseUrl;

    public override string Display { get; }

    public CustomApiService(CustomPreset? preset, Func<string>? baseUrl,
        HttpMessageHandler? handler = null)
        : base(handler, 15000)
    {
        _preset = preset;
        _baseUrl = baseUrl ?? (() => "");
        Display = preset is not null ? "Custom – " + preset.Name : "API personalizada";  // RF-306
    }

    public CustomApiService(HttpMessageHandler? handler = null)
        : this(null, null, handler) { }

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        string text = texts.Count > 0 ? texts[0] : "";
        try
        {
            if (_preset is null)
            {
                // RF-292: nome, texto, destino, origem.
                string body = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["name"] = srcCode + dstCode,
                    ["text"] = text,
                    ["result_code"] = dstCode,
                    ["source_code"] = srcCode,
                });
                string url = _baseUrl();
                if (url == "") return Fail("Informe a URL da API personalizada.");
                using var resp = await PostJsonAsync(url, body,
                    new Dictionary<string, string>(), ct).ConfigureAwait(false);
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseStandard(json);
            }
            string req = TemplateEngine.Substitute(_preset.ReqTemplate, text, srcCode, dstCode);
            var (ok, jsonBody, err) = TemplateEngine.BuildJson(req);
            if (!ok) return Fail(err ?? "Modelo inválido.");
            using var resp2 = await PostJsonAsync(_preset.Url, jsonBody,
                TemplateEngine.ParseHeaders(_preset.Headers), ct).ConfigureAwait(false);
            string json2 = await resp2.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp2.IsSuccessStatusCode)
                return Fail(json2.Length > 200 ? json2[..200] : json2);
            string? key = TemplateEngine.DiscoverKey(_preset.ResTemplate);
            if (key is null) return Fail("Chave de resultado não encontrada no modelo.");
            try
            {
                using var doc = JsonDocument.Parse(json2);
                string? found = TemplateEngine.FindRecursive(doc.RootElement, key);
                return found is null
                    ? Fail($"Chave '{key}' ausente na resposta.")
                    : new ServiceResult { Translations = new List<string> { found } };
            }
            catch { return Fail("JSON inválido na resposta."); }
        }
        catch (OperationCanceledException) { return CancelOrTimeout(ct); }
        catch (Exception ex) { return Fail("Falha de processamento: " + ex.Message); }
    }

    /// <summary>RF-293/294: erro ≠ "0" falha; vetor concatena.</summary>
    internal static ServiceResult ParseStandard(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string code = root.TryGetProperty("error", out var e)
                ? TemplateEngine.JsonText(e) : "0";
            if (code != "0")
            {
                string msg = root.TryGetProperty("message", out var m)
                    ? TemplateEngine.JsonText(m) : "";
                return Fail(msg);
            }
            if (!root.TryGetProperty("result", out var r))
                return Fail("Campo de resultado ausente.");
            if (r.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var el in r.EnumerateArray()) parts.Add(TemplateEngine.JsonText(el));
                return new ServiceResult { Translations = new List<string> { string.Concat(parts) } };
            }
            return new ServiceResult { Translations = new List<string> { TemplateEngine.JsonText(r) } };
        }
        catch { return Fail("JSON inválido na resposta."); }
    }

    /// <summary>
    /// Presets em arquivo (RF-302..304): um preset ou lista; arquivos vencem
    /// a lista editável; duplicados no arquivo ignorados com registro.
    /// </summary>
    public static List<CustomPreset> LoadPresetFiles(string dir)
    {
        var list = new List<CustomPreset>();
        var seen = new HashSet<string>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var items = new List<JsonElement>();
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var el in doc.RootElement.EnumerateArray()) items.Add(el);
                else items.Add(doc.RootElement);
                foreach (var el in items)
                {
                    string name = el.TryGetProperty("name", out var n)
                        ? n.GetString() ?? "" : "";
                    if (name == "" || !seen.Add(name))
                    {
                        System.Diagnostics.Trace.WriteLine("GORT preset duplicado: " + name);
                        continue;                                    // RF-304
                    }
                    list.Add(new CustomPreset
                    {
                        Name = name,
                        Url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                        Headers = el.TryGetProperty("headers", out var h)
                            ? h.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                            : new List<string>(),
                        ReqTemplate = el.TryGetProperty("request", out var rq)
                            ? rq.GetString() ?? "" : "",
                        ResTemplate = el.TryGetProperty("response", out var rp)
                            ? rp.GetString() ?? "" : "",
                        FromFile = true,                             // RF-303
                    });
                }
            }
            catch { }
        }
        return list;
    }

    /// <summary>RF-305: duplicados da interface ganham sufixo (n).</summary>
    public static string UniqueName(List<CustomPreset> list, string name)
    {
        if (!list.Any(p => p.Name == name)) return name;
        int n = 2;
        while (list.Any(p => p.Name == $"{name} ({n})")) n++;
        return $"{name} ({n})";
    }
}
