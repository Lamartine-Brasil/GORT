using System.Collections.Generic;

namespace Gort.Config;

/// <summary>
/// Tabela de idiomas (RF-308..RF-316). Escopo inicial: ja/en → pt-BR (RF-309).
/// Dados, não código (RF-029, RF-566): acrescentar idioma = nova entrada.
/// </summary>
public sealed class LanguageInfo
{
    public required string Key { get; init; }          // identificador textual estável (RF-027)
    public required string DisplayPtBr { get; init; }
    public required string OcrCode { get; init; }
    public Dictionary<string, string> ServiceCodes { get; init; } = new();
    public bool SeparatesWords { get; init; }          // RF-311
    public bool AllowsVertical { get; init; }          // RF-311
    public bool RightToLeft { get; init; }             // RF-311
}

public static class LanguageTable
{
    public static readonly List<LanguageInfo> All =
    [
        new() {
            Key = "ja", DisplayPtBr = "Japonês", OcrCode = "jpn",
            ServiceCodes = new() { ["web"] = "ja", ["custom"] = "ja" },
            SeparatesWords = false, AllowsVertical = true, RightToLeft = false,
        },
        new() {
            Key = "en", DisplayPtBr = "Inglês", OcrCode = "eng",
            ServiceCodes = new() { ["web"] = "en", ["custom"] = "en" },
            SeparatesWords = true, AllowsVertical = false, RightToLeft = false,
        },
        new() {
            Key = "pt-BR", DisplayPtBr = "Português do Brasil", OcrCode = "por",
            ServiceCodes = new() { ["web"] = "pt", ["custom"] = "pt-BR" },
            SeparatesWords = true, AllowsVertical = false, RightToLeft = false,
        },
    ];

    public const string DefaultTarget = "pt-BR";   // RF-314

    public static LanguageInfo? Find(string key)
    {
        foreach (var l in All)
            if (l.Key == key) return l;
        return null;
    }

    /// <summary>en ≡ en-US (RF-316).</summary>
    public static bool SameCode(string a, string b)
    {
        if (a == b) return true;
        var na = a.Split('-')[0]; var nb = b.Split('-')[0];
        return na == nb;
    }
}
