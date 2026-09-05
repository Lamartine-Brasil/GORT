using System;
using System.Diagnostics;

namespace Gort.Audio;

/// <summary>
/// Leitura em voz alta (cap. 25, RF-476..480).
/// - Windows: sintetizador do sistema (SAPI).
/// - macOS: comando `say`.
/// - Linux: `spd-say` (speech-dispatcher) ou `espeak-ng`/`espeak`.
/// Sem backend, inerte sem erro (RF-480). Só no texto mudado (o laço só
/// chama no caminho completo — RF-479). Tokens separadores removidos (RF-478):
/// com espera descarta se a anterior toca; sem espera interrompe (RF-477).
/// </summary>
public sealed class SpeechService : IDisposable
{
    private System.Speech.Synthesis.SpeechSynthesizer? _synth;
    private Process? _cli;
    private readonly object _gate = new();

    /// <summary>Há backend de voz neste SO?</summary>
    public static bool CliToolAvailable
    {
        get
        {
            try
            {
                if (OperatingSystem.IsMacOS())
                    return Platform.Cli.Shell.Which("say") is not null;
                if (OperatingSystem.IsLinux())
                    return Platform.Cli.Shell.Which("spd-say") is not null
                        || Platform.Cli.Shell.Which("espeak-ng") is not null
                        || Platform.Cli.Shell.Which("espeak") is not null;
                return false;
            }
            catch { return false; }
        }
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Ensure();
                    return _synth is not null;
                }
                return CliToolAvailable;
            }
            catch { return false; }                    // RF-480
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void Ensure()
    {
        if (_synth is not null) return;
        _synth = new System.Speech.Synthesis.SpeechSynthesizer();
    }

    /// <summary>
    /// Fala o texto (tokens separadores removidos — RF-478). Com espera:
    /// descarta se a anterior toca; sem espera: interrompe (RF-477).
    /// </summary>
    public void Speak(string text, bool waitPrevious, string token)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string clean = text.Replace(token, "");        // RF-478
        if (string.IsNullOrWhiteSpace(clean)) return;
        try
        {
            lock (_gate)
            {
                if (OperatingSystem.IsWindows())
                {
                    Ensure();
                    if (_synth is null) return;
                    if (waitPrevious && _synth.State ==
                            System.Speech.Synthesis.SynthesizerState.Speaking)
                        return;                            // RF-477: descarta
                    _synth.SpeakAsyncCancelAll();
                    _synth.SpeakAsync(clean);
                    return;
                }
                SpeakCli(clean, waitPrevious);
            }
        }
        catch { }
    }

    private void SpeakCli(string clean, bool waitPrevious)
    {
        try
        {
            bool running = _cli is not null && !_cli.HasExited;
            if (waitPrevious && running) return;       // RF-477: descarta
            try { if (running) _cli?.Kill(); } catch { }
            try { _cli?.Dispose(); } catch { }
            _cli = StartCli(clean);
        }
        catch { }
    }

    private static Process? StartCli(string clean)
    {
        try
        {
            string? exe = null;
            string args;
            if (OperatingSystem.IsMacOS())
            {
                exe = Platform.Cli.Shell.Which("say");
                if (exe is null) return null;
                args = Escape(clean);
            }
            else if (OperatingSystem.IsLinux())
            {
                exe = Platform.Cli.Shell.Which("spd-say")
                    ?? Platform.Cli.Shell.Which("espeak-ng")
                    ?? Platform.Cli.Shell.Which("espeak");
                if (exe is null) return null;
                string name = System.IO.Path.GetFileName(exe);
                args = name == "spd-say" ? $"-w {Escape(clean)}" : Escape(clean);
            }
            else return null;
            var p = new Process
            {
                StartInfo = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            try
            {
                if (p.Start()) return p;
                p.Dispose();
                return null;
            }
            catch { p.Dispose(); return null; }
        }
        catch { return null; }
    }

    private static string Escape(string s)
    {
        if (s.Contains('"') || s.Contains('\n') || s.Contains('\\'))
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return "\"" + s + "\"";
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    _synth?.Dispose();
                    _synth = null;
                }
            }
            catch { _synth = null; }
            try { _cli?.Kill(); } catch { }
            try { _cli?.WaitForExit(2000); } catch { }
            try { _cli?.Dispose(); } catch { }
            _cli = null;
        }
    }
}
