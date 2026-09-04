using Avalonia.Threading;
using Gort.Loop;

namespace Gort.UI;

/// <summary>Sink da sobreposição: quadros por blocos; repintar re-renderiza.</summary>
public sealed class OverlaySink : IDisplaySink
{
    private readonly OverlayWindow _win;
    private OverlayFrame? _last;
    private bool _alive = true;

    public OverlaySink(OverlayWindow win)
    {
        _win = win;
        win.Closed += (_, _) => _alive = false;
    }

    public bool IsAlive => _alive;

    public void Draw(string display, string recognized) { }   // overlay usa blocos

    public void DrawOverlay(OverlayFrame frame) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _last = frame;
            _win.DrawOverlay(frame);
        });

    public void DrawSnapshot(OverlayFrame frame, int staySeconds) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _last = frame;
            _win.DrawOverlay(frame, staySeconds);
        });

    public void Repaint()
    {
        var f = _last;
        if (f is not null)
            Dispatcher.UIThread.InvokeAsync(() => _win.DrawOverlay(f));  // RF-197
    }

    public void SetRunning(bool running) =>
        Dispatcher.UIThread.InvokeAsync(() => _win.ApplyRunning(running));
}
