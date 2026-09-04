using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gort.Config;
using Gort.Store;
using SkiaSharp;

namespace Gort.UI;

/// <summary>
/// Modo camada (19.3, RF-332..343): janela sem bordas com transparência por
/// pixel, desenhada inteira a cada atualização. Parada: fundo alfa P-79,
/// clicável, borda de destaque. Rodando: transparente e atravessável
/// (RF-333/334), salvo transparência forçada (RF-335). Menu de contexto
/// imediato (RF-545/546).
/// </summary>
public sealed class LayerWindow : Window
{
    private readonly ConfigService _cfg;
    private readonly Image _view = new() { Stretch = Stretch.Fill };
    private string _text = "";
    private bool _running;
    private (string Text, DateTime Until)? _warning;
    private readonly Avalonia.Threading.DispatcherTimer _warnTimer;

    public LayerWindow(ConfigService cfg)
    {
        _cfg = cfg;
        Title = "GORT — Camada";
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        MinWidth = Core.Params.P87_LayerMinW;                    // RF-339
        MinHeight = Core.Params.P88_LayerMinH;
        Content = _view;

        var p = cfg.Profile;
        if (p.LayerW > 0) { Width = p.LayerW; Height = p.LayerH; }
        else { Width = Core.Params.P133_LayerDefaultW; Height = Core.Params.P133_LayerDefaultH; }

        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        Resized += (_, _) => Render();

        _warnTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _warnTimer.Tick += (_, _) =>
        {
            if (_warning.HasValue && DateTime.UtcNow >= _warning.Value.Until)
            { _warning = null; Render(); }
        };
        _warnTimer.Start();

        ApplyRunning(false);
    }

    // ---- geometria persistida (RF-340) ----

    public void SaveGeometry()
    {
        var p = _cfg.Profile;
        p.LayerX = Position.X; p.LayerY = Position.Y;
        p.LayerW = (int)Width; p.LayerH = (int)Height;
    }

    /// <summary>RF-041/P-133: valida contra os monitores.</summary>
    public static (int X, int Y, int W, int H) Validate(
        int x, int y, int w, int h, int screenH,
        List<(int X, int Y, int W, int H)> monitors)
    {
        if (w <= 0 || h <= 0)
            return (Core.Params.P133_LayerDefaultX, screenH - Core.Params.P133_LayerDefaultYOffset,
                Core.Params.P133_LayerDefaultW, Core.Params.P133_LayerDefaultH);  // P-133 🔒
        foreach (var m in monitors)
        {
            if (x < m.X + m.W && m.X < x + w && y < m.Y + m.H && m.Y < y + h)
            {
                // Intersecta: desloca para dentro se parcial.
                int nx = Math.Clamp(x, m.X, m.X + m.W - Math.Min(w, m.W));
                int ny = Math.Clamp(y, m.Y, m.Y + m.H - Math.Min(h, m.H));
                return (nx, ny, w, h);
            }
        }
        return (Core.Params.P133_LayerDefaultX, screenH - Core.Params.P133_LayerDefaultYOffset,
            Core.Params.P133_LayerDefaultW, Core.Params.P133_LayerDefaultH);
    }

    // ---- estados (RF-333..335) ----

    public void ApplyRunning(bool running)
    {
        _running = running;
        var app = _cfg.App;
        Topmost = app.TranslationAlwaysOnTop       // RF-319/320
            && (!_cfg.Advanced.TopOnlyDuring || running);
        bool transparent = running || _cfg.Advanced.ForcedTransparency;
        Platform.GuiFx.SetClickThrough(this, transparent);  // C7 (por SO)
        Render();
    }

    /// <summary>Mensagem de aviso temporária prefixada (RF-342/343).</summary>
    public void ShowWarning(string text, int seconds)
    {
        _warning = (text, DateTime.UtcNow.AddSeconds(seconds));
        Render();
    }

    public void SetText(string text)
    {
        _text = text.Replace("\r\n", "\n");
        AutoFitToText();   // janela acompanha o conteúdo, até o máximo
        Render();
    }

    private float? _fitPx;   // fonte encolhida pelo auto-ajuste (nulo = tamanho manual)

