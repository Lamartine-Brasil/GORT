using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Gort.Config;
using Gort.Store;

namespace Gort.UI;

/// <summary>
/// Modo escuro (19.2, RF-327..RF-331): caixa de texto rolável com fundo
/// escuro, indicador de "parado", eco do OCR, arrasto pelo corpo, fechar
/// apenas oculta (RF-326). Quebras normalizadas para a plataforma (RF-329).
/// </summary>
public sealed class DarkWindow : Window
{
    private readonly TextBox _box = new();
    private readonly TextBlock _status = new();

    public DarkWindow()
    {
        Title = "GORT — Tradução";
        Width = 480; Height = 340;
        MinWidth = 240; MinHeight = 140;
        // Fundo da janela também escuro: sem ele, a faixa do status vazio
        // aparece branca antes do primeiro SetRunning.
        Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));

        _box.AcceptsReturn = true;
        _box.IsReadOnly = true;
        _box.TextWrapping = TextWrapping.Wrap;
        _box.Background = new SolidColorBrush(Color.FromRgb(18, 18, 18));
        _box.Foreground = Brushes.White;
        _box.FontSize = 15;

        _status.FontSize = 12;
        _status.Margin = new Thickness(8, 4, 8, 4);
        _status.MinHeight = 20;
        _status.Text = " ";

        var dock = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        dock.Children.Add(_status);
        dock.Children.Add(_box);
        Content = dock;

        PointerPressed += (_, e) =>
        {
            // RF-331: arrastar por qualquer ponto do corpo (fora da caixa).
            if (e.Source is not TextBox
                && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    /// <summary>RF-325: reconfigura sem destruir.</summary>
    public void ApplySettings(Profile p, AdvancedOptions a, AppOptions app)
    {
        _box.FontSize = p.FontSize;
        _box.Foreground = new SolidColorBrush(Color.FromRgb(p.TextColor[0], p.TextColor[1], p.TextColor[2]));
        if (!string.IsNullOrWhiteSpace(a.DarkFont))
        {
            try { _box.FontFamily = new FontFamily(a.DarkFont); } catch { }
        }
        _box.TextAlignment = p.TextOrder == "center" ? TextAlignment.Center : TextAlignment.Left;  // RF-323
        _box.FlowDirection = (a.RightToLeft || LangCodes_IsRtl(p.TargetLanguage))  // RF-324
            ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        Topmost = app.TranslationAlwaysOnTop && (!a.TopOnlyDuring || _running);    // RF-319/320
    }

    private static bool LangCodes_IsRtl(string lang) => Translate.LangCodes.IsRtl(lang);

    private bool _running;

    /// <summary>Indicador visível de parado/rodando (RF-327) + topo (RF-320).</summary>
    public void SetRunning(bool running, Profile p, AdvancedOptions a, AppOptions app)
    {
        _running = running;
        _status.Text = running ? "● Traduzindo…" : "● Parado";
        _status.Foreground = running ? Brushes.LightGreen : Brushes.Gray;
        Topmost = app.TranslationAlwaysOnTop && (!a.TopOnlyDuring || running);
    }

    /// <summary>
    /// Exibe a tradução; com eco do OCR: tradução + 2 quebras + "OCR: " +
    /// reconhecido (RF-328). Vazio ignorado se a opção manda (RF-240).
    /// </summary>
    public void ShowTranslation(string display, string recognized,
        bool showOcr, bool ignoreEmpty)
    {
        string clean = recognized.TrimStart();
        if (clean.StartsWith("OCR:", StringComparison.OrdinalIgnoreCase))
            clean = clean[4..].TrimStart(' ', ':');
        else if (clean.StartsWith("OCR :", StringComparison.OrdinalIgnoreCase))
            clean = clean[5..].TrimStart(' ', ':');
        string text = showOcr && recognized.Length > 0
            ? display + "\n\nOCR: " + clean
            : display;
        text = text.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);  // RF-329
        if (ignoreEmpty && text.Length == 0) return;                           // RF-240
        if (text.Length == 0 && recognized.Length == 0)
            text = "Nenhum texto reconhecido. Ajuste a área, o filtro ou a ampliação.";
        _box.Text = text;
        if (!IsVisible) Show();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;   // RF-326: fechar apenas oculta
        Hide();
        base.OnClosing(e);
    }
}

