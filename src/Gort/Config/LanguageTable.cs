using System.Collections.Generic;

namespace Gort.Config;

/// <summary>
/// Tabela de idiomas (RF-308..RF-316). Escopo inicial: ja/en → pt-BR (RF-309).
/// Dados, não código (RF-029, RF-566): acrescentar idioma = nova entrada.
/// </summary>
public sealed class LanguageInfo
{
    public required string Key { get; init; }          // identificador textual estável (RF-027)
    public required string OcrCode { get; init; }
    public bool SeparatesWords { get; init; }          // RF-311
}

public static class LanguageTable
{
    public static readonly List<LanguageInfo> All =
    [
        new() {
            Key = "ja", OcrCode = "jpn",
            SeparatesWords = false,
        },
        new() {
            Key = "en", OcrCode = "eng",
            SeparatesWords = true,
        },
        new() {
            Key = "pt-BR", OcrCode = "por",
            SeparatesWords = true,
        },
    ];

    public static LanguageInfo? Find(string key)
    {
        foreach (var l in All)
            if (string.Equals(l.Key, key, System.StringComparison.OrdinalIgnoreCase)) return l;
        return null;
    }
}
