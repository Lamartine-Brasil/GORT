using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Gort.Platform;

namespace Gort.UI;

/// <summary>
/// Camada de seleção (RF-047..RF-053): cobre a área de trabalho virtual,
/// arrasto com botão esquerdo desenha o retângulo (interior na cor de destaque,
/// borda verde-escura 2 px — RF-048), botão direito cancela (RF-051),
/// arrasto ≤4 px é descartado (RF-052). Opacidade da fórmula P-10 (RF-050 🔒).
/// Cores vindas das opções avançadas (RF-049).
/// </summary>
public sealed class SelectionWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle _rubber = new();
    private readonly double _scale;
    private readonly PixelPoint _originPhys;
    private Point? _startDip;

    public event Action<ScreenRect>? Selected;
    public event Action? Cancelled;

    public SelectionWindow(ScreenRect virtualPhys, double scale,
        string bgArgb, string accentArgb)
    {
        _scale = scale;
        _originPhys = new PixelPoint(virtualPhys.X, virtualPhys.Y);
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Cursor = new Cursor(StandardCursorType.Cross);
        Position = _originPhys;
        Width = Math.Max(1, virtualPhys.W / scale);
        Height = Math.Max(1, virtualPhys.H / scale);

        // RF-050 🔒: opacidade = max(alfa,75)/255 × 0,15.
        byte alpha = ParseAlpha(bgArgb);
        double opacity = Math.Max((int)alpha, 75) / 255.0 * 0.15;
        Background = new SolidColorBrush(Color.FromArgb(
            (byte)(opacity * 255), 0, 0, 0));

        _rubber.Fill = new SolidColorBrush(ParseColor(accentArgb, 77)); // ~0,30
        _rubber.Stroke = new SolidColorBrush(Color.FromRgb(0, 100, 0));  // verde-escuro
        _rubber.StrokeThickness = 2;                                     // RF-048
        _rubber.IsVisible = false;
        _canvas.Children.Add(_rubber);
        Content = _canvas;

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        Closed += (_, _) => InputGuard_Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Regions.InputGuard.SelectionOpen = true;   // RF-053
    }

    private void InputGuard_Close() => Regions.InputGuard.SelectionOpen = false;

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.RightButtonPressed)      // RF-051
        {
            Cancelled?.Invoke();
            Close();
        }
        else if (kind == PointerUpdateKind.LeftButtonPressed)
        {
            _startDip = e.GetPosition(this);
            _rubber.IsVisible = true;
        }
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_startDip is null) return;
        var p = e.GetPosition(this);
        Canvas.SetLeft(_rubber, Math.Min(_startDip.Value.X, p.X));
        Canvas.SetTop(_rubber, Math.Min(_startDip.Value.Y, p.Y));
        _rubber.Width = Math.Abs(p.X - _startDip.Value.X);
        _rubber.Height = Math.Abs(p.Y - _startDip.Value.Y);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind
                != PointerUpdateKind.LeftButtonReleased) return;
        if (_startDip is null) return;
        var p = e.GetPosition(this);
        // DIP → físico global.
        int x1 = _originPhys.X + (int)Math.Round(Math.Min(_startDip.Value.X, p.X) * _scale);
        int y1 = _originPhys.Y + (int)Math.Round(Math.Min(_startDip.Value.Y, p.Y) * _scale);
        int x2 = _originPhys.X + (int)Math.Round(Math.Max(_startDip.Value.X, p.X) * _scale);
        int y2 = _originPhys.Y + (int)Math.Round(Math.Max(_startDip.Value.Y, p.Y) * _scale);
        _startDip = null;
        _rubber.IsVisible = false;
        if (Regions.FrameGeometry.IsAccidentalClick(x2 - x1, y2 - y1)) return;  // RF-052
        Selected?.Invoke(new ScreenRect(x1, y1, x2 - x1, y2 - y1));
        Close();
    }

    private static byte ParseAlpha(string argb)
    {
        var s = argb.TrimStart('#');
        if (s.Length == 8 && byte.TryParse(s[..2],
                System.Globalization.NumberStyles.HexNumber, null, out var a)) return a;
        return 255;
    }

    private static Color ParseColor(string argb, byte forceAlpha)
    {
        var s = argb.TrimStart('#');
        try
        {
            if (s.Length == 8)
                return Color.FromArgb(forceAlpha,
                    Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
            if (s.Length == 6)
                return Color.FromArgb(forceAlpha,
                    Convert.ToByte(s[0..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16));
        }
        catch { }
        return Color.FromArgb(forceAlpha, 0, 0, 0);
    }
}
