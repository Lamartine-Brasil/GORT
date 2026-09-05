using System;
using Avalonia.Threading;

namespace Gort.Clipboard;

/// <summary>
/// Tradução da área de transferência (cap. 24, RF-464..472): monitora texto
/// novo e traduz fora do laço (ocioso, sem sobreposição, texto diferente).
/// Uma por vez (RF-468); "traduzindo" e original opcionais (RF-469/470);
/// exibe pela janela ativa + TTS (RF-471).
/// </summary>
public sealed class ClipboardWatcher
{
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _idle;
    private readonly Func<bool> _overlay;
    private readonly Func<bool> _busy;
    private readonly Func<string, bool> _showOriginal;
    private readonly Func<bool> _showWorking;
    private readonly Func<string, System.Threading.Tasks.Task<string>> _translate;
    private readonly Action<string> _display;
    private readonly Action<string> _speak;
    private readonly DispatcherTimer _timer;
    private string _last = "";
    private bool _working;
    private int _gen;   // Reset() invalida traduções em voo

    public ClipboardWatcher(
        Func<bool> enabled, Func<bool> idle, Func<bool> overlay, Func<bool> busy,
        Func<string, bool> showOriginal, Func<bool> showWorking,
        Func<string, System.Threading.Tasks.Task<string>> translate,
        Action<string> display, Action<string> speak)
    {
        _enabled = enabled; _idle = idle; _overlay = overlay; _busy = busy;
        _showOriginal = showOriginal; _showWorking = showWorking;
        _translate = translate; _display = display; _speak = speak;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    /// <summary>RF-472: aplicar limpa o estado.</summary>
    public void Reset() { _gen++; }

    /// <summary>Para o temporizador (encerramento): sem novas sondagens.</summary>
    public void Stop()
    {
        try { _timer.Stop(); } catch { }
        _gen++;
    }

    /// <summary>RF-467: condições para traduzir pela área de transferência.</summary>
    internal bool ShouldTranslate(string? text) =>
        _enabled() && text is not null && text.Length > 0
        && _idle() && !_busy() && !_overlay() && text != _last && !_working;  // RF-464..468

    private async void Poll()
    {
        string? text;
        try { text = TextCopy.ClipboardService.GetText(); }   // RF-466: só texto
        catch { return; }
        if (!ShouldTranslate(text)) return;
        _working = true;                                     // RF-468: bloqueia
        int gen = _gen;
        try
        {
            _last = text!;
            if (_showWorking()) _display("detectado — traduzindo");   // RF-469
            string tr = await _translate(text!).ConfigureAwait(true);
            if (gen != _gen) return;   // aplicar no meio: descarta o velho
            string out0 = _showOriginal(text!) ? tr + "\n\n" + text : tr;  // RF-470
            _display(out0);                                   // RF-471
            _speak(tr);
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("GORT clipboard: " + ex.Message); }
        finally { _working = false; }
    }
}
