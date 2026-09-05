using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Gort.Config;
using Gort.Core;
using Gort.Loop;
using Gort.Overlay;
using Gort.Regions;
using Gort.Store;
using SkiaSharp;

namespace Gort.UI;

/// <summary>
/// Modo sobreposição (19.4, RF-344..386): janela sem bordas com transparência
/// por pixel cobrindo a união dos monitores (RF-344), sempre no topo sem
/// taskbar (RF-345), excluída de capturas (RF-346) salvo atalho de captura
/// (RF-347) ou janela anexada (RF-348). Redimensionada por desenho para a
/// união das áreas × P-92 (RF-349), acumulativa (RF-350). Cores configuradas
/// ou automáticas (cap. 20). Sem coleta forçada (RF-380), com trava de
/// reentrância (RF-381) e abandono sem handle (RF-382).
/// </summary>
public sealed class OverlayWindow : Window
{
    private readonly ConfigService _cfg;
    private readonly Func<Platform.ScreenRect, double> _scaleOf;
    private readonly Image _view = new() { Stretch = Stretch.Fill };
    private readonly OverlayReuseCache _reuse = new();
    // Medidas por (família, orientação, texto, tamanho): persistem entre
    // quadros com teto (hit ~100% em texto repetido). Chave inclui a
    // família para invalidação automática ao trocar de fonte.
    private readonly Dictionary<(string? Fam, bool Vert, string Text, float Size), (float W, float H)> _measure = new();
    private const int MeasureCap = 2000;
    private SKBitmap? _canvas;                                                  // RF-379
    private readonly object _drawLock = new();                                  // RF-381
    private bool _running;
    private bool _suspended;
    /// <summary>Fase 2: cobre o original sob a tradução (modo novo).</summary>
    public bool Substitute { get; set; }
    private int _winX, _winY;
    private int _accX1, _accY1, _accX2, _accY2;
    private bool _hasAcc;
    private int _taskId;
    private DispatcherTimer? _stayTimer;

    public OverlayWindow(ConfigService cfg, Func<Platform.ScreenRect, double> scaleOf)
    {
        _cfg = cfg;
        _scaleOf = scaleOf;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;                                            // RF-345
        ShowInTaskbar = false;                                     // RF-345
        CanResize = false;
        Content = _view;
        SizeToFullUnion();
    }

    private void SizeToFullUnion()
    {
        // RF-344: cobre a união de todos os monitores (+ deslocamento p/ negativas).
        var cap = Platform.PlatformFactory.Current.Capture;
        var v = cap.VirtualScreen;
        Position = new PixelPoint(v.X, v.Y);
        _winX = v.X; _winY = v.Y;
        double s = 1.0;
        try { s = Screens.Primary?.Scaling ?? 1.0; } catch { }
        Width = Math.Max(1, v.W / s);
        Height = Math.Max(1, v.H / s);
    }

    public void ApplyRunning(bool running)
    {
        _running = running;
        if (running)
        {
            // RF-383: limpa, zera acúmulo, libera travas, sincroniza.
            _hasAcc = false;
            _taskId = (_taskId + 1) % Params.P132_TaskCounterReset;
            _stayTimer?.Stop();
            ClearCanvas();
            Platform.GuiFx.SetClickThrough(this, true);
            Platform.GuiFx.SetCaptureExclusion(this, true);  // RF-346 (Win; demais: concealer)
            Platform.GuiFx.SyncCompositor();                  // RF-383/C9
        }
        else
        {
            _hasAcc = false;                                       // RF-350: zera
            _stayTimer?.Stop();   // sem isso um snapshot antigo apagava o quadro novo
            ClearCanvas();
        }
    }

