using Avalonia.Threading;
using Gort.Loop;

namespace Gort.UI;

/// <summary>Sink da camada: texto redesenha tudo; repintar re-renderiza.</summary>
public sealed class LayerSink : IDisplaySink
{
    private readonly LayerWindow _win;
    private string _last = "";
    private bool _alive = true;

    public LayerSink(LayerWindow win)
    {
        _win = win;
        win.Closed += (_, _) => _alive = false;
    }

    public bool IsAlive => _alive;

    public void Draw(string display, string recognized)
    {
        _last = display;
        Dispatcher.UIThread.InvokeAsync(() => _win.SetText(display));
    }

    public void Repaint() =>
        Dispatcher.UIThread.InvokeAsync(() => _win.SetText(_last));  // RF-197

    public void SetRunning(bool running) =>
        Dispatcher.UIThread.InvokeAsync(() => _win.ApplyRunning(running));
}
