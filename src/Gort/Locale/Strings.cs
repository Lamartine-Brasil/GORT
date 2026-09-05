using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Gort.Locale;

/// <summary>
/// Localização da interface (cap. 26, RF-481..489): tabela em arquivo
/// externo editável (RF-489), CSV com chave + coluna por idioma (RF-482),
/// chave ausente mostra a própria chave (RF-485), pt-BR inicial (RF-487).
/// </summary>
public static class Strings
{
    private static readonly Dictionary<string, Dictionary<string, string>> Table = new();
    private static readonly object Gate = new();
    public static string Language { get; private set; } = "pt-BR";
    public static string FilePath { get; private set; } = "";

    public static void Load(string path, string language)
    {
        List<(string Key, List<string> Cols)> parsed;
        try
        {
            parsed = File.Exists(path) ? ParseCsv(File.ReadAllText(path)) : new List<(string Key, List<string> Cols)>();
        }
        catch { parsed = new List<(string Key, List<string> Cols)>(); }   // ilegível: cai p/ chaves
        lock (Gate)
        {
            Table.Clear();
            FilePath = path;
            Language = language;
            foreach (var (key, cols) in parsed)
            {
                var map = new Dictionary<string, string>();
                for (int i = 0; i < cols.Count; i++)
                    map["lang" + i] = cols[i];
                Table[key] = map;
            }
        }
    }

    /// <summary>Acrescentar idioma = acrescentar coluna de dados (RF-483).</summary>
    public static void AddLanguageColumn() { }

    public static string Get(string key)
    {
        lock (Gate)
        {
            if (Table.TryGetValue(key, out var cols))
            {
                // Coluna 0 = pt-BR (única nesta versão — RF-483/487).
                if (cols.TryGetValue("lang0", out var v) && v.Length > 0) return v;
            }
        }
        return key;   // RF-485: falta visível
    }

    public static string _(string key) => Get(key);

    internal static List<(string Key, List<string> Cols)> ParseCsv(string text)
    {
        var rows = new List<(string, List<string>)>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false;
        string? key = null;
        var cols = new List<string>();
        void EndCell()
        {
            if (key is null) { key = cell.ToString(); }
            else cols.Add(cell.ToString());
            cell.Clear();
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else inQuotes = false;
                }
                else cell.Append(c);   // RF-482: vírgulas e quebras entre aspas
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') EndCell();
            else if (c == '\r') { }
            else if (c == '\n') { EndCell(); rows.Add((key ?? "", cols)); key = null; cols = new(); }
            else cell.Append(c);
        }
        EndCell();
        if (key is not null && (key.Length > 0 || cols.Count > 0)) rows.Add((key, cols));
        return rows;
    }

    /// <summary>Deriva o idioma do SO com queda para pt-BR (RF-484).</summary>
    public static string SystemLanguage()
    {
        try
        {
            string tag = System.Globalization.CultureInfo.CurrentUICulture.Name;
            if (tag.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
        }
        catch { }
        return "pt-BR";
    }
}