    /// <summary>
    /// Auto-ajuste: a janela cola no texto até o máximo configurado; se o
    /// texto estourar a altura, a fonte encolhe até caber, nunca abaixo do
    /// mínimo configurado (legibilidade; piso absoluto 6 pt no extremo).
    /// Só traduzindo — pausado, o tamanho manual do usuário manda.
    /// 0 em largura/altura = livre = sem auto-ajuste.
    /// </summary>
    private void AutoFitToText()
    {
        var p = _cfg.Profile;
        _fitPx = null;
        if (!_running || !p.LayerAutoFit) return;
        if (string.IsNullOrWhiteSpace(_text)) return;
        if (p.LayerMaxW <= 0 && p.LayerMaxH <= 0) return;
        double scale = Screens.ScreenFromWindow(this)?.Scaling
            ?? Screens.Primary?.Scaling ?? 1.0;
        double floorPt = System.Math.Clamp(p.AutoMinPt, 6, 72);
        var (w, h, pt) = ComputeFit(_text, p.FontSize, scale, p.LayerMaxW, p.LayerMaxH, floorPt);
        // Trava na área útil do monitor e mantém a janela toda visível.
        var scr = Screens.ScreenFromWindow(this);
        var wa = scr?.WorkingArea;
        if (wa.HasValue)
        {
            w = Math.Min(w, wa.Value.Width / scale);
            h = Math.Min(h, wa.Value.Height / scale);
            int pxW = Math.Max(1, (int)(w * scale)), pxH = Math.Max(1, (int)(h * scale));
            int nx = Math.Clamp(Position.X, wa.Value.X,
                Math.Max(wa.Value.X, wa.Value.X + wa.Value.Width - pxW));
            int ny = Math.Clamp(Position.Y, wa.Value.Y,
                Math.Max(wa.Value.Y, wa.Value.Y + wa.Value.Height - pxH));
            Position = new PixelPoint(nx, ny);
        }
        Width = w; Height = h;
        _fitPx = (float)(pt * 96 / 72 * scale);
    }

    /// <summary>
    /// Núcleo puro do auto-ajuste (testável): devolve largura/altura da
    /// janela em DIPs e a fonte em pt. Largura/altura ≤ 0 = sem limite.
    /// A fonte encolhe para caber, nunca abaixo de minPt (legibilidade).
    /// </summary>
    internal static (double WDip, double HDip, double FontPt) ComputeFit(
        string text, double fontPt, double scale, double maxWDip, double maxHDip,
        double minPt)
    {
        double margin = Core.Params.P86_LayerMargin;
        double adv = Core.Params.P98_LineAdvance;
        if (string.IsNullOrWhiteSpace(text))
            return (Core.Params.P87_LayerMinW, Core.Params.P88_LayerMinH,
                Math.Max(minPt, fontPt));
        using var face = SkiaText.ResolveFont(null);
        double fitPt = Math.Max(minPt, fontPt);
        double wrapDip = maxWDip > 0 ? Math.Max(50, maxWDip - 2 * margin) : 10000;
        double hCapDip = maxHDip > 0 ? Math.Max(1, maxHDip - 2 * margin)
            : double.PositiveInfinity;
        float wPx = 0, hPx = 0;
        for (int i = 0; i < 25; i++)
        {
            float sizePx = (float)(fitPt * 96 / 72 * scale);
            using var meas = new SKFont(face, sizePx);
            var lines = SkiaText.Wrap(text, meas, (float)Math.Max(1, wrapDip * scale));
            wPx = 0;
            foreach (var l in lines) wPx = Math.Max(wPx, meas.MeasureText(l));
            hPx = lines.Count == 0 ? 0
                : sizePx + (lines.Count - 1) * sizePx * (float)adv;
            if (hPx / scale <= hCapDip || fitPt <= minPt) break;
            fitPt = Math.Max(minPt, fitPt * 0.9);
        }
        double wDip = wPx / scale + 2 * margin;
        double hDip = hPx / scale + 2 * margin;
        if (maxWDip > 0) wDip = Math.Min(wDip, maxWDip);
        if (maxHDip > 0) hDip = Math.Min(hDip, maxHDip);
        wDip = Math.Max(wDip, Core.Params.P87_LayerMinW);
        hDip = Math.Max(hDip, Core.Params.P88_LayerMinH);
        return (wDip, hDip, fitPt);
    }

