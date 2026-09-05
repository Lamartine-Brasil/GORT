using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Gort.Ocr.Cloud;

/// <summary>
/// Cota mensal por credencial e por mês civil (RF-124/127): zera quando mês
/// ou ano mudam. Limite P-29 (RF-125). Persistida junto da data (RF-127),
/// exibida como "usadas / limite".
/// </summary>
public sealed class CloudQuota
{
    private readonly string _file;
    private readonly Dictionary<string, (int Used, int Year, int Month)> _rows = new();
    private readonly object _gate = new();   // laço × UI concorrentes

    public CloudQuota()
    {
        _file = Path.Combine(Core.Paths.BaseDir, "cloud-usage.json");
        try
        {
            if (File.Exists(_file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_file));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "v") continue;   // versão do esquema (RF-038)
                    var v = prop.Value;
                    _rows[prop.Name] = (
                        v.GetProperty("used").GetInt32(),
                        v.GetProperty("year").GetInt32(),
                        v.GetProperty("month").GetInt32());
                }
            }
        }
        catch { }
    }

    public (int Used, int Limit) Status(string cred, int limit)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            if (_rows.TryGetValue(cred, out var r) && r.Year == now.Year && r.Month == now.Month)
                return (r.Used, limit);
            return (0, limit);   // RF-124: zera ao mudar mês/ano
        }
    }

    public bool TryConsume(string cred, int limit)
    {
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            int used = 0;
            if (_rows.TryGetValue(cred, out var r) && r.Year == now.Year && r.Month == now.Month)
                used = r.Used;
            if (used >= limit) return false;                        // RF-125
            _rows[cred] = (used + 1, now.Year, now.Month);
            Save();
            return true;
        }
    }

    private void Save()
    {
        try
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteNumber("v", 1);   // RF-038: versão do esquema
                foreach (var (k, v) in _rows)
                {
                    w.WriteStartObject(k);
                    w.WriteNumber("used", v.Used);
                    w.WriteNumber("year", v.Year);
                    w.WriteNumber("month", v.Month);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            Directory.CreateDirectory(Core.Paths.BaseDir);
            File.WriteAllBytes(_file, ms.ToArray());
        }
        catch { }
    }
}
