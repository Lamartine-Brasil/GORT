using System;
using System.Diagnostics;
using System.IO;

namespace Gort.Platform.Cli;

/// <summary>
/// Utilidades de processo externo (captura/voz/janelas no Linux/macOS):
/// localizar ferramenta no PATH e executar com timeout, sem janela.
/// Tudo com try/catch no chamador — ferramenta ausente nunca é erro fatal.
/// </summary>
public static class Shell
{
    /// <summary>Procura executável no PATH (+ caminhos típicos do SO).</summary>
    public static string? Which(string name)
    {
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            char sep = Path.PathSeparator;
            foreach (var dir in pathEnv.Split(sep, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    string cand = Path.Combine(dir.Trim(), name);
                    if (File.Exists(cand)) return cand;
                }
                catch { }
            }
            // Caminhos típicos fora do PATH (brew, sistema).
            string[] extra = OperatingSystem.IsMacOS()
                ? ["/opt/homebrew/bin/" + name, "/usr/local/bin/" + name,
                   "/usr/bin/" + name, "/bin/" + name, "/usr/sbin/" + name]
                : ["/usr/local/bin/" + name, "/usr/bin/" + name, "/bin/" + name,
                   "/usr/sbin/" + name, "/snap/bin/" + name,
                   Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                     + "/.local/bin/" + name];
            foreach (var cand in extra)
                try { if (File.Exists(cand)) return cand; } catch { }
        }
        catch { }
        return null;
    }

    /// <summary>Executa e devolve (saída, código). Timeout em ms.</summary>
    public static (string Out, int Code) Run(string exe, string args, int timeoutMs = 8000)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                // stderr NÃO redirecionado: com redirect e sem drenagem o
                // filho bloqueia ao encher o buffer (deadlock clássico).
                RedirectStandardError = false,
                CreateNoWindow = true,
            };
            p.Start();
            // Lê em background para o timeout valer mesmo se travar.
            var outTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
                return ("", -1);
            }
            string output;
            try { output = outTask.GetAwaiter().GetResult(); }
            catch { return ("", -1); }
            return (output, p.ExitCode);
        }
        catch { return ("", -1); }
    }

    /// <summary>Executa esperando só o término (captura em arquivo). True se saiu 0.</summary>
    public static bool RunToFile(string exe, string args, int timeoutMs = 8000)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
            };
            p.Start();
            var outTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); p.WaitForExit(1000); } catch { }
                return false;
            }
            try { outTask.GetAwaiter().GetResult(); } catch { }
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
