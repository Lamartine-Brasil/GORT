using System;
using System.IO;
using Gort.Audio;
using Gort.Config;
using Gort.Core;
using Gort.Store;
using Gort.Translate;

namespace Gort.Loop;

/// <summary>
/// Efeitos reais do ciclo: memória de exibição (Etapa 10), cópia para a área
/// de transferência (RF-473/474), gravação em arquivo (RF-496) e TTS.
/// </summary>
public sealed class RealEffects : ILoopEffects
{
    private readonly DisplayMemory _memory;
    private readonly ConfigService _cfg;
    private readonly SpeechService _speech;
    private string _lastCopied = "";
    private readonly object _gate = new();

    public RealEffects(DisplayMemory memory, ConfigService cfg, SpeechService speech)
    {
        _memory = memory; _cfg = cfg; _speech = speech;
    }

    public string ApplyDisplayMemory(string display) =>
        _cfg.Advanced.DisplayMemory
            ? _memory.Apply(display, DateTime.UtcNow) : display;

    public void SideEffects(string display, string recognized)
    {
        var p = _cfg.Profile;
        CopyOut(display, recognized, p);                 // RF-473/474
        if (p.SaveResultFile) AppendFile(recognized, display);   // RF-496
        if (p.Tts) _speech.Speak(display, p.TtsWait,      // RF-476/477
            Translate.RemoteDefaults.DefaultToken);
    }

    /// <summary>
    /// RF-473: só reconhecido / só tradução / ambos. Só quando mudou e com a
    /// área livre; falhas ignoradas; suspensa com o editor aberto (RF-475).
    /// </summary>
    internal void CopyOut(string display, string recognized, Profile p)
    {
        if (!p.CopyToClipboard) return;
        var app = Avalonia.Application.Current as global::Gort.App;
        if (app is not null && app.DictEditorOpen) return;   // RF-475
        string text = p.CopyFormat switch
        {
            "translation-only" => display,
            "both" => recognized + "\n\n" + display,
            _ => recognized,
        };
        lock (_gate)
        {
            if (text == _lastCopied) return;             // RF-474: mudou
            try
            {
                TextCopy.ClipboardService.SetText(text);
                _lastCopied = text;
            }
            catch { /* área ocupada: ignora silenciosamente */ }
        }
    }

    internal void AppendFile(string recognized, string display)
    {
        try
        {
            string path = Path.Combine(Paths.BaseDir, "results.txt");
            File.AppendAllText(path,
                "/s\n" + recognized + "\n/t\n" + display + "\n/e\n\n");
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("GORT results: " + ex.Message); }
    }
}
