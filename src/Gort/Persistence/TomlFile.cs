using System;
using System.Collections.Generic;
using System.IO;
using Tomlyn;
using Tomlyn.Model;

namespace Gort.Persistence;

/// <summary>
/// Persistência TOML (RF-023): texto legível, multilinha ("""), comentários (#),
/// schema_version na raiz. Leitor tolerante (RF-024): desconhecido ignora,
/// ausente mantém padrão, exceção restaura padrões. Migrações (RF-038) com
/// preservação de chaves desconhecidas/novas.
/// </summary>
public static class TomlFile
{
    public static (TomlTable Raw, bool Fresh) Load(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                return (new TomlTable(), true);
            var text = File.ReadAllText(path);
            var table = Tomlyn.TomlSerializer.Deserialize<TomlTable>(text);
            return (table, false);
        }
        catch
        {
            return (new TomlTable(), true);   // RF-024: corrompido → padrões
        }
    }

    public static string GetString(TomlTable t, string key, string dflt) =>
        t.TryGetValue(key, out var v) && v is string s ? s : dflt;

    public static bool GetBool(TomlTable t, string key, bool dflt) =>
        t.TryGetValue(key, out var v) && v is bool b ? b : dflt;

    public static int GetInt(TomlTable t, string key, int dflt)
    {
        if (t.TryGetValue(key, out var v))
        {
            if (v is long l) return (int)l;
            if (v is int i) return i;
            if (v is string s && string.IsNullOrWhiteSpace(s)) return 0;  // RF-042 vazio→0
        }
        return dflt;
    }

    public static double GetDouble(TomlTable t, string key, double dflt)
    {
        if (t.TryGetValue(key, out var v))
        {
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return 0;               // RF-042
                if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
            }
        }
        return dflt;
    }

    public static int GetSchema(TomlTable t, int current)
    {
        var v = GetInt(t, "schema_version", current);
        return v <= 0 ? current : v;
    }

    /// <summary>Grava fundindo chaves conhecidas sobre as desconhecidas preservadas (RF-038).</summary>
    public static void Save(string path, TomlTable raw, Dictionary<string, object?> known, int schema)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var merged = new TomlTable();
        foreach (var kv in raw) merged[kv.Key] = kv.Value;   // preserva novas/desconhecidas
        foreach (var kv in known) merged[kv.Key] = kv.Value;
        merged["schema_version"] = (long)schema;
        File.WriteAllText(path, Tomlyn.TomlSerializer.Serialize(merged));
    }
}
