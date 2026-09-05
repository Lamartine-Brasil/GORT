using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;

namespace Gort.Update;

/// <summary>
/// Atualização menor/maior (RF-420..432): verifica, baixa soma (RF-423),
/// delega ao ajudante em processo separado (RF-425) e encerra.
/// Ditames de dicionário (RF-433/434).
/// </summary>
public static class Updater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public enum Verdict { None, Minor, Major }

    public static async Task<(Verdict Kind, VersionFile File)> CheckAsync(
        string localVersion, CancellationToken ct)
    {
        string text = await Http.GetStringAsync(Dist.VersionUrl, ct).ConfigureAwait(false);
        var vf = VersionFile.Parse(text);
        if (vf.Version == "" || VersionFile.Compare(localVersion, vf.Version) >= 0)
            return (Verdict.None, vf);
        bool minor = VersionFile.IsMinor(localVersion, vf.Version, vf.InlineMin);
        return (minor ? Verdict.Minor : Verdict.Major, vf);
    }

    /// <summary>
    /// RF-422/423: aborta se a config publicada diverge ou sem soma.
    /// RF-425: inicia o ajudante e pede o encerramento do principal.
    /// </summary>
    public static async Task<bool> StartMinorAsync(string localVersion, VersionFile vf,
        Action<string> progress, CancellationToken ct)
    {
        try
        {
            if (FailMarker.InWait()) return false;               // RF-428
            string cfg = await Http.GetStringAsync(
                VersionFile.ForceHttps(Dist.RemoteConfigUrl), ct).ConfigureAwait(false);
            if (!RemoteConfigMatches(cfg, vf.Version)) return false;   // RF-422
            if (vf.SumUrl == "") return false;                   // RF-423: sem soma
            string exe = System.Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "Gort.exe");
            var psi = new System.Diagnostics.ProcessStartInfo(exe,
                $"--do-update \"{vf.Version}\" " +
                $"\"{VersionFile.ForceHttps(vf.ExeUrl)}\" " +
                $"\"{VersionFile.ForceHttps(Dist.DownloadPage)}\" " +
                $"\"{VersionFile.ForceHttps(vf.SumUrl)}\"")
            {
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);               // RF-425: auxiliar
            return true;
        }
        catch { return false; }
    }

    internal static bool RemoteConfigMatches(string cfg, string version)
    {
        // A config publicada declara a versão em linha `{app-version}X`.
        foreach (var line in cfg.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith("{app-version}"))
                return t["{app-version}".Length..].Trim() == version;
        }
        return false;
    }

    /// <summary>RF-433: dicionários padrão (um por idioma).</summary>
    public static async Task CheckDictsAsync(VersionFile vf, CancellationToken ct)
    {
        try
        {
            var versions = DataVersions.Load();
            foreach (var (lang, (ver, url)) in vf.Dicts)
            {
                if (versions.GetValueOrDefault(lang) == ver) continue;
                string text = await Http.GetStringAsync(
                    VersionFile.ForceHttps(url), ct).ConfigureAwait(false);
                string dest = Path.Combine(Paths.DictDir,
                    lang == "ja" ? "jaDic.txt" : "myDic.txt");
                // RF-433: UTF-8 sem BOM; tmp+rename contra queda no meio.
                string tmp = dest + ".tmp";
                await File.WriteAllTextAsync(tmp, text,
                    new System.Text.UTF8Encoding(false), ct).ConfigureAwait(false);
                File.Move(tmp, dest, overwrite: true);
                versions[lang] = ver;
            }
            DataVersions.Save(versions);
        }
        catch { }
    }
}

/// <summary>Versões de dados instalados (RF-433/435).</summary>
public static class DataVersions
{
    private static string Path =>
        System.IO.Path.Combine(Core.Paths.BaseDir, "data-versions.json");

    public static System.Collections.Generic.Dictionary<string, string> Load()
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path));
            var d = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                d[p.Name] = p.Value.GetString() ?? "";
            return d;
        }
        catch { return new(); }
    }

    public static void Save(System.Collections.Generic.Dictionary<string, string> d)
    {
        try
        {
            Directory.CreateDirectory(Core.Paths.BaseDir);
            File.WriteAllText(Path, System.Text.Json.JsonSerializer.Serialize(d));
        }
        catch { }
    }
}

/// <summary>
/// Ajudante de atualização (processo separado — RF-425..432): baixa com
/// progresso, verifica a soma, substitui com backup e tentativas, oferece
/// notas e reinício.
/// </summary>
public static class UpdateHelper
{
    public static async Task<int> RunAsync(string[] args, Action<string> log)
    {
        // args: version exeUrl notesUrl sumUrl
        if (args.Length < 5) return 1;
        string version = args[1], exeUrl = args[2], notesUrl = args[3], sumUrl = args[4];
        string dir = AppContext.BaseDirectory;
        string exeName = Path.GetFileName(Environment.ProcessPath ?? "Gort.exe");
        string current = Path.Combine(dir, exeName);
        string tmpExe = Path.Combine(dir, exeName + ".new");
        string tmpCfg = Path.Combine(dir, "config.new");
        string backup = Path.Combine(dir, exeName + ".bak");      // RF-431
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            log("Baixando " + version);
            await DownloadAsync(http, exeUrl, tmpExe, log);
            await DownloadAsync(http, VersionFile.ForceHttps(
                Dist.RemoteConfigUrl), tmpCfg, log);
            string expected = (await http.GetStringAsync(sumUrl)).Trim();
            if (expected == "" || !VersionFile.ShaOk(tmpExe, expected))  // RF-427
            {
                try { File.Delete(tmpExe); File.Delete(tmpCfg); } catch { }
                FailMarker.Write();                               // RF-428
                log("Soma de verificação divergente. Arquivos apagados.");
                return 2;
            }
            MoveWithRetry(current, backup);                       // RF-430
            try
            {
                MoveWithRetry(tmpExe, current);
            }
            catch
            {
                // Volta o backup: nunca deixar a pasta sem executável.
                try { MoveWithRetry(backup, current); } catch { }
                throw;
            }
            try { File.Delete(tmpCfg); } catch { }
            try { File.Delete(backup); } catch { }
            log("Atualizado para " + version + ". Notas: " + notesUrl);
            return 0;
        }
        catch (Exception ex)
        {
            log("Falha: " + ex.Message);
            return 3;
        }
    }

    private static async Task DownloadAsync(HttpClient http, string url,
        string dest, Action<string> log)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long? total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long done = 0;
        int n;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            done += n;
            if (total > 0) log($"{done * 100 / total.Value}%");   // RF-426
        }
    }

    internal static void MoveWithRetry(string from, string to)
    {
        Exception? last = null;
        for (int i = 0; i < Params.P117_MoveTries; i++)           // P-117
        {
            try { File.Move(from, to, overwrite: true); return; }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(Params.P118_MoveIntervalMs);         // P-118
            }
        }
        throw new IOException("Não foi possível mover arquivo.", last);
    }
}