/// <summary>
/// Janelas de tradução (19.1): troca de modo destrói e recria (RF-318).
/// Etapa 7: escuro. Camada (Etapa 11) e sobreposição (Etapa 12) plugam aqui.
/// </summary>
public sealed class TranslationWindows
{
    private readonly ConfigService _cfg;

    public TranslationWindows(ConfigService cfg) => _cfg = cfg;

    public DarkWindow Dark()
    {
        if (_dark is null)
        {
            _dark = new DarkWindow();
            _dark.ApplySettings(_cfg.Profile, _cfg.Advanced, _cfg.App);
            _dark.Closed += (_, _) => _dark = null;
        }
        return _dark;
    }

    public LayerWindow Layer()
    {
        if (_layer is null)
        {
            _layer = new LayerWindow(_cfg);
            _layer.Closed += (_, _) => _layer = null;
        }
        return _layer;
    }

    private DarkWindow? _dark;
    private LayerWindow? _layer;
    // Sobreposição/Substituição: uma janela por área, ancorada no
    // retângulo da captura com a escala do próprio monitor. Nada de
    // conta global: monitores e escala se resolvem na hora, por janela
    // (pedido do dono — o texto sai dentro do retângulo em qualquer
    // combinação de monitores). Janelas escondem, nunca morrem.
    private readonly Dictionary<int, OverlayWindow> _overlays = new();
    private bool _overlayRunning;
    private bool _overlaySubstitute;
    private bool _darkPlaced, _layerPlaced;   // estreia posicionada: não mexer depois

    /// <summary>Escala do monitor da área (RF-075); o App injeta via Screens.</summary>
    public Func<Platform.ScreenRect, double> ScaleOf { get; set; } = _ => 1.0;

    /// <summary>RF-317/318: mostra o modo pedido, destruindo o anterior.
    /// Janelas de tradução são independentes da principal (sem dono): minimizar
    /// uma não minimiza a outra. Escuro/camada estreiam fora das áreas de OCR.</summary>
    public void ShowForMode(string mode,
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect>? areas = null)
    {
        bool overlayLike = mode == "overlay" || mode == "replace";
        if (overlayLike)
        {
            // Fase 2: modo novo substitui o original sob a tradução.
            _overlaySubstitute = mode == "replace";
            foreach (var w in _overlays.Values) w.Substitute = _overlaySubstitute;
            // Janelas nascem no primeiro quadro, já ancoradas — sem roubar
            // foco no meio do jogo.
            _dark?.Hide();
            _layer?.Hide();
            return;
        }
        foreach (var w in _overlays.Values) w.Hide();
        if (mode == "layer")
        {
            _dark?.Hide();
            var w = Layer();
            w.Topmost = true;   // pedido do dono: clicar no jogo não cobre a camada
            if (!w.IsVisible)
            {
                // Só estreia sem geometria salva: depois o usuário é quem manda.
                if (!_layerPlaced && _cfg.Profile.LayerW <= 0)
                {
                    PlaceLayer(w, areas);
                    _layerPlaced = true;
                }
                ShowExcluded(w);
            }
            w.Activate();
        }
        else
        {
            _layer?.Hide();
            ShowDark(areas);
        }
    }

    public void ShowDark(System.Collections.Generic.IReadOnlyList<Platform.ScreenRect>? areas = null)
    {
        var w = Dark();
        w.ApplySettings(_cfg.Profile, _cfg.Advanced, _cfg.App);
        if (!w.IsVisible)
        {
            if (!_darkPlaced) { PlaceOutside(w, areas); _darkPlaced = true; }
            ShowExcluded(w);
        }
        w.Activate();
    }

