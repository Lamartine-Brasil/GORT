using Avalonia.Threading;
using Gort.Loop;
using Gort.UI;

namespace Gort.UI;

/// <summary>Adapta o DarkWindow ao laço: desenho na thread de UI.</summary>
public sealed class DarkSink : IDisplaySink
{
    private readonly TranslationWindows _windows;
    private DarkWindow? _win;
    private bool _alive = true;

    public DarkSink(TranslationWindows windows, DarkWindow win)
    {
        _windows = windows;
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

    private static App AppRef() =>
        (App)Avalonia.Application.Current!;
}
