using System.Collections.Generic;

namespace Gort.Translate;

/// <summary>
/// Tabela de idiomas para tradução (RF-308..RF-316): chave, nome, código OCR
/// e códigos por serviço. Escopo inicial ja/en→pt-BR (RF-309); tabela é dado
/// (RF-310). Código vazio = serviço não oferece (RF-308).
/// </summary>
public static class LangCodes
{
    public sealed class Entry
    {
        public string Key = "";
        public string Ocr = "";
        public string NameEn = "";
        public Dictionary<string, string> Svc = new();
        public bool SeparatesWords;
        public bool Vertical;
        public bool Rtl;
    }

    public static readonly List<Entry> All =
    [
        new() {
            Key = "ja", Ocr = "jpn", NameEn = "Japanese",
            Svc = new() {
                ["web-free"] = "ja", ["custom"] = "ja",
                ["commercial-kr"] = "ja", ["web-nokey"] = "ja",
                ["sheets"] = "ja", ["embedded-browser"] = "ja",
                ["commercial-eu"] = "JA", ["llm"] = "Japanese",
                ["local-worker"] = "ja",
            },
            SeparatesWords = false, Vertical = true, Rtl = false,
        },
        new() {
            Key = "en", Ocr = "eng", NameEn = "English",
            Svc = new() {
                ["web-free"] = "en", ["custom"] = "en",
                ["commercial-kr"] = "en", ["web-nokey"] = "en",
                ["sheets"] = "en", ["embedded-browser"] = "en",
                ["commercial-eu"] = "EN", ["llm"] = "English",
                ["local-worker"] = "en",
            },
            SeparatesWords = true, Vertical = false, Rtl = false,
        },
        new() {
            Key = "pt-BR", Ocr = "por", NameEn = "Portuguese (Brazil)",
            Svc = new() {
                ["web-free"] = "pt", ["custom"] = "pt-BR",
                ["commercial-kr"] = "pt", ["web-nokey"] = "pt",
                ["sheets"] = "pt", ["embedded-browser"] = "pt",
                ["commercial-eu"] = "PT-BR", ["llm"] = "Portuguese (Brazil)",
                ["local-worker"] = "pt-BR",
            },
            SeparatesWords = true, Vertical = false, Rtl = false,
        },
    ];

    public const string DefaultTarget = "pt-BR";   // RF-314

    /// <summary>Código do idioma para o serviço (RF-315: vazio = não oferece).</summary>
    public static string CodeFor(string serviceId, string langKey)
    {
        // Snapshot: All é mutável (testes adicionam "xx") — evita
        // InvalidOperationException em enumeração concorrente.
        foreach (var e in All.ToArray())
            if (e.Key == langKey && e.Svc.TryGetValue(serviceId, out var c)) return c;
        return "";
    }

    /// <summary>Chave pelo código OCR (RF-147: propaga OCR→tradução).</summary>
    public static string KeyForOcr(string ocrCode)
    {
        foreach (var e in All.ToArray())
            if (e.Ocr == ocrCode) return e.Key;
        return "";
    }

    /// <summary>en ≡ en-US (RF-316).</summary>
    public static bool SameCode(string a, string b)
    {
        if (a == b) return true;
        return a.Split('-')[0] == b.Split('-')[0];
    }

    public static bool IsRtl(string langKey)
    {
        foreach (var e in All)
            if (e.Key == langKey) return e.Rtl;
        return false;
    }
}
