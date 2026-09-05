using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Gort.Core;

namespace Gort.UI;

/// <summary>
/// Tela de abertura (RF-004): versão + data de compilação, mantida por P-01
/// e removida com desvanecimento de P-02. Enquanto visível, o App executa as
/// verificações da inicialização (RF-005).
/// </summary>
public sealed class SplashWindow : Window
{
    private readonly DispatcherTimer _fade = new();
    private double _opacity = 1.0;

    public SplashWindow(string version, string buildDate)
    {
        Title = "GORT";
        BrandLogo.Apply(this);
        Width = 480; Height = 320;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        Topmost = true;
        Background = new SolidColorBrush(GortTheme.Bg);

        var stack = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var badge = BrandLogo.Badge(96);
        if (badge is not null)
        {
            badge.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            stack.Children.Add(badge);
        }
        stack.Children.Add(new TextBlock
        {
            Text = "GORT", FontSize = 32, FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Tradutor de tela em tempo real", FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"v{version} — compilada em {buildDate}", FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
        var bar = new ProgressBar
        {
            IsIndeterminate = true, Width = 200, Height = 6,
            Foreground = new SolidColorBrush(GortTheme.Accent),
            Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
            Margin = new Thickness(0, 20, 0, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        stack.Children.Add(bar);
        stack.Children.Add(new TextBlock
        {
            Name = "Status", Text = "Iniciando…", FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });
        Content = stack;

        _fade.Interval = TimeSpan.FromMilliseconds(50);
        _fade.Tick += (_, _) =>
        {
            _opacity -= 1.0 / (Params.P02_SplashFadeSec * 20);   // P-02 🔒: 1→0 em 2 s
            if (_opacity <= 0) { _fade.Stop(); Close(); }
            else Opacity = _opacity;
        };
        Closed += (_, _) => _fade.Stop();   // fechou no X: sem ticks órfãos
    }

    public void SetStatus(string s)
    {
        if (Content is StackPanel sp)
            foreach (var c in sp.Children)
                if (c is TextBlock t && t.Name == "Status") t.Text = s;
    }

    private bool _finishing;

    /// <summary>Encerra após P-01 com desvanecimento (chamado ao fim das tarefas).</summary>
    public async void FinishAsync()
    {
        if (_finishing) return;
        _finishing = true;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await System.Threading.Tasks.Task.Delay(
                    TimeSpan.FromSeconds(Params.P01_SplashStaySec));  // P-01 🔒
                _fade.Start();
            });
        }
        catch { }
    }
}
