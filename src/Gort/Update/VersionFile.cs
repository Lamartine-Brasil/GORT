using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Gort.Update;

/// <summary>
/// Endereços de distribuição (dado, VI.10). Comunidade idem.
/// Dono: Lamartine Barbosa — tudo no GitHub dele por enquanto; os
/// arquivos de distribuição (version.txt etc.) ele publica depois.
/// Até lá, atualização/comunidade mostram "sem rede" com elegância.
/// </summary>
public static class Dist
{
    private const string GitHub = "https://github.com/Lamartine-Brasil";
    public const string VersionUrl = GitHub;
    public const string RemoteConfigUrl = GitHub;
    public const string CommunityIndexUrl = GitHub;
    public const string CommunityBase = GitHub;
    public const string DownloadPage = GitHub;
}

/// <summary>
/// Arquivo de versão (VI.10): seções [app]/[dicts], entradas {chave}valor.
/// </summary>
public sealed class VersionFile
{
    public string Version = "";
    public string InlineMin = "";
    public string ExeUrl = "";
    public string SumUrl = "";
    public string NotesUrl = "";
    public Dictionary<string, (string Ver, string Url)> Dicts = new();

    public static VersionFile Parse(string text)
    {
        var vf = new VersionFile();
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith("[") && line.EndsWith("]"))
            { section = line[1..^1]; continue; }
            if (!line.StartsWith("{")) continue;
            int end = line.IndexOf('}');
            if (end < 0) continue;
            string key = line[1..end];
            string val = line[(end + 1)..].Trim();
            if (section == "app")
            {
                if (key == "version") vf.Version = val;
                else if (key == "inline-min") vf.InlineMin = val;
                else if (key == "url-exe") vf.ExeUrl = val;
                else if (key == "url-sum") vf.SumUrl = val;
                else if (key == "url-notes") vf.NotesUrl = val;
            }
            else if (section == "dicts" && key.StartsWith("dict-"))
            {
                // dict-ja = 3 https://...
                var parts = val.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2) vf.Dicts[key["dict-".Length..]] = (parts[0], parts[1]);
            }
        }
        return vf;
    }

    /// <summary>
    /// RF-420: menor = local dentro da faixa atualizável; senão maior.
    /// Compara por segmentos numéricos.
    /// </summary>
    public static bool IsMinor(string local, string remote, string inlineMin)
    {
        if (Compare(local, remote) >= 0) return false;   // sem novidade
        if (inlineMin == "") return true;
        return Compare(local, inlineMin) >= 0;
    }

    internal static int Compare(string a, string b)
    {
        var pa = a.Split('.', '-', '+');
        var pb = b.Split('.', '-', '+');
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length && int.TryParse(pa[i], out var vx) ? vx : 0;
            int y = i < pb.Length && int.TryParse(pb[i], out var vy) ? vy : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    public static string ForceHttps(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? "https://" + url["http://".Length..] : url;   // RF-424

    public static bool ShaOk(string path, string expected)
    {
        try
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            string got = Convert.ToHexString(sha.ComputeHash(fs));
            return string.Equals(got, expected.Trim(),   // RF-427: sem caso
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

/// <summary>
/// Marcador de falha de integridade (RF-428/429): bloqueia novas tentativas
/// por P-116. Ausente, malformado ou futuro = sem espera.
/// </summary>
public static class FailMarker
{
    public static string Path =>
        System.IO.Path.Combine(Core.Paths.BaseDir, "update-failed.txt");

    public static void Write()
    {
        try
        {
            File.WriteAllText(Path, DateTime.UtcNow.ToString("o",
                System.Globalization.CultureInfo.InvariantCulture));
        }
        catch { }
    }

    public static bool InWait()
    {
        try
        {
            if (!File.Exists(Path)) return false;
            if (!DateTime.TryParse(File.ReadAllText(Path).Trim(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var at)) return false;                    // malformado
            if (at > DateTime.UtcNow.AddMinutes(1)) return false; // futuro
            return (DateTime.UtcNow - at).TotalMinutes < Core.Params.P116_FailWaitMin;  // 🔒 10
        }
        catch { return false; }
    }
}
