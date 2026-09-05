using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;

namespace Gort.Translate;

/// <summary>
/// Memória de resultados anteriores (cap. 17, RF-206..213): separada por
/// serviço (RF-206), consultada antes da rede (RF-207), um arquivo por serviço
/// (RF-208) no formato /s origem /t destino /e (RF-209, origem com rtrim).
/// Teto P-48: ao atingir, descarta tudo e esvazia (RF-210 🔒). Gravação
/// assíncrona em anexar ao fim do laço (RF-211); durante a gravação a memória
/// se comporta como vazia (RF-212 🔒). Limpeza total (RF-213).
/// </summary>
public sealed class ResultMemory : ITranslationMemory
{
    private readonly Dictionary<string, Dictionary<string, string>> _mem = new();
    private readonly List<(string Service, string Source, string Translated)> _pending = new();
    private readonly object _gate = new();
    private bool _writing;
    private int _epoch;   // ClearAll invalida o lote em voo (não ressuscita arquivo)

    /// <summary>RF-499: a depuração desabilita a limpeza durante a gravação.</summary>
    public bool IsWriting
    {
        get { lock (_gate) return _writing; }
    }

    public static string FileFor(string serviceId)
    {
        // IDs de preset ("custom:X") têm caracteres inválidos: higieniza.
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
        var safe = new System.Text.StringBuilder();
        foreach (char c in $"memory-{serviceId}.txt")
            safe.Append(invalid.Contains(c) ? '_' : c);
        return Path.Combine(Paths.BaseDir, safe.ToString());
    }

    public void LoadAll()
    {
        Paths.EnsureAll();
        lock (_gate)
        {
            _mem.Clear();
            foreach (var f in Directory.GetFiles(Paths.BaseDir, "memory-*.txt"))
            {
                string id = Path.GetFileNameWithoutExtension(f)["memory-".Length..];
                _mem[id] = Cap(Parse(File.ReadAllText(f)));
            }
        }
    }

    /// <summary>Teto P-48 também na carga (arquivo gigante não entra na RAM).</summary>
    internal static Dictionary<string, string> Cap(Dictionary<string, string> d)
    {
        if (d.Count <= Params.P48_MemoryMaxEntries) return d;
        var keep = new Dictionary<string, string>();
        int skip = d.Count - Params.P48_MemoryMaxEntries;
        foreach (var kv in d)
        {
            if (skip-- > 0) continue;
            keep[kv.Key] = kv.Value;
        }
        return keep;
    }

    internal static Dictionary<string, string> Parse(string text)
    {
        var d = new Dictionary<string, string>();
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() != "/s") continue;
            var src = new List<string>();
            int j = i + 1;
            while (j < lines.Length && lines[j].Trim() != "/t") { src.Add(lines[j].TrimEnd()); j++; }
            if (j >= lines.Length) break;
            var dst = new List<string>();
            j++;
            while (j < lines.Length && lines[j].Trim() != "/e") { dst.Add(lines[j]); j++; }
            if (j >= lines.Length) break;
            // RF-209: origem com espaços à direita removidos.
            string key = string.Join("\n", src).TrimEnd();
            d[key] = string.Join("\n", dst);
            i = j;
        }
        return d;
    }

    public string? TryGet(string serviceId, string source)
    {
        lock (_gate)
        {
            if (_writing) return null;                        // RF-212 🔒
            return _mem.TryGetValue(serviceId, out var d)
                && d.TryGetValue(source, out var t) ? t : null;
        }
    }

    public void Store(string serviceId, string source, string translated)
    {
        lock (_gate)
        {
            if (_writing) return;                             // RF-212 🔒
            if (!_mem.TryGetValue(serviceId, out var d))
            { d = new Dictionary<string, string>(); _mem[serviceId] = d; }
            if (d.Count >= Params.P48_MemoryMaxEntries)       // RF-210 🔒
            {
                d.Clear();
                try { File.WriteAllText(FileFor(serviceId), ""); } catch { }
            }
            d[source] = translated;
            _pending.Add((serviceId, source, translated));
        }
    }

    /// <summary>RF-211: anexa as novas entradas ao fim do laço, assíncrono.</summary>
    public Task FlushAsync()
    {
        List<(string Service, string Source, string Translated)> batch;
        int epoch;
        lock (_gate)
        {
            if (_pending.Count == 0) return Task.CompletedTask;
            _writing = true;
            epoch = _epoch;
            batch = new List<(string, string, string)>(_pending);
            _pending.Clear();
        }
        return Task.Run(() =>
        {
            try
            {
                lock (_gate)
                {
                    // Limpeza no meio do caminho: descarta o lote em vez de
                    // ressuscitar o arquivo apagado.
                    if (epoch != _epoch) return;
                }
                var bySvc = new Dictionary<string, List<(string, string)>>();
                foreach (var (svc, src, tr) in batch)
                {
                    if (!bySvc.TryGetValue(svc, out var l)) { l = new(); bySvc[svc] = l; }
                    l.Add((src, tr));
                }
                foreach (var (svc, items) in bySvc)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var (src, tr) in items)
                        sb.Append("/s\n").Append(src).Append("\n/t\n")
                          .Append(tr).Append("\n/e\n\n");
                    File.AppendAllText(FileFor(svc), sb.ToString());
                }
            }
            catch { }
            finally { lock (_gate) _writing = false; }
        });
    }

    /// <summary>RF-213: limpa tudo, memória e arquivos.</summary>
    public void ClearAll()
    {
        lock (_gate)
        {
            _mem.Clear();
            _pending.Clear();
            _epoch++;
        }
        foreach (var f in Directory.GetFiles(Paths.BaseDir, "memory-*.txt"))
            try { File.Delete(f); } catch { }
    }

    public int Count(string serviceId)
    {
        lock (_gate) return _mem.TryGetValue(serviceId, out var d) ? d.Count : 0;
    }
}
