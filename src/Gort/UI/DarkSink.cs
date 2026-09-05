using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Gort.Loop;
using Gort.UI;

namespace Gort.UI;

/// <summary>Adapta o DarkWindow ao laço: desenho na thread de UI.</summary>
public sealed class DarkSink : IDisplaySink
{
    private DarkWindow? _win;
    private bool _alive = true;

    public DarkSink(DarkWindow win)
    {
        _win = win;
        win.Closed += (_, _) => _alive = false;
    }

    public bool IsAlive => _alive && _win is not null;

    public void Draw(string display, string recognized) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var app = AppRef();
            app.LastRecognized = recognized;
            if (_win is null) return;
            _win.ShowTranslation(display, recognized,
                app.Config.Profile.ShowOcrText, app.Config.Advanced.IgnoreEmpty);
        });

    public void Repaint()
    {
        // Modo escuro não precisa de repintar ocioso (RF-196: camada/sobreposição).
    }

    public void SetRunning(bool running) =>
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_win is null) return;
            var app = AppRef();
            _win.SetRunning(running, app.Config.Profile, app.Config.Advanced, app.Config.App);
        });

    public IReadOnlyList<Platform.ScreenRect> OutputOccluders()
    {
        // Mesmo padrão de OwnWindowRects (App): leitura direta com tolerância,
        // na thread do laço. Só a janela visível oclui.
        try
        {
            var w = _win;
            if (w is null || !w.IsVisible) return Array.Empty<Platform.ScreenRect>();
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

    private static App AppRef() =>
        (App)Avalonia.Application.Current!;
}
