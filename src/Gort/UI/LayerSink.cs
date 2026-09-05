using System;
using System.Collections.Generic;
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

    public IReadOnlyList<Platform.ScreenRect> OutputOccluders()
    {
        // Mesmo padrão de OwnWindowRects (App): leitura direta com tolerância,
        // na thread do laço. Só a janela visível oclui.
        try
        {
            var w = _win;
            if (!w.IsVisible) return Array.Empty<Platform.ScreenRect>();
            // Escala da tela que contém a janela (não a primária: DPI misto
            // errava o oclusor e o OCR relia a própria saída).
            double s = 1.0;
            try
            {
                s = w.Screens.ScreenFromWindow(w)?.Scaling
                    ?? w.Screens.Primary?.Scaling ?? 1.0;
            }
            catch { }
            var p = w.Position;
            return new[]
            {
                new Platform.ScreenRect(p.X, p.Y,
                    Math.Max(1, (int)(w.Width * s)),
                    Math.Max(1, (int)(w.Height * s))),
            };
        }
        catch { return Array.Empty<Platform.ScreenRect>(); }
    }
}
