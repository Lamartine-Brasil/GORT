using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gort.Imaging;
using Gort.Platform;

namespace Gort.UI;

/// <summary>
/// Conta-gotas (RF-080..RF-082): imagem da região ampliável 1–4× (P-139, combo
/// ou roda), arrasto mostra R,G,B,H,S,V do pixel; pré-visualização binarizada
/// lado a lado com o mesmo critério do pré-processamento (preto passa,
/// branco não passa).
/// </summary>
public sealed class DropperWindow : Window
{
    private readonly Image _view = new() { Stretch = Stretch.None };
    private readonly Image _preview = new() { Stretch = Stretch.None };
    private readonly TextBlock _readout = new();
    private readonly ComboBox _zoomBox = new();
    private readonly byte[] _bgra;
    private readonly int _w, _h;
    private readonly FilterMode _mode;
    private readonly System.Collections.Generic.List<(int R, int G, int B, int S1, int S2, int V1, int V2)> _groups;
    private readonly int _threshold;

    public DropperWindow(string title, RegionImage img, FilterMode mode,
        System.Collections.Generic.List<(int R, int G, int B, int S1, int S2, int V1, int V2)> groups,
        int threshold)
    {
        Title = title;
        Width = 900; Height = 520;
        Topmost = true;   // conta-gotas abre sobre as molduras (Topmost)
        _bgra = img.Bytes; _w = img.Width; _h = img.Height;
        _mode = mode; _groups = groups; _threshold = threshold;

        _zoomBox.ItemsSource = new[] { "1×", "2×", "3×", "4×" };   // P-139
        _zoomBox.SelectedIndex = 1;
        _zoomBox.SelectionChanged += (_, _) => Render();
        _view.PointerMoved += OnPixel;
        _view.PointerWheelChanged += (_, e) =>
        {
            int z = _zoomBox.SelectedIndex + 1 + (e.Delta.Y > 0 ? 1 : -1);
            _zoomBox.SelectedIndex = Math.Clamp(z, 1, 4) - 1;
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        var left = new StackPanel { Margin = new Thickness(8), Spacing = 6 };
        left.Children.Add(new TextBlock { Text = "Original (arraste sobre a imagem):" });
        left.Children.Add(_zoomBox);
        left.Children.Add(new ScrollViewer { Content = _view, Height = 380 });
        left.Children.Add(_readout);
        var right = new StackPanel { Margin = new Thickness(8), Spacing = 6 };
        right.Children.Add(new TextBlock { Text = "Binarizada (preto = passa no filtro):" });
        right.Children.Add(new ScrollViewer { Content = _preview, Height = 380 });
        var refresh = new Button { Content = "Atualizar", HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += (_, _) => Render();
        right.Children.Add(refresh);
        grid.Children.Add(left); grid.Children.Add(right);
        Grid.SetColumn(right, 1);
        Content = grid;
        Render();
    }

    private int Zoom => System.Math.Max(1, _zoomBox.SelectedIndex + 1);

    private static Bitmap ToBitmap(byte[] bgra, int w, int h)
    {
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();
        Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        return wb;
    }

    private void Render()
    {
        SetSource(_view, ToBitmap(_bgra, _w, _h));
        _view.Width = _w * Zoom; _view.Height = _h * Zoom;
        var gray = ColorFilter.Binarize(_bgra, _w, _h, _mode, _groups, _threshold);
        var pv = new byte[gray.Length * 4];
        for (int i = 0; i < gray.Length; i++)
        { pv[i * 4] = pv[i * 4 + 1] = pv[i * 4 + 2] = gray[i]; pv[i * 4 + 3] = 255; }
        SetSource(_preview, ToBitmap(pv, _w, _h));
        _preview.Width = _w * Zoom; _preview.Height = _h * Zoom;
    }

    private static void SetSource(Image view, Bitmap next)
    {
        (view.Source as System.IDisposable)?.Dispose();
        view.Source = next;
    }

    private void OnPixel(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(_view);
        int x = Math.Clamp((int)(p.X / Zoom), 0, _w - 1);
        int y = Math.Clamp((int)(p.Y / Zoom), 0, _h - 1);
        int o = (y * _w + x) * 4;
        byte b = _bgra[o], g = _bgra[o + 1], r = _bgra[o + 2];
        var (hh, s, v) = ColorFilter.RgbToHsv(r, g, b);
        _readout.Text = $"R {r}  G {g}  B {b}  H {(int)hh}  S {s * 100 / 255}  V {v * 100 / 255}";
    }
}
