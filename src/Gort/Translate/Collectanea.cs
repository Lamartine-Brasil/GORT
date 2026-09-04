using System;
using System.Collections.Generic;
using System.IO;
using Gort.Core;

namespace Gort.Translate;

/// <summary>
/// Coletânea do usuário (RF-215..221): arquivos de pares em pasta dedicada,
/// só os existentes permanecem ativos (RF-216, poda na carga), seção de
/// informação no topo em linhas `#`, busca exata ou modo banco (RF-218),
/// banco só para en/ja (RF-219 🔒), ignora-maiúsculas opcional (RF-220),
/// nunca para o próprio DB (RF-221, via UsesCollectanea).
/// Formato de pares: /s origem /t destino /e (igual à memória).
/// </summary>
public sealed class Collectanea
{
    private readonly Func<List<string>> _activeFiles;
    private readonly Func<bool> _asDb;
    private readonly Func<bool> _ignoreCase;

    public Collectanea(Func<List<string>> activeFiles, Func<bool> asDb,
        Func<bool> ignoreCase)
    {
        _activeFiles = activeFiles; _asDb = asDb; _ignoreCase = ignoreCase;
    }

    public static string InfoOf(string path)
    {
        try
        {
            var info = new List<string>();
            foreach (var line in File.ReadLines(path))
            {
                if (line.StartsWith("#")) info.Add(line[1..].Trim());
                else if (line.Trim().Length == 0) continue;
                else break;
            }
            return string.Join("\n", info);
        }
        catch { return ""; }
    }

    public string? TryGet(string source, string ocrLang)
    {
        Core.Paths.EnsureAll();   // RF: pasta ausente → criada
        bool db = _asDb() && (ocrLang == "en" || ocrLang == "ja");  // RF-219 🔒
        bool ic = _ignoreCase();
        foreach (var name in _activeFiles())
        {
            string path = Path.Combine(Paths.CollectDir, name);
            var pairs = CachedPairs(path, name);
            if (pairs is null) continue;                            // RF-216
            if (!db)
            {
                foreach (var (o, t) in pairs)
                    if (Eq(o, source, ic)) return t;                // exata
            }
            else
            {
                foreach (var (o, t) in pairs)
                    if (Contains(o, source, ic) || Contains(source, o, ic)) return t;
            }
        }
        return null;
    }

    // Pares em memória por arquivo, invalidados por data/tamanho. Mesma
    // semântica da leitura direta (RF-216/218/219/220), sem I/O por texto.
    private readonly Dictionary<string, (DateTime Write, long Len,
        Dictionary<string, string> Pairs)> _cache = new();

    private Dictionary<string, string>? CachedPairs(string path, string name)
    {
        System.IO.FileInfo fi;
        try { fi = new System.IO.FileInfo(path); }
        catch { _cache.Remove(name); return null; }
        if (!fi.Exists) { _cache.Remove(name); return null; }       // RF-216
        if (_cache.TryGetValue(name, out var hit)
            && hit.Write == fi.LastWriteTimeUtc && hit.Len == fi.Length)
            return hit.Pairs;
        string text;
        try { text = File.ReadAllText(path); }
        catch { _cache.Remove(name); return null; }
        var pairs = ResultMemory.Parse(text);
        _cache[name] = (fi.LastWriteTimeUtc, fi.Length, pairs);
        return pairs;
    }

    private static bool Eq(string a, string b, bool ic) =>
        ic ? a.Equals(b, StringComparison.OrdinalIgnoreCase) : a == b;

    private static bool Contains(string hay, string needle, bool ic) =>
        hay.Contains(needle, ic ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
