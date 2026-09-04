using System.Collections.Generic;
using System.IO;
using Gort.Core;

namespace Gort.Text;

/// <summary>
/// Dicionário de correção (RF-181..185): pares texto→texto em arquivo
/// (`/s`, original, corrigido, linha em branco), com passagens encadeadas
/// (RF-182, P-46) e modo por-palavra (RF-183).
/// </summary>
public sealed class DictionaryStore
{
    private readonly List<(string From, string To)> _pairs = new();

    public int Count => _pairs.Count;

    /// <summary>Carrega o formato RF-185. Ausente → vazio, sem erro.</summary>
    public void Load(string path)
    {
        _pairs.Clear();
        if (!File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return; }
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i] != "/s") continue;
            if (i + 2 >= lines.Length) break;
            _pairs.Add((lines[i + 1], lines[i + 2]));
            i += 2;
        }
    }

    /// <summary>Acrescenta o par e recarrega (RF-184: editor rápido).</summary>
    public void AddPair(string from, string to, string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.AppendAllText(path, "/s\n" + from + "\n" + to + "\n\n");
        }
        catch { return; }
        Load(path);
    }

    /// <summary>
    /// Aplica ao texto: 1 passagem + até <paramref name="extraPasses"/> (P-46).
    /// Cada passagem aplica todos os pares de uma vez sobre o texto de entrada
    /// da passagem (sem encadeamento intra-passagem); o encadeamento a→b→c
    /// exige passagens extras (RF-182). Por palavra → só em limites de palavra.
    /// </summary>
    public string Apply(string text, bool byWord, int extraPasses)
    {
        if (_pairs.Count == 0) return text;
        // Mais longo primeiro: prefere o padrão mais específico em sobreposição.
        var ordered = new List<(string From, string To)>();
        foreach (var p in _pairs)
            if (!string.IsNullOrEmpty(p.From)) ordered.Add(p);
        if (ordered.Count == 0) return text;
        ordered.Sort((a, b) => b.From.Length.CompareTo(a.From.Length));
        string alt = string.Join("|", ordered.ConvertAll(p =>
            System.Text.RegularExpressions.Regex.Escape(p.From)));
        string pattern = byWord ? @"\b(" + alt + @")\b" : "(" + alt + ")";
        var map = new Dictionary<string, string>();
        foreach (var (from, to) in ordered)
            if (!map.ContainsKey(from)) map[from] = to;
        var rx = new System.Text.RegularExpressions.Regex(pattern);
        string cur = text;
        int passes = 1 + System.Math.Clamp(extraPasses, 0, 3);
        for (int p = 0; p < passes; p++)
            cur = rx.Replace(cur, m => map[m.Groups[1].Value]);
        return cur;
    }
}
