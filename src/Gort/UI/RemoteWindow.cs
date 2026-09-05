using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Gort.UI;

/// <summary>
/// Controle remoto (V.2, RF-517..522): barrinha sem bordas com o essencial —
/// gerenciar áreas, área rápida, área instantânea, iniciar/parar com cor de
/// estado e sistema. Arrastável em qualquer ponto, redimensionável
/// mantendo a proporção (RF-518), controles escalados (RF-519),
/// sempre-no-topo opcional (RF-520), fechar esconde (RF-521), botões reagem
/// ao pressionar (RF-522).
/// </summary>
/// <summary>
/// Contrato mínimo do controle remoto (RF-517): o que ele precisa do
/// aplicativo. Permite cobertura headless com um hospedeiro de teste,
/// sem bootar o App real.
/// </summary>
public interface IRemoteHost
{
    bool RemoteAlwaysOnTop { get; }
    Lifecycle.LoopState LoopState { get; }
    HotkeyActions? HotkeyActions { get; }
    void OpenAreas();
    void ToggleLoop();
    void ShowMain();
}

public sealed class RemoteWindow : Window
{
    private const double BaseW = 470, BaseH = 96;
    private readonly Button _toggle;
    private readonly IRemoteHost _app;
    private readonly Avalonia.Threading.DispatcherTimer _poll;