    /// <summary>RF-347: atalho de captura → capturável por P-91, sem atualizar.</summary>
    public void SetScreenshotCapture()
    {
        Platform.GuiFx.SetCaptureExclusion(this, false);
        _suspended = true;
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Params.P91_ScreenshotMs) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            _suspended = false;
            Platform.GuiFx.SetCaptureExclusion(this, true);
        };
        t.Start();
    }

    /// <summary>RF-348: com janela anexada, sempre capturável.</summary>
    public void SetAttachedMode(bool attached)
    {
        Platform.GuiFx.SetCaptureExclusion(this, !attached);
    }

    private void ClearCanvas()
    {
        Dispatcher.UIThread.InvokeAsync(() => _view.Source = null);
    }

    public void DrawOverlay(OverlayFrame frame, int staySeconds = 0)
    {
        if (_suspended) return;                                    // RF-347
        if (!Dispatcher.UIThread.CheckAccess()
            && Platform.GuiFx.HandleOf(this) is null) return;  // RF-382
        lock (_drawLock)                                           // RF-381
        {
            try { DrawInner(frame); }
            finally { /* trava sempre liberada */ }
        }
        if (staySeconds > 0)                                       // RF-384/385
        {
            int id = ++_taskId % Params.P132_TaskCounterReset;
            _stayTimer?.Stop();
            _stayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(staySeconds),
            };
            _stayTimer.Tick += (_, _) =>
            {
                _stayTimer?.Stop();
                if (id == _taskId) ClearCanvas();                  // cancela se recomeçou
            };
            _stayTimer.Start();
        }
    }

    /// <summary>
    /// RF-353 puro e testável: limita a origem por baixo à posição do
    /// cliente quando há cliente (janela anexada); sem cliente, devolve
    /// intacto — (0,0) significa ausência, não coordenada.
    /// </summary>
    internal static (float X, float Y) ClampToClient(float ox, float oy,
        bool hasClient, int clientX, int clientY, int winX, int winY) =>
        hasClient
            ? (Math.Max(ox, clientX - winX), Math.Max(oy, clientY - winY))
            : (ox, oy);

    private void DrawInner(OverlayFrame frame)
    {
        var p = _cfg.Profile;
        if (frame.Regions.Count == 0) { ClearCanvas(); return; }

        // União das áreas × P-92, acumulativa (RF-349/350 🔒).
        int ux1 = int.MaxValue, uy1 = int.MaxValue, ux2 = int.MinValue, uy2 = int.MinValue;
        foreach (var rg in frame.Regions)
        {
            ux1 = Math.Min(ux1, rg.Rect.X); uy1 = Math.Min(uy1, rg.Rect.Y);
            ux2 = Math.Max(ux2, rg.Rect.X + rg.Rect.W); uy2 = Math.Max(uy2, rg.Rect.Y + rg.Rect.H);
        }
        int uw = (int)((ux2 - ux1) * Params.P92_OverlaySlack);
        int uh = (int)((uy2 - uy1) * Params.P92_OverlaySlack);
        if (!_hasAcc) { _accX1 = ux1; _accY1 = uy1; _accX2 = ux1 + uw; _accY2 = uy1 + uh; _hasAcc = true; }
        else
        {
            if (ux1 + uw <= _accX2 - _accX1 + _accX1 && uy1 + uh <= _accY2 - _accY1 + _accY1) { }
            else
            {
                _accX1 = Math.Min(_accX1, ux1); _accY1 = Math.Min(_accY1, uy1);
                _accX2 = Math.Max(_accX2, ux1 + uw); _accY2 = Math.Max(_accY2, uy1 + uh);
            }
        }
        // Posiciona junto (já roda na thread da UI via OverlaySink): itens
        // e janela sempre concordam na origem — sem atraso de um quadro.
        // Escala do monitor onde a janela está (não o primário): cada
        // janela usa a sua, resolvida na hora.
        {
            double s = 1.0;
            try { s = Screens.ScreenFromWindow(this)?.Scaling ?? Screens.Primary?.Scaling ?? 1.0; } catch { }
            Position = new PixelPoint(_accX1, _accY1);
            _winX = _accX1; _winY = _accY1;
            Width = Math.Max(1, (_accX2 - _accX1) / s);
            Height = Math.Max(1, (_accY2 - _accY1) / s);
        }

        // Monta itens: origem RF-352, recorte RF-354, reuso RF-203.
        var items = new List<OverlayLayout.Item>();
        var present = new HashSet<int>();
        foreach (var rg in frame.Regions)
        {
            present.Add(rg.Index);
            double scale = _scaleOf(rg.Rect);
            float borderHalf = Regions.FrameGeometry.Chrome(scale).Border / 2f;
            foreach (var b in rg.Blocks)
            {
                var (ox, oy, ow, oh) = OverlayLayout.Origin(
                    rg.Rect.X, rg.Rect.Y, borderHalf,
                    b.OX, b.OY, b.OW, b.OH, rg.Zoom, _winX, _winY);   // RF-352
                // RF-353: limita por baixo à posição do cliente — só
                // quando há cliente (anexada). Sem ele, (0,0) é ausência:
                // aplicar o máximo deslocava o texto para o lugar errado.
                (ox, oy) = ClampToClient(ox, oy, rg.HasClient,
                    rg.ClientX, rg.ClientY, _winX, _winY);
                // Recorta pelo retângulo da área (RF-354).
                float cx1 = Math.Max(ox, rg.Rect.X - _winX);
                float cy1 = Math.Max(oy, rg.Rect.Y - _winY);
                float cx2 = Math.Min(ox + ow, rg.Rect.X + rg.Rect.W - _winX);
                float cy2 = Math.Min(oy + oh, rg.Rect.Y + rg.Rect.H - _winY);
                if (cx2 <= cx1 || cy2 <= cy1) continue;              // descartado
                items.Add(new OverlayLayout.Item
                {
                    Area = rg.Index, Title = b.IsTitle, Vertical = b.Vertical,
                    Text = b.Text, X = cx1, Y = cy1, W = cx2 - cx1, H = cy2 - cy1,
                });
            }
        }
        _reuse.Prune(present);                                     // RF-204

        // Cores por bloco (configuradas ou automáticas).
        var colors = ResolveColors(frame, items);
        foreach (var rg in frame.Regions) rg.OrigBytes = null;   // RF-099: libera
        OverlayLayout.ResolveCollisions(items);                    // RF-355..358
        foreach (var it in items)
        {
            OverlayLayout.ApplyContent(it, p.OverlayOutline);      // RF-359
            if (it.CW <= 0 || it.CH <= 0) { it.Clipped = true; continue; }  // recortado
        }
        items.RemoveAll(i => i.Clipped && (i.CW <= 0 || i.CH <= 0));

        LayoutFonts(items, frame, ComputePrefs(frame, items));
        RenderItems(items, colors);
    }

    private Dictionary<OverlayLayout.Item, BlockColors> ResolveColors(
        OverlayFrame frame, List<OverlayLayout.Item> items)
    {
        var p = _cfg.Profile;
        var map = new Dictionary<OverlayLayout.Item, BlockColors>();
        bool auto = p.AutoColorMaster && (p.AutoColorFg || p.AutoColorBg);
        // A análise usa a imagem e as palavras da REGIÃO inteira: resultado
        // idêntico para todos os blocos da mesma área — calcula uma vez.
        var areaCache = new Dictionary<int, BlockColors>();
        foreach (var it in items)
        {
            if (areaCache.TryGetValue(it.Area, out var cached))
            {
                map[it] = cached;
                continue;
            }
            var col = new BlockColors
            {
                Font = (p.TextColor[0], p.TextColor[1], p.TextColor[2]),
                Bg = (p.BgColor[1], p.BgColor[2], p.BgColor[3]),
                BgAlpha = p.BgColor[0],
                C1 = (p.Outline1[0], p.Outline1[1], p.Outline1[2]),
                C2 = (p.Outline2[0], p.Outline2[1], p.Outline2[2]),
                Auto = false,
            };
            if (auto)
            {
                var rg = frame.Regions.Find(r => r.Index == it.Area);
                if (rg?.OrigBytes is not null)
                {
                    var words = new List<ColorAnalysis.WordBox>();
                    // Caixas das palavras em coordenadas da imagem original (RF-395).
                    double sx = (double)rg.OrigW / Math.Max(1, (int)Math.Round(rg.Rect.W * rg.Zoom / 1.0));
                    // (zoom: imagem tratada = captura×zoom; original = captura)
                    foreach (var b in rg.Blocks)
                        foreach (var wb in b.WordBoxes)
                            words.Add(ToOrig(wb, rg, sx));
                    var an = ColorAnalysis.Analyze(rg.OrigBytes, rg.OrigW, rg.OrigH, 4,
                        0, 0, rg.OrigW, rg.OrigH, words);
                    if (!an.Failed)
                    {
                        bool paintBg = p.TextBackground && p.BgColor[0] > 0;  // RF-412
                        if (p.AutoColorFg) { col.Font = an.Font; col.Auto = true; }
                        if (p.AutoColorBg)
                        {
                            col.Bg = an.Background;                 // RF-414: alfa do usuário
                            col.Auto = true;
                        }
                        if (col.Auto && (an.UsedFallback || an.ContrastFixed || paintBg))
                        {
                            var (c1, c2) = ColorAnalysis.DeriveOutlines(col.Font);  // RF-393
                            col.C1 = c1; col.C2 = c2;
                        }
                    }
                }
            }
            map[it] = col;
            areaCache[it.Area] = col;
        }
        return map;
    }

    private static ColorAnalysis.WordBox ToOrig((int X, int Y, int W, int H) wb,
        OverlayRegion rg, double s) => new()
        {
            // RF-395: piso no topo/esquerda, teto na base/direita, satura.
            X = Math.Clamp((int)Math.Floor(wb.X * s), 0, Math.Max(0, rg.OrigW - 1)),
            Y = Math.Clamp((int)Math.Floor(wb.Y * s), 0, Math.Max(0, rg.OrigH - 1)),
            W = Math.Max(0, (int)Math.Ceiling((wb.X + wb.W) * s)
                - Math.Clamp((int)Math.Floor(wb.X * s), 0, rg.OrigW)),
            H = Math.Max(0, (int)Math.Ceiling((wb.Y + wb.H) * s)
                - Math.Clamp((int)Math.Floor(wb.Y * s), 0, rg.OrigH)),
        };

    /// <summary>
    /// RF-360 passos 1–2: próprio = mediana das linhas (altura p/ horizontal,
    /// largura p/ vertical; senão estimativa RF-164), em px da imagem.
    /// </summary>
    private static Dictionary<OverlayLayout.Item, (float Own, float Body)> ComputePrefs(
        OverlayFrame frame, List<OverlayLayout.Item> items)
    {
        var map = new Dictionary<OverlayLayout.Item, (float, float)>();
        var byKey = new Dictionary<(int Area, int N), OverlayBlock>();
        foreach (var rg in frame.Regions)
        {
            var perArea = new List<OverlayBlock>(rg.Blocks);
            for (int i = 0; i < perArea.Count; i++)
                byKey[(rg.Index, i)] = perArea[i];
        }
        // Ordem dos itens segue a dos blocos por região:
        var counters = new Dictionary<int, int>();
        foreach (var it in items)
        {
            int n = counters.TryGetValue(it.Area, out var c) ? c : 0;
            counters[it.Area] = n + 1;
            float own = 10;
            if (byKey.TryGetValue((it.Area, n), out var b) && b.LineBoxes.Count > 0)
            {
                var sizes = new List<float>();
                foreach (var lb in b.LineBoxes)
                    sizes.Add(b.Vertical ? lb.W : lb.H);
                sizes.Sort();
                own = sizes.Count % 2 == 1 ? sizes[sizes.Count / 2]
                    : (sizes[sizes.Count / 2 - 1] + sizes[sizes.Count / 2]) / 2;
                if (own <= 0) own = EstimateFont(b);
            }
            map[it] = (own, 0);
        }
        return map;
    }

    private static float EstimateFont(OverlayBlock b)
    {
        var mins = new List<int>();
        foreach (var wb in b.WordBoxes)
            if (wb.W > 0 && wb.H > 0) mins.Add(Math.Min(wb.W, wb.H));
        if (mins.Count == 0) return 10;   // P-38
        mins.Sort();
        return mins.Count % 2 == 1 ? mins[mins.Count / 2]
            : (mins[mins.Count / 2 - 1] + mins[mins.Count / 2]) / 2f;
    }

    private sealed class BlockColors
    {
        public (byte R, byte G, byte B) Font, Bg, C1, C2;
        public byte BgAlpha;
        public bool Auto;
    }

    private void LayoutFonts(List<OverlayLayout.Item> items, OverlayFrame frame,
        Dictionary<OverlayLayout.Item, (float Own, float Body)> prefs)
    {
        var p = _cfg.Profile;
        double dpi = 96 * 1.0;   // P-141: 96 (a escala entra na medida Skia)
        foreach (var grp in GroupByArea(items, frame))
        {
            // Corpo: mediana dos próprios entre não-títulos mesma orientação.
            var bodies = new List<float>();
            foreach (var it in grp.Items)
                if (!it.Title) bodies.Add(prefs[it].Own);
            bodies.Sort();
            float bodyOrig = bodies.Count == 0 ? prefs[grp.Items[0]].Own
                : bodies[bodies.Count / 2];
            // Líder: mais acima/esquerda da área.
            OverlayLayout.Item? leader = null;
            foreach (var it in grp.Items)
                if (leader is null || it.Y < leader.Y
                    || (it.Y == leader.Y && it.X < leader.X)) leader = it;
            foreach (var it in grp.Items)
            {
                float ownOrig = prefs[it].Own;
                float prefDraw = p.AutoFontSize
                    ? OverlayLayout.Preferred(ToDrawPx(ownOrig, grp), ToDrawPx(bodyOrig, grp),
                        it.Title, it == leader)                       // RF-360 🔒
                    : (float)(p.FontSize * dpi / 72);
                float minDraw = (float)((p.AutoFontSize ? p.AutoMinPt : 1) * dpi / 72);
                float maxDraw = (float)((p.AutoFontSize ? p.AutoMaxPt : 10000) * dpi / 72);
                prefDraw = Math.Clamp(prefDraw, minDraw, maxDraw);     // RF-361
                ExpandRect(it, prefDraw, grp);                          // RF-362
                var (size, _) = OverlayLayout.FindFont(prefDraw, minDraw,   // RF-363
                    s => FitsFixed(it, s));
                it.FontPx = size;
                var (ok, placed) = Fit(it, size);
                it.Lines.Clear();
                it.Lines.AddRange(placed);
                if (!ok) it.Clipped = true;   // desenhado assim mesmo + marcado
            }
        }
    }

    /// <summary>RF-360 passo 4→px: orig/zoom × P-95 (dpi cancela).</summary>
    private static float ToDrawPx(float origPx, AreaGroup grp) =>
        origPx / (float)Math.Max(0.01, grp.Zoom) * (float)Core.Params.P95_FontScale;  // 🔒 1,15

    private sealed class AreaGroup
    {
        public List<OverlayLayout.Item> Items = new();
        public (float X, float Y, float W, float H) AreaBounds;
        public double Zoom = 1;
        public List<OverlayLayout.Item> Neighbors = new();
    }

    private static List<AreaGroup> GroupByArea(List<OverlayLayout.Item> items, OverlayFrame frame)
    {
        var map = new Dictionary<int, AreaGroup>();
        foreach (var it in items)
        {
            if (!map.TryGetValue(it.Area, out var g))
            {
                g = new AreaGroup();
                var rg = frame.Regions.Find(r => r.Index == it.Area);
                if (rg is not null)
                {
                    g.AreaBounds = (rg.Rect.X, rg.Rect.Y, rg.Rect.W, rg.Rect.H);
                    g.Zoom = rg.Zoom;
                }
                map[it.Area] = g;
            }
            g.Items.Add(it);
        }
        foreach (var g in map.Values) g.Neighbors.AddRange(g.Items);
        return new List<AreaGroup>(map.Values);
    }

    /// <summary>
    /// RF-362: 1) na direção de leitura (só horizontais, se quebrar em mais
    /// linhas que o original): cresce à direita até a área ou o vizinho com
    /// sobreposição vertical, por busca binária; 2) para caber a fonte: cresce
    /// para baixo (horizontais) ou esquerda (verticais), por busca binária.
    /// </summary>
    private void ExpandRect(OverlayLayout.Item it, float size, AreaGroup grp)
    {
        var p = _cfg.Profile;
        bool vert = p.KeepDirection && it.Vertical;
        var (_, placed0) = Fit(it, size);
        if (!vert)
        {
            int origLines = Math.Max(1, placed0.Count);
            var (fitNow, _) = Fit(it, size);
            if (!fitNow || placed0.Count > origLines)
            {
                // Limite: área ou vizinho sobreposto verticalmente mais próximo.
                float limit = grp.AreaBounds.X + grp.AreaBounds.W;
                foreach (var nb in grp.Neighbors)
                {
                    if (nb == it || nb.Vertical) continue;
                    bool overlap = nb.Y < it.Y + it.CH && it.Y < nb.Y + nb.CH;
                    if (overlap && nb.X >= it.X + it.CW - 1)
                        limit = Math.Min(limit, nb.X);
                }
                float maxW = Math.Max(it.CW, limit - it.CX);
                it.CW = OverlayLayout.ExpandToFit(it.CW, maxW,
                    w => FitsWidth(it, size, w));
            }
            float maxH = grp.AreaBounds.Y + grp.AreaBounds.H - it.CY;
            it.CH = OverlayLayout.ExpandToFit(it.CH, Math.Max(it.CH, maxH),
                h => FitsHeight(it, size, h));
        }
        else
        {
            float maxH = grp.AreaBounds.Y + grp.AreaBounds.H - it.CY;
            it.CH = OverlayLayout.ExpandToFit(it.CH, Math.Max(it.CH, maxH),
                h => FitsHeight(it, size, h));
        }
    }

    private bool FitsWidth(OverlayLayout.Item it, float size, float w)
    {
        float ow = it.CW; it.CW = w;
        var (ok, _) = Fit(it, size);
        it.CW = ow;
        return ok;
    }

    private bool FitsHeight(OverlayLayout.Item it, float size, float h)
    {
        float oh = it.CH; it.CH = h;
        var (ok, _) = Fit(it, size);
        it.CH = oh;
        return ok;
    }

    private bool FitsFixed(OverlayLayout.Item it, float size)
    {
        var (ok, _) = Fit(it, size);
        return ok;
    }

    private (bool Fits, List<(string, float, float)> Placed) Fit(
        OverlayLayout.Item it, float size)
    {
        var p = _cfg.Profile;
        bool vert = p.KeepDirection && it.Vertical;              // RF-375
        Func<string, float> hm = t => HMeasure(t, size);
        Func<string, float> vm = t => VMeasure(t, size);
        return OverlayLayout.Place(it, size, hm, vm, vert);      // RF-376 implícito
    }

    // Fonte resolvida uma vez por família + um SKFont por tamanho.
    // Evita milhares de FromFamilyName por quadro (só invalida ao trocar).
    private string? _fontFam;
    private SKTypeface? _fontFace;
    private readonly Dictionary<float, SKFont> _fontSizes = new();

    private SKFont FontFor(float size)
    {
        string? fam = Family();
        if (fam != _fontFam)
        {
            foreach (var f in _fontSizes.Values) f.Dispose();
            _fontSizes.Clear();
            _fontFace?.Dispose();
            _fontFace = null;
            _fontFam = fam;
        }
        if (!_fontSizes.TryGetValue(size, out var font))
        {
            _fontFace ??= SkiaText.ResolveFont(fam);
            font = new SKFont(_fontFace, size);
            _fontSizes[size] = font;
        }
        return font;
    }

    private float HMeasure(string text, float size)
    {
        string? fam = Family();
        var key = (fam, false, text, size);
        if (_measure.TryGetValue(key, out var v)) { _measureHits++; return v.W; }
        _measureMiss++;
        var font = FontFor(size);
        float wPath = 0;
        try
        {
            using var path = font.GetTextPath(text, new SKPoint(0, 0));
            path.GetBounds(out var bounds);
            wPath = bounds.Width;
        }
        catch { }
        float w = Math.Max(wPath, font.MeasureText(text));      // RF-373
        if (_measure.Count >= MeasureCap) _measure.Clear();
        _measure[key] = (w, size);
        return w;
    }

    private float VMeasure(string text, float size)
    {
        string? fam = Family();
        var key = (fam, true, text, size);
        if (_measure.TryGetValue(key, out var v)) { _measureHits++; return v.H; }
        _measureMiss++;
        var font = FontFor(size);
        float hPath = 0;
        try
        {
            using var path = font.GetTextPath(text, new SKPoint(0, 0));
            path.GetBounds(out var bounds);
            hPath = bounds.Height;
        }
        catch { }
        float stacked = text.Length * size * (float)Core.Params.P98_LineAdvance;
        float h = Math.Max(hPath, stacked);                      // RF-373 + empilhado
        if (_measure.Count >= MeasureCap) _measure.Clear();
        _measure[key] = (size, h);
        return h;
    }

    private string? Family()
    {
        var f = _cfg.Profile.FontFamily;
        return string.IsNullOrWhiteSpace(f) ? null : f;          // RF-387
    }

    private void RenderItems(List<OverlayLayout.Item> items,
        Dictionary<OverlayLayout.Item, BlockColors> colors)
    {
        var total = System.Diagnostics.Stopwatch.StartNew();   // RF-494
        var p = _cfg.Profile;
        double s = 1.0;
        try { s = Screens.ScreenFromWindow(this)?.Scaling ?? Screens.Primary?.Scaling ?? 1.0; } catch { }
        int pw = Math.Max(1, (int)Math.Round(Width * s));
        int ph = Math.Max(1, (int)Math.Round(Height * s));
        if (_canvas is null || _canvas.Width != pw || _canvas.Height != ph)  // RF-379
        {
            _canvas?.Dispose();
            _canvas = new SKBitmap(pw, ph);
        }
        using var canvas = new SKCanvas(_canvas);
        canvas.Clear(new SKColor(240, 248, 255, 0));             // P-156 🔒
        using var face = SkiaText.ResolveFont(Family());
        bool wordAreas = Debug.DebugFlags.ShowWordAreas;        // RF-491
        using var dbgPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 90),                   // P-157 🔒
            Style = SKPaintStyle.Fill,
        };
        foreach (var it in items)
        {
            var col = colors[it];
            if (wordAreas)
            {
                // Diagnóstico: retângulos das origens em vez do fundo normal.
                System.Diagnostics.Trace.WriteLine(
                    $"GORT dbg: '{it.Text}' em {it.X:F0},{it.Y:F0} {it.W:F0}x{it.H:F0} " +
                    $"fonte {it.FontPx:F1} recortado={it.Clipped}");
                canvas.DrawRect((float)(it.X * s), (float)(it.Y * s),
                    (float)(it.W * s), (float)(it.H * s), dbgPaint);
                continue;
            }
            if (Substitute && _running)
            {
                // Fase 2: cobre o original com fundo opaco no retângulo
                // inteiro do bloco — a tradução substitui o texto de baixo.
                var bg = new SKColor(col.Bg.R, col.Bg.G, col.Bg.B, 255);
                using var paint = new SKPaint { Color = bg };
                canvas.DrawRect((float)(it.X * s), (float)(it.Y * s),
                    (float)(it.W * s), (float)(it.H * s), paint);
            }
            else if (p.TextBackground && _running)                    // RF-377
            {
                var bg = new SKColor(col.Bg.R, col.Bg.G, col.Bg.B,
                    p.BgTransparency ? col.BgAlpha : (byte)255); // RF-378 🔒
                using var paint = new SKPaint { Color = bg };
                canvas.DrawRect((float)(it.X * s), (float)(it.Y * s),
                    (float)(it.W * s), (float)(it.H * s), paint);
            }
            bool vert = p.KeepDirection && it.Vertical;
            var fill = new SKColor(col.Font.R, col.Font.G, col.Font.B);
            var c1 = new SKColor(col.C1.R, col.C1.G, col.C1.B);
            var c2 = new SKColor(col.C2.R, col.C2.G, col.C2.B);
            foreach (var (text, lx, ly) in it.Lines)
            {
                if (!vert)
                    DrawOutlined(canvas, text,
                        (float)(lx * s), (float)(ly * s),
                        face, (float)(it.FontPx * s), fill, c1, c2,
                        p.OverlayOutline);
                else
                {
                    // Vertical: empilha caracteres de cima para baixo.
                    float adv = (float)(it.FontPx * s * Core.Params.P98_LineAdvance);
                    for (int i = 0; i < text.Length; i++)
                        DrawOutlined(canvas, text[i].ToString(),
                            (float)(lx * s),
                            (float)(ly * s + i * adv),
                            face, (float)(it.FontPx * s), fill, c1, c2,
                            p.OverlayOutline);
                }
            }
        }
        var wb = new WriteableBitmap(new PixelSize(pw, ph), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
            Marshal.Copy(_canvas.Bytes, 0, fb.Address, _canvas.Bytes.Length);
        _view.Source = wb;
        total.Stop();
        if (Debug.DebugFlags.SaveAnalysis) WriteDrawFile(items, colors, total);  // RF-493
    }

    private int _measureHits, _measureMiss;

    /// <summary>RF-493/494: desenho por bloco + tempos + acertos do cache.</summary>
    private void WriteDrawFile(List<OverlayLayout.Item> items,
        Dictionary<OverlayLayout.Item, BlockColors> colors,
        System.Diagnostics.Stopwatch total)
    {
        try
        {
            var p = _cfg.Profile;
            using var ms = new MemoryStream();
            using (var w = new System.Text.Json.Utf8JsonWriter(ms,
                       new System.Text.Json.JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteString("window", $"{_winX},{_winY},{(int)Width},{(int)Height}");
                w.WriteString("font", Family() ?? "default");
                w.WriteNumber("fontMin", p.AutoMinPt);
                w.WriteNumber("fontMax", p.AutoMaxPt);
                w.WriteBoolean("outline", p.OverlayOutline);
                w.WriteBoolean("autoColor", p.AutoColorMaster);
                w.WriteStartArray("blocks");
                foreach (var it in items)
                {
                    var col = colors[it];
                    w.WriteStartObject();
                    w.WriteString("text", it.Text);
                    w.WriteBoolean("title", it.Title);
                    w.WriteString("orientation", it.Vertical ? "vertical" : "horizontal");
                    w.WriteString("origin", $"{it.X:F0},{it.Y:F0},{it.W:F0},{it.H:F0}");
                    w.WriteString("view", $"{it.X:F0},{it.Y:F0},{it.W:F0},{it.H:F0}");
                    w.WriteString("content", $"{it.CX:F0},{it.CY:F0},{it.CW:F0},{it.CH:F0}");
                    w.WriteNumber("fontPx", it.FontPx);
                    w.WriteString("fontRgb",
                        $"{col.Font.R},{col.Font.G},{col.Font.B}");
                    w.WriteString("bgRgb", $"{col.Bg.R},{col.Bg.G},{col.Bg.B}");
                    w.WriteBoolean("auto", col.Auto);
                    w.WriteBoolean("clipped", it.Clipped);
                    w.WriteNumber("lines", it.Lines.Count);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartObject("times");
                w.WriteNumber("totalMs", total.Elapsed.TotalMilliseconds);
                w.WriteEndObject();
                w.WriteStartObject("measureCache");
                w.WriteNumber("hits", _measureHits);
                w.WriteNumber("misses", _measureMiss);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            System.IO.Directory.CreateDirectory(Core.Paths.DebugDir);
            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(Core.Paths.DebugDir,
                    $"cycle-{DateTime.Now:yyyyMMdd-HHmmss-fff}-draw.json"),
                ms.ToArray());
            Debug.DebugLog.PruneDebugDir();
        }
        catch { }
    }

    private static void DrawOutlined(SKCanvas canvas, string text, float x, float y,
        SKTypeface face, float size, SKColor fill, SKColor c1, SKColor c2,
        bool outline)
    {
        using var font = new SKFont(face, size);
        using var pFill = new SKPaint
        {
            Color = fill,
            IsAntialias = true, StrokeJoin = SKStrokeJoin.Round,   // RF-386
        };
        if (outline && SkiaText.VectorOk)
        {
            using var pIn = new SKPaint
            {
                Color = c1, IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)Core.Params.P81_OutlineInner,
                StrokeJoin = SKStrokeJoin.Round,
            };
            using var pOut = new SKPaint
            {
                Color = c2, IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)Core.Params.P80_OutlineOuter,
                StrokeJoin = SKStrokeJoin.Round,
            };
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, pOut);
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, pIn);
        }
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, pFill);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
