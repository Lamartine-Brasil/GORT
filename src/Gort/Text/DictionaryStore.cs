using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Gort.Text;

/// <summary>
/// Dicionário de correção (RF-181..185): pares texto→texto em arquivo
/// (`/s`, original, corrigido, linha em branco), com passagens encadeadas
/// (RF-182, P-46) e modo por-palavra (RF-183).
/// </summary>
public sealed class DictionaryStore
{
    private readonly List<(string From, string To)> _pairs = new();
    private int _version;   // incrementado a cada mutação; invalida o cache
    private readonly Dictionary<bool, (int Ver, Regex Rx, Dictionary<string, string> Map)> _cache = new();
    // Laço lê (Step) enquanto a UI recarrega (ReloadDict): sem trava o
    // Clear/Add concorrente derrubava a thread do laço (RF-009).
    private readonly object _gate = new();

    public int Count => _pairs.Count;

    /// <summary>Carrega o formato RF-185. Ausente → vazio, sem erro.</summary>
    public void Load(string path)
    {
        string[] lines;
        try
        {
            if (!File.Exists(path)) lines = [];
            else lines = File.ReadAllLines(path);
        }
        catch { return; }
        lock (_gate)
        {
            _pairs.Clear();
            _version++;
            _cache.Clear();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != "/s") continue;
                if (i + 2 >= lines.Length) break;
                _pairs.Add((lines[i + 1], lines[i + 2]));
                i += 2;
            }
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
        lock (_gate)
        {
            if (_pairs.Count == 0) return text;
            if (!_cache.TryGetValue(byWord, out var hit) || hit.Ver != _version)
            {
                // Mais longo primeiro: prefere o padrão mais específico em sobreposição.
                var ordered = new List<(string From, string To)>();
                foreach (var p in _pairs)
                    if (!string.IsNullOrEmpty(p.From)) ordered.Add(p);
                if (ordered.Count == 0) return text;
                ordered.Sort((a, b) => b.From.Length.CompareTo(a.From.Length));
                string alt = string.Join("|", ordered.ConvertAll(p =>
                    Regex.Escape(p.From)));
                string pattern = byWord ? @"\b(" + alt + @")\b" : "(" + alt + ")";
                var map = new Dictionary<string, string>();
                foreach (var (from, to) in ordered)
                    if (!map.ContainsKey(from)) map[from] = to;
                hit = (_version, new Regex(pattern, RegexOptions.Compiled), map);
                _cache[byWord] = hit;
            }
            string cur = text;
            int passes = 1 + System.Math.Clamp(extraPasses, 0, 3);
            var rx = hit.Rx;
            var m = hit.Map;
            for (int p = 0; p < passes; p++)
                cur = rx.Replace(cur, x => m[x.Groups[1].Value]);
            return cur;
        }
    }
}