    /// <summary>
    /// Fase 1 do roadmap: Escuro/Camada nunca entram na captura (o overlay
    /// já se exclui sozinho). Sem isso o OCR leria a própria tradução.
    /// </summary>
    internal static void ShowExcluded(Avalonia.Controls.Window w)
    {
        w.Show();
        Platform.GuiFx.SetCaptureExclusion(w, true);
    }
    /// <summary>
    /// Estreia da camada na posição inicial escolhida (perfil): fora das
    /// áreas (padrão), em cima dentro da captura ou embaixo dentro dela.
    /// Depois da estreia, o usuário é quem manda (geometria persistida).
    /// </summary>
    private void PlaceLayer(Avalonia.Controls.Window w,
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect>? areas)
    {
        try
        {
            var scr = w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary;
            if (scr is null) return;
            double s = scr.Scaling;
            var wa = scr.WorkingArea;
            int ww = (int)(w.Width * s), wh = (int)(w.Height * s);
            var (x, y) = ComputePlacement(_cfg.Profile.LayerPlace,
                ww, wh, areas, wa.X, wa.Y, wa.Width, wa.Height);
            w.Position = new Avalonia.PixelPoint(x, y);
        }
        catch { }
    }

    /// <summary>
    /// Reposiciona a camada visível pela opção vigente (troca de opção com
    /// a janela aberta). Fora disso, a estreia usa a mesma conta.
    /// </summary>
    public void RepositionLayer(
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect>? areas)
    {
        var w = _layer;
        if (w is null || !w.IsVisible) return;
        PlaceLayer(w, areas);
    }

    /// <summary>Esquece a estreia para a próxima exibição reposicionar.</summary>
    public void ResetLayerPlacement() => _layerPlaced = false;

    /// <summary>
    /// Núcleo puro do posicionamento inicial (testável), em pixels físicos:
    /// "top" = em cima dentro da primeira captura; "bottom" = embaixo
    /// dentro dela; resto = fora das áreas (canto inferior-direito,
    /// inferior-esquerdo, superior-direito, superior-esquerdo — o primeiro
    /// sem interseção; se tudo sobrepõe, inferior-direito). Sempre preso à
    /// área útil do monitor.
    /// </summary>
    internal static (int X, int Y) ComputePlacement(string place,
        int ww, int wh,
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect>? areas,
        int waX, int waY, int waW, int waH)
    {
        if ((place == "top" || place == "bottom") && areas is not null && areas.Count > 0)
        {
            var a = areas[0];
            int x = System.Math.Clamp(a.X, waX, System.Math.Max(waX, waX + waW - ww));
            int y = place == "top"
                ? System.Math.Clamp(a.Y, waY, System.Math.Max(waY, waY + waH - wh))
                : System.Math.Clamp(a.Y + a.H - wh, waY, System.Math.Max(waY, waY + waH - wh));
            return (System.Math.Max(waX, x), System.Math.Max(waY, y));
        }
        const int m = 12;
        var cands = new (int X, int Y)[]
        {
            (waX + waW - ww - m, waY + waH - wh - m),
            (waX + m, waY + waH - wh - m),
            (waX + waW - ww - m, waY + m),
            (waX + m, waY + m),
        };
        var best = cands[0];
        double bestOver = double.MaxValue;
        foreach (var c in cands)
        {
            double over = 0;
            if (areas is not null)
                foreach (var a in areas)
                    over += Overlap(c.X, c.Y, ww, wh, a);
            if (over <= 0) { best = c; break; }
            if (over < bestOver) { bestOver = over; best = c; }
        }
        return (System.Math.Max(waX, best.X), System.Math.Max(waY, best.Y));
    }

    /// <summary>
    /// Estreia fora das áreas de captura (escuro): primeiro canto livre,
    /// senão inferior-direito — padrão previsível que o usuário arrasta.
    /// </summary>
    private static void PlaceOutside(Avalonia.Controls.Window w,
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect>? areas)
    {
        try
        {
            var scr = w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary;
            if (scr is null) return;
            double s = scr.Scaling;
            var wa = scr.WorkingArea;
            int ww = (int)(w.Width * s), wh = (int)(w.Height * s);
            var (x, y) = ComputePlacement("outside",
                ww, wh, areas, wa.X, wa.Y, wa.Width, wa.Height);
            w.Position = new Avalonia.PixelPoint(x, y);
        }
        catch { }
    }