    public RemoteWindow(IRemoteHost app)
    {
        _app = app;
        Title = "GORT";
        BrandLogo.Apply(this);
        Width = BaseW; Height = BaseH;
        MinWidth = 170; MinHeight = 48;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = app.RemoteAlwaysOnTop;   // RF-520
        Closed += (_, _) => { try { _poll?.Stop(); } catch { } };  // não vazar o temporizador

        // Borda de cima: título à esquerda, fechar à direita (margem original).
        var titleBar = new DockPanel { Height = 28 };
        var title = new TextBlock
        {
            Text = "GORT", VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var close = new Button
        {
            Content = "×", Width = 28, Margin = new Thickness(0, 2, 4, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.Click += (_, _) => Hide();                    // RF-521
        Avalonia.Controls.ToolTip.SetTip(close, "Esconder (o programa continua rodando)");
        DockPanel.SetDock(close, Dock.Right);
        titleBar.Children.Add(close);
        titleBar.Children.Add(title);
        DockPanel.SetDock(titleBar, Dock.Top);

        var strip = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 4,
        };
        // Ícones vetoriais desenhados à mão (sem fonte de ícones: funciona
        // em Windows, Linux e macOS). Cinza neutro legível em tema claro/escuro.
        var area = Mk("Gerenciar áreas", "Gerenciar áreas de OCR", AreasIcon(),
            () => _app.OpenAreas());
        var quick = Mk("Área rápida", "Área rápida temporária (Ctrl+Shift+X)", QuickIcon(),
            () => _app.HotkeyActions?.Execute(Config.ShortcutActions.Quick));
        var snap = Mk("Área instantânea", "Traduzir um trecho agora (Ctrl+Shift+A)", CameraIcon(),
            () => _app.HotkeyActions?.SnapshotFromUi());
        _toggle = MkToggle(() => _app.ToggleLoop());
        var cfg = Mk("Sistema", "Abrir configurações", GearIcon(),
            () => _app.ShowMain());
        // Sem botão "_" na barrinha: o "×" do título já esconde (RF-521).
        // Dois botões fazendo a mesma coisa só confundia.
        foreach (var b in new[] { area, quick, snap, _toggle, cfg })
        {
            b.MinWidth = 64;
            b.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            strip.Children.Add(b);
        }

        var box = new Viewbox { Child = strip };             // RF-519: escala tudo
        var dock = new DockPanel();
        dock.Children.Add(titleBar);   // fora do Viewbox: fixos (RF-519)
        dock.Children.Add(box);
        Content = dock;

        PointerPressed += (_, e) =>
        {
            // Fora do canto de resize (esse é do OnPointerPressed).
            var p = e.GetPosition(this);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && e.Source is not Button
                && p.X <= Width - 10 && p.Y <= Height - 10)
                BeginMoveDrag(e);
        };
        PointerPressed += (_, e) => Opacity = 0.6;          // RF-522
        PointerReleased += (_, _) => Opacity = 1.0;

        _poll = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _poll.Tick += (_, _) => RefreshToggle();
        _poll.Start();
        RefreshToggle();
    }

    private Button Mk(string label, string tip, Control icon, Action onClick)
    {
        var stack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 1,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        var holder = new Border
        {
            Height = 24, Child = icon,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var text = new TextBlock
        {
            Text = label, FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        stack.Children.Add(holder); stack.Children.Add(text);
        var b = new Button
        {
            Content = stack,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        b.Click += (_, _) => onClick();
        Avalonia.Controls.ToolTip.SetTip(b, tip);
        return b;
    }

    private Button MkToggle(Action onClick)
    {
        _toggleSymbol = new TextBlock
        {
            Text = "▶", FontSize = 18,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        _toggleLabel = new TextBlock
        {
            Text = "Iniciar tradução", FontSize = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var stack = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Vertical,
            Spacing = 1,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        stack.Children.Add(_toggleSymbol); stack.Children.Add(_toggleLabel);
        var b = new Button
        {
            Content = stack,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };
        b.Click += (_, _) => onClick();
        Avalonia.Controls.ToolTip.SetTip(b, "Iniciar/parar tradução (Ctrl+Shift+Z)");
        return b;
    }

    private TextBlock? _toggleSymbol;
    private TextBlock? _toggleLabel;

    private static readonly SolidColorBrush IconBrush =
        new(Color.FromRgb(0x66, 0x66, 0x66));

    // Retângulo tracejado = "marcar uma região da tela".
    private static Control AreasIcon() => new Avalonia.Controls.Shapes.Rectangle
    {
        Width = 17, Height = 13,
        Stroke = IconBrush, StrokeThickness = 1.6,
        StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 2 },
    };

    // Região + raio = "rápida, na hora".
    private static Control QuickIcon()
    {
        var g = new Grid { Width = 22, Height = 22 };
        g.Children.Add(new Avalonia.Controls.Shapes.Rectangle
        {
            Width = 12, Height = 10,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Stroke = IconBrush, StrokeThickness = 1.4,
            StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 },
        });
        g.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M14,2 L8,12 L11,12 L10,20 L17,9 L13.5,9 Z"),
            Fill = IconBrush, Stretch = Avalonia.Media.Stretch.Fill,
            Width = 10, Height = 18,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        return g;
    }

    // Câmera = "fotografar um trecho e traduzir".
    private static Control CameraIcon()
    {
        var g = new Grid { Width = 22, Height = 22 };
        g.Children.Add(new Avalonia.Controls.Shapes.Rectangle
        {
            Width = 17, Height = 11,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            Stroke = IconBrush, StrokeThickness = 1.6,
        });
        g.Children.Add(new Avalonia.Controls.Shapes.Rectangle
        {
            Width = 6, Height = 3,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(-6, -9, 0, 0),
            Fill = IconBrush,
        });
        g.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 6, Height = 6,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
            Stroke = IconBrush, StrokeThickness = 1.4,
        });
        return g;
    }

    // Engrenagem = "ajustes do programa".
    private static Control GearIcon()
    {
        var g = new Grid { Width = 22, Height = 22 };
        foreach (double angle in new[] { 0.0, 45.0, 90.0, 135.0 })
            g.Children.Add(new Avalonia.Controls.Shapes.Rectangle
            {
                Width = 17, Height = 3.4, Fill = IconBrush,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                RenderTransform = new Avalonia.Media.RotateTransform(angle),
            });
        g.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 15, Height = 15,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Fill = Avalonia.Media.Brushes.Transparent,
            Stroke = IconBrush, StrokeThickness = 2.2,
        });
        g.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 5.5, Height = 5.5,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Fill = IconBrush,
        });
        return g;
    }

    // Estado por cor + palavra no botão central: verde "Parar" traduzindo,
    // padrão "Traduzir" ocioso. Informação num relance.
    private void RefreshToggle()
    {
        bool idle = _app.LoopState == Lifecycle.LoopState.Idle;
        if (_toggleSymbol is not null) _toggleSymbol.Text = idle ? "▶" : "■";
        if (_toggleLabel is not null) _toggleLabel.Text = idle ? "Iniciar tradução" : "Parar";
        _toggle.Background = idle
            ? null
            : new SolidColorBrush(Color.FromRgb(46, 160, 67));
    }

    // RF-518: redimensionável mantendo a proporção.
    private bool _resizing;
    private Point _grab;
    private double _gw, _gh;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind
                != PointerUpdateKind.LeftButtonPressed) return;
        if (e.Source is Button) return;
        var p = e.GetPosition(this);
        if (p.X > Width - 10 || p.Y > Height - 10)   // borda/canto inferior-direito
        {
            _resizing = true;
            _grab = p; _gw = Width; _gh = Height;
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_resizing) return;
        var p = e.GetPosition(this);
        double fx = (_gw + (p.X - _grab.X)) / BaseW;
        double fy = (_gh + (p.Y - _grab.Y)) / BaseH;
        double f = Math.Max(fx, fy);                    // canto: maior fator
        if (p.X <= Width - 10) f = fy;                  // só borda inferior
        else if (p.Y <= Height - 10) f = fx;            // só borda direita
        f = Math.Max(0.5, f);
        Width = BaseW * f;
        Height = BaseH * f;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _resizing = false;
        e.Pointer.Capture(null);
    }
}
