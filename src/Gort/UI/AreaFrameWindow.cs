using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Gort.Platform;
using Gort.Regions;

namespace Gort.UI;

/// <summary>
/// Moldura de área (RF-054..RF-060, RF-063, RF-073..077): janela sem borda de
/// sistema, sempre no topo, fora da barra de tarefas, com barra de título
/// (tipo, índice, tamanho, posição — RF-055) e borda dupla. Move pela barra,
/// redimensiona por borda/canto (zona P-11 — RF-056), mínimo P-12 (RF-057),
/// volta para dentro da área virtual ao soltar (RF-058). Exclusão: borda
/// vermelha, opacidade 70%, sem botões de cor (RF-063).
/// </summary>
public sealed class AreaFrameWindow : Window
{
    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly bool _exclusion;
    private readonly Func<AreaFrameWindow, double> _scaleOf;
    private readonly Func<ScreenRect> _virtual;
    private int _index;

    public event Action<AreaFrameWindow>? GeometryChanged;
    public event Action<AreaFrameWindow>? CloseRequested;
    public event Action<AreaFrameWindow>? DropperRequested;
    public event Action<AreaFrameWindow>? GroupsRequested;

    private int _rz; // bitmask resize: 1=L 2=R 4=T 8=B
    private Point _grabDip;
    private PixelPoint _grabPos;
    private double _grabW, _grabH;
    private double _grabScale = 1;

    public AreaFrameWindow(int index, ScreenRect capture, double scale, bool exclusion,
        Func<AreaFrameWindow, double> scaleOf, Func<ScreenRect> virtualOf,
        bool followStyle = false)   // RF-463: borda distinta do segue-mouse
    {
        _index = index;
        _exclusion = exclusion;
        _scaleOf = scaleOf;
        _virtual = virtualOf;

        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var (fx, fy, fw, fh) = FrameGeometry.CaptureToFrame(capture, scale);
        Position = new PixelPoint(fx, fy);
        var (cw, ch) = FrameGeometry.ClampMin(fw, fh);
        Width = cw; Height = ch;

        var outerColor = exclusion ? Color.FromRgb(255, 0, 0)
            : followStyle ? Color.FromRgb(170, 0, 255)
            : Color.FromRgb(40, 167, 69);
        var root = new Border
        {
            BorderBrush = new SolidColorBrush(outerColor),
            BorderThickness = new Thickness(8),
            Opacity = exclusion ? 0.7 : 1.0,                       // RF-063, P-140
            Background = Brushes.Transparent,
            Child = new Border
            {
                BorderBrush = new SolidColorBrush(outerColor),
                BorderThickness = new Thickness(3),
                Background = Brushes.Transparent,
                Child = BuildTitleBar(),
            },
        };
        Content = root;

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        RefreshTitle();
    }

    private Control BuildTitleBar()
    {
        var bar = new DockPanel { Height = 20, Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 20)) };
        _title.FontSize = 11;
        _title.Foreground = Brushes.White;
        _title.Margin = new Thickness(4, 0, 0, 0);
        bar.Children.Add(_title);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        if (!_exclusion)
        {
            var drop = new Button { Content = "Cor", FontSize = 10, Padding = new Thickness(4, 0, 4, 0) };
            drop.Click += (_, _) => DropperRequested?.Invoke(this);
            var grp = new Button { Content = "Grupos", FontSize = 10, Padding = new Thickness(4, 0, 4, 0) };
            grp.Click += (_, _) => GroupsRequested?.Invoke(this);
            btns.Children.Add(drop); btns.Children.Add(grp);
        }
        var close = new Button { Content = "X", FontSize = 10, Padding = new Thickness(4, 0, 4, 0) };
        close.Click += (_, _) => CloseRequested?.Invoke(this);
        btns.Children.Add(close);
        DockPanel.SetDock(btns, Dock.Right);
        bar.Children.Add(btns);
        return bar;
    }

    public void SetIndex(int i) { _index = i; RefreshTitle(); }

    public ScreenRect CurrentCapture() =>
        FrameGeometry.FrameToCapture(Position.X, Position.Y, Width, Height, _scaleOf(this));

    public void RefreshTitle()
    {
        var c = CurrentCapture();
        _title.Text = _exclusion
            ? $"Exclusão {_index + 1} — {c.W}×{c.H} @ ({c.X},{c.Y})"
            : $"Área {_index + 1} — {c.W}×{c.H} @ ({c.X},{c.Y})";   // RF-055
    }

    private int ZoneAt(Point p)
    {
        const double z = FrameGeometry.EdgeZoneDip;                 // P-11
        int m = 0;
        if (p.X < z) m |= 1;
        if (p.X > Width - z) m |= 2;
        if (p.Y < 11) m |= 4;                                       // só o cromo
        if (p.Y > Height - z) m |= 8;
        // Cantos têm precedência; faixa do título (11..31, fora dos cantos) = mover.
        if (m == 4 && p.Y >= 11) m = 0;
        return m;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        var p = e.GetPosition(this);
        _rz = ZoneAt(p);
        if (_rz == 0)
        {
            if (p.Y >= 11 && p.Y < 31) BeginMoveDrag(e);            // barra de título
            return;
        }
        _grabDip = p; _grabPos = Position; _grabW = Width; _grabH = Height;
        _grabScale = _scaleOf(this);
        e.Pointer.Capture(this);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_rz == 0)
        {
            Cursor = new Cursor(p.Y >= 11 && p.Y < 31
                ? StandardCursorType.SizeAll : StandardCursorType.Arrow);
            return;
        }
        Cursor = new Cursor((_rz & 3) != 0 && (_rz & 12) != 0
            ? ((_rz & 1) != 0) == ((_rz & 4) != 0)
                ? StandardCursorType.TopLeftCorner : StandardCursorType.TopRightCorner
            : (_rz & 3) != 0 ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);

        double dx = p.X - _grabDip.X, dy = p.Y - _grabDip.Y;   // DIP
        double s = _grabScale;
        double nx = _grabPos.X, ny = _grabPos.Y, nw = _grabW, nh = _grabH;
        if ((_rz & 1) != 0) { nx = _grabPos.X + dx * s; nw = _grabW - dx; }
        if ((_rz & 2) != 0) nw = _grabW + dx;
        if ((_rz & 4) != 0) { ny = _grabPos.Y + dy * s; nh = _grabH - dy; }
        if ((_rz & 8) != 0) nh = _grabH + dy;
        var (mw, mh) = FrameGeometry.ClampMin(nw, nh);              // RF-057
        if (nw < mw) { if ((_rz & 1) != 0) nx -= (mw - nw) * s; nw = mw; }
        if (nh < mh) { if ((_rz & 4) != 0) ny -= (mh - nh) * s; nh = mh; }
        Position = new PixelPoint((int)nx, (int)ny);
        Width = nw; Height = nh;
        RefreshTitle();
        GeometryChanged?.Invoke(this);                              // RF-059 (throttle no manager)
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        var v = _virtual();                                         // RF-058
        int x = Position.X < v.X ? v.X : Position.X;
        int y = Position.Y < v.Y ? v.Y : Position.Y;
        if (x != Position.X || y != Position.Y) Position = new PixelPoint(x, y);
        if (_rz != 0) { _rz = 0; e.Pointer.Capture(null); }
        RefreshTitle();
        GeometryChanged?.Invoke(this);
    }
}