    /// <summary>Fase 1: área de interseção (testável).</summary>
    internal static double Overlap(int x, int y, int w, int h,
        Platform.ScreenRect a)
    {
        int x1 = System.Math.Max(x, a.X), y1 = System.Math.Max(y, a.Y);
        int x2 = System.Math.Min(x + w, a.X + a.W);
        int y2 = System.Math.Min(y + h, a.Y + a.H);
        if (x2 <= x1 || y2 <= y1) return 0;
        return (double)(x2 - x1) * (y2 - y1);
    }

    public DarkWindow? DarkWindowOrNull() => _dark;
    public LayerWindow? LayerWindowOrNull() => _layer;
    public IEnumerable<OverlayWindow> OverlayWindows() => _overlays.Values;

    /// <summary>Alguma janela de sobreposição visível (bandeja/esconder).</summary>
    public bool AnyOverlayVisible()
    {
        foreach (var w in _overlays.Values)
            if (w.IsVisible) return true;
        return false;
    }

    public void HideAll()
    {
        if (_dark is not null) { Platform.GuiFx.SetCaptureExclusion(_dark, false); _dark.Hide(); }
        if (_layer is not null) { Platform.GuiFx.SetCaptureExclusion(_layer, false); _layer.Hide(); }
        foreach (var w in _overlays.Values) w.Hide();
    }

    /// <summary>
    /// Desenha o quadro da sobreposição: uma janela por região, cada uma
    /// ancorada no seu retângulo de captura. Regiões sumidas têm a janela
    /// escondida (reuso). Roda na thread da UI (o sink posta).
    /// </summary>
    public void DrawOverlayFrame(Loop.OverlayFrame frame, int staySeconds = 0)
    {
        var present = new HashSet<int>();
        foreach (var rg in frame.Regions)
        {
            present.Add(rg.Index);
            if (!_overlays.TryGetValue(rg.Index, out var w) || w is null)
            {
                w = new OverlayWindow(_cfg, ScaleOf);
                int idx = rg.Index;
                w.Closed += (_, _) => _overlays.Remove(idx);
                w.ApplyRunning(_overlayRunning);   // clique/exclusão desde o nascimento
                _overlays[rg.Index] = w;
            }
            w.Substitute = _overlaySubstitute;
            if (!w.IsVisible) w.Show();
            var single = new Loop.OverlayFrame();
            single.Regions.Add(rg);
            w.DrawOverlay(single, staySeconds);
        }
        foreach (var kv in _overlays)
            if (!present.Contains(kv.Key)) kv.Value.Hide();
    }

    /// <summary>Estado correndo para as janelas existentes e as futuras.</summary>
    public void SetOverlayRunning(bool running)
    {
        _overlayRunning = running;
        foreach (var w in _overlays.Values) w.ApplyRunning(running);
    }

    /// <summary>Atalho de captura: todas ficam capturáveis por P-91.</summary>
    public void SetOverlayScreenshotCapture()
    {
        foreach (var w in _overlays.Values) w.SetScreenshotCapture();
    }

    /// <summary>Janela anexada: todas sempre capturáveis (RF-348).</summary>
    public void SetOverlayAttachedMode(bool attached)
    {
        foreach (var w in _overlays.Values) w.SetAttachedMode(attached);
    }

    public void SetRunning(bool running)
    {
        _dark?.SetRunning(running, _cfg.Profile, _cfg.Advanced, _cfg.App);
        _dark?.ApplySettings(_cfg.Profile, _cfg.Advanced, _cfg.App);
        if (_layer is not null) _layer.ApplyRunning(running);
        SetOverlayRunning(running);
    }

    public void SaveLayerGeometry()
    {
        _layer?.SaveGeometry();
        _cfg.SaveProfile();   // RF-340: posição/tamanho persistem
    }

    /// <summary>Cria o sink do modo vigente para o laço.</summary>
    public Loop.IDisplaySink MakeSink()
    {
        if (_cfg.Profile.WindowMode == "layer" && _layer is not null)
            return new LayerSink(_layer);
        if (_cfg.Profile.WindowMode == "overlay" || _cfg.Profile.WindowMode == "replace")
            return new OverlaySink(this);   // janelas nascem no primeiro quadro
        return new DarkSink(Dark());
    }
}