    private void Render()
    {
        var p = _cfg.Profile;
        double scale = Screens.ScreenFromWindow(this)?.Scaling
            ?? Screens.Primary?.Scaling ?? 1.0;
        int pw = Math.Max(1, (int)Math.Round(Width * scale));
        int ph = Math.Max(1, (int)Math.Round(Height * scale));
        using var bmp = new SKBitmap(pw, ph);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        bool transparent = _running || _cfg.Advanced.ForcedTransparency;
        if (!transparent)
        {
            // Fundo semitransparente + borda de destaque (RF-333, P-155).
            canvas.Clear(new SKColor(0, 0, 0, Core.Params.P79_LayerIdleAlpha));  // 🔒 190
            using var border = new SKPaint
            {
                Color = new SKColor(40, 134, 249), IsStroke = true, StrokeWidth = 3,
            };
            canvas.DrawRect(1.5f, 1.5f, pw - 3, ph - 3, border);
        }
        else if (p.TextBackground && _running)
        {
            // Fundo medido pela extensão real + P-82..85 (RF-337 🔒).
            // Alfa da configuração só com "usar transparência"; senão opaco (RF-378 🔒).
            var (tw, th) = Measure(p, scale);
            var bg = ToSk(p.BgColor);
            if (!p.BgTransparency) bg = bg.WithAlpha(255);
            using var paint = new SKPaint { Color = bg };
            canvas.DrawRect(
                (float)(Core.Params.P86_LayerMargin * scale - Core.Params.P82_BgPadLeft),
                (float)(Core.Params.P86_LayerMargin * scale - Core.Params.P83_BgPadTop),
                (float)(tw + Core.Params.P84_BgPadWidth),
                (float)(th + Core.Params.P85_BgPadHeight), paint);
        }

        string body = _warning.HasValue ? _warning.Value.Text + "\n" + _text : _text;
        if (body.Length == 0 && !_running)
            body = "Arraste/redimensione — o texto traduzido aparece aqui";
        if (body.Length > 0)
        {
            using var face = SkiaText.ResolveFont(
                string.IsNullOrWhiteSpace(p.FontFamily) ? null : p.FontFamily);
            float sizePx = _fitPx ?? (float)(p.FontSize * 96 / 72 * scale);
            float maxW = (float)(pw - 2 * Core.Params.P86_LayerMargin * scale);
            using var meas = new SKFont(face, sizePx);
            var lines = SkiaText.Wrap(body, meas, Math.Max(1, maxW));
            var align = p.TextOrder == "center" ? SkiaText.HAlign.Center
                : _cfg.Advanced.LayerRight ? SkiaText.HAlign.Right
                : SkiaText.HAlign.Left;
            float y = (float)(Core.Params.P86_LayerMargin * scale);   // RF-338
            if (_cfg.Advanced.LayerBottom)                             // RF-341
            {
                float total = sizePx + (lines.Count - 1) * sizePx
                    * (float)Core.Params.P98_LineAdvance;
                y = (float)(ph - Core.Params.P86_LayerMargin * scale - total);
            }
            SkiaText.DrawOutlined(canvas, lines,
                (float)(Core.Params.P86_LayerMargin * scale), y,
                face, sizePx, ToSk(p.TextColor), ToSk(p.Outline1), ToSk(p.Outline2),
                align, Math.Max(1, maxW), p.OverlayOutline);
        }

        var wb = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
            System.Runtime.InteropServices.Marshal.Copy(
                bmp.Bytes, 0, fb.Address, bmp.Bytes.Length);
        _view.Source = wb;
    }

    private (float W, float H) Measure(Profile p, double scale)
    {
        using var face = SkiaText.ResolveFont(
            string.IsNullOrWhiteSpace(p.FontFamily) ? null : p.FontFamily);
        float sizePx = _fitPx ?? (float)(p.FontSize * 96 / 72 * scale);
        float maxW = (float)(Width * scale - 2 * Core.Params.P86_LayerMargin * scale);
        using var meas = new SKFont(face, sizePx);
        var lines = SkiaText.Wrap(_text, meas, Math.Max(1, maxW));
        float w = 0;
        foreach (var l in lines) w = Math.Max(w, meas.MeasureText(l));
        return (w, lines.Count == 0 ? 0 : sizePx + (lines.Count - 1) * sizePx
            * (float)Core.Params.P98_LineAdvance);
    }

