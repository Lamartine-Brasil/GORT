using Avalonia.Threading;
using Gort.Loop;

namespace Gort.UI;

/// <summary>
/// Sink da sobreposição: repassa o quadro ao gerente, que desenha uma
/// janela por região ancorada no retângulo. Repintar redesenha.
/// </summary>
public sealed class OverlaySink : IDisplaySink
{
    private readonly TranslationWindows _wins;
    private OverlayFrame? _last;

    public OverlaySink(TranslationWindows wins)
    {
        _wins = wins;
    }

    // O gerente vive com o programa; janelas escondem, nunca morrem
    // (OnClosing cancela) — sempre vivo durante o laço.
    public bool IsAlive => true;

    public void Draw(string display, string recognized) { }   // overlay usa blocos

    public void DrawOverlay(OverlayFrame frame) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _last = frame;
            _wins.DrawOverlayFrame(frame);
        });

    public void DrawSnapshot(OverlayFrame frame, int staySeconds) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _last = frame;
            _wins.DrawOverlayFrame(frame, staySeconds);
        });

    public void Repaint()
    {
        var f = _last;
        if (f is not null)
            Dispatcher.UIThread.InvokeAsync(() => _wins.DrawOverlayFrame(f));  // RF-197
    }

    public void SetRunning(bool running) =>
        Dispatcher.UIThread.InvokeAsync(() => _wins.SetOverlayRunning(running));
}
