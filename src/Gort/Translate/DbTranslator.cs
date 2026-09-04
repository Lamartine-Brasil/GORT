using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Text;

namespace Gort.Translate;

/// <summary>
/// Banco de dados local (RF-241..243): dicionário em memória carregado no
/// aplicar; sem ele, busca parcial gerenciada (substring, multilinha, sem
/// diferenciar maiúsculas — opções do perfil). Sem resultado → vazio
/// (marcador vira vazio). Não usa memória de resultados (RF-214).
/// </summary>
public sealed class DbTranslator : ITranslationService
{
    public string Id => "db";
    public string Display => "Banco de dados local";
    public string DefaultToken => RemoteDefaults.DefaultToken;
    public bool SupportsBridge => false;
    public bool UsesResultMemory => false;   // RF-214
    public bool UsesCollectanea => false;    // RF-221

    private Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private List<(string O, string T)> _pairs = new();
    private bool _ignoreCase;
    private bool _partial;

    public void Reload(string dbFile, bool ignoreCase, bool partialMultiLine)
    {
        _ignoreCase = ignoreCase;
        _partial = partialMultiLine;
        var cmp = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _exact = new Dictionary<string, string>(cmp);
        _pairs = new List<(string, string)>();
        string path = Path.IsPathRooted(dbFile) ? dbFile : Path.Combine(Paths.BaseDir, dbFile);
        if (!File.Exists(path)) return;
        string text;
        try { text = File.ReadAllText(path); }
        catch { return; }
        foreach (var (o, t) in ResultMemory.Parse(text))
        {
            if (!_exact.ContainsKey(o)) _exact[o] = t;
            _pairs.Add((o, t));
        }
    }

    public string? Lookup(string text)
    {
        if (_exact.TryGetValue(text, out var t)) return Done(t);
        if (_partial)
        {
            var cmp = _ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var (o, tr) in _pairs)
                if (o.Contains(text, cmp) || text.Contains(o, cmp)) return Done(tr);
        }
        return null;
    }

    private static string? Done(string t) =>
        t == TextPipeline.NoResultMarker ? null : t;   // RF-241: marcador → vazio

    public Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        // Sem nenhum par carregado: avisa em vez de devolver vazio mudo
        // (o usuário acha que "não funcionou").
        if (_exact.Count == 0 && _pairs.Count == 0)
            return Task.FromResult(new ServiceResult
            {
                Error = "Banco de dados vazio ou ausente. Informe o arquivo na seção do banco.",
            });
        // O lote chega unido por token; consulta por parte e rejunta.
        string joined = texts.Count > 0 ? texts[0] : "";
        var parts = joined.Split(new[] { DefaultToken }, StringSplitOptions.None);
        var outParts = new List<string>();
        int start = parts.Length > 0 && parts[0] == "" ? 1 : 0;
        for (int i = start; i < parts.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            string q = parts[i].Trim().TrimEnd('\n').Trim();
            outParts.Add(Lookup(q) ?? TextPipeline.NoResultMarker);
        }
        return Task.FromResult(new ServiceResult
        {
            Translations = new List<string>
                { DefaultToken + string.Join(DefaultToken, outParts) },
        });
    }
}