    private static SKColor ToSk(byte[] rgb) =>
        rgb.Length >= 4
            ? new SKColor(rgb[1], rgb[2], rgb[3], rgb[0])
            : rgb.Length >= 3
                ? new SKColor(rgb[0], rgb[1], rgb[2])
                : SKColors.White;

    // ---- arrasto + redimensionamento (zona P-89) ----

    private int _rz;
    private Point _grab;
    private PixelPoint _gpos;
    private double _gw, _gh, _gscale = 1;

    private int ZoneAt(Point pt)
    {
        const double z = 30;   // P-89
        int m = 0;
        if (pt.X < z) m |= 1;
        if (pt.X > Width - z) m |= 2;
        if (pt.Y < z) m |= 4;
        if (pt.Y > Height - z) m |= 8;
        return m;
    }

    private void OnPressed(object? s, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.PointerUpdateKind == PointerUpdateKind.RightButtonPressed)
        {
            OpenContextMenu();                                    // RF-545
            return;
        }
        if (props.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        var pt = e.GetPosition(this);
        _rz = ZoneAt(pt);
        if (_rz == 0) { BeginMoveDrag(e); return; }
        _grab = pt; _gpos = Position; _gw = Width; _gh = Height;
        _gscale = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
        e.Pointer.Capture(this);
    }

    /// <summary>RF-545/546: menu imediato, marcas refletem o estado.</summary>
    private void OpenContextMenu()
    {
        var p = _cfg.Profile;
        var menu = new ContextMenu();
        var order = new MenuItem
        {
            Header = p.TextOrder == "center" ? "Centralizar ✓" : "Centralizar",
        };
        order.Click += (_, _) =>
        {
            p.TextOrder = p.TextOrder == "center" ? "left" : "center";
            _cfg.SaveProfile();
            Render();
        };
        var spaces = new MenuItem
        {
            Header = p.RemoveSpaces ? "Remover espaços ✓" : "Remover espaços",
        };
        spaces.Click += (_, _) =>
        {
            p.RemoveSpaces = !p.RemoveSpaces;
            _cfg.SaveProfile();
            Render();
        };
        var transp = new MenuItem
        {
            Header = _cfg.Advanced.ForcedTransparency
                ? "Transparência forçada ✓" : "Transparência forçada",
        };
        transp.Click += (_, _) =>
        {
            _cfg.Advanced.ForcedTransparency = !_cfg.Advanced.ForcedTransparency;
            _cfg.SaveAdvanced();
            ApplyRunning(_running);
        };
        var close = new MenuItem { Header = "Fechar" };
        close.Click += (_, _) => Hide();
        menu.Items.Add(order); menu.Items.Add(spaces);
        menu.Items.Add(transp); menu.Items.Add(close);
        menu.Open(this);
    }

    private void OnMoved(object? s, PointerEventArgs e)
    {
        if (_rz == 0) return;
        var pt = e.GetPosition(this);
        double dx = pt.X - _grab.X, dy = pt.Y - _grab.Y;
        double nx = _gpos.X, ny = _gpos.Y, nw = _gw, nh = _gh;
        if ((_rz & 1) != 0) { nx = _gpos.X + dx * _gscale; nw = _gw - dx; }
        if ((_rz & 2) != 0) nw = _gw + dx;
        if ((_rz & 4) != 0) { ny = _gpos.Y + dy * _gscale; nh = _gh - dy; }
        if ((_rz & 8) != 0) nh = _gh + dy;
        nw = Math.Max(MinWidth, nw); nh = Math.Max(MinHeight, nh);
        Position = new PixelPoint((int)nx, (int)ny);
        Width = nw; Height = nh;
    }

    private void OnReleased(object? s, PointerReleasedEventArgs e)
    {
        _rz = 0;
        e.Pointer.Capture(null);
        Render();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;   // RF-326
        Hide();
        base.OnClosing(e);
    }
}
