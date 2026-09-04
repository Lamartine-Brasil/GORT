using System;
using System.Collections.Generic;
using Gort.Config;

namespace Gort.Imaging;

/// <summary>Imagem tratada pronta para OCR (6.3) + memória da escala.</summary>
public sealed class ProcessedImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Channels { get; init; }
    public required byte[] Bytes { get; init; }
    /// <summary>Fator de ampliação aplicado (= P-22 efetivo).</summary>
    public double Zoom { get; init; }
    public bool IsBinarized { get; init; }
}

/// <summary>
/// Pré-processamento (cap. 13): recorte por exclusão → filtro → erosão →
/// ampliação (pseudocódigo do capítulo). Usa o mesmo critério do conta-gotas
/// (ColorFilter), logo a pré-visualização é exatamente a entrada do OCR.
/// </summary>
public static class Preprocess
{
    public static ProcessedImage Run(
        RegionImage src,
        IReadOnlyList<Platform.ScreenRect> exclusionsLocal,
        FilterMode mode,
        IReadOnlyList<(int R, int G, int B, int S1, int S2, int V1, int V2)> groups,
        int threshold,
        bool erode,
        double zoom,
        bool disabled)
    {
        if (disabled)
            return new ProcessedImage   // RF-118: colorida original, sem nada
            {
                Width = src.Width, Height = src.Height,
                Channels = src.Channels, Bytes = (byte[])src.Bytes.Clone(),
                Zoom = 1.0, IsBinarized = false,
            };

        int w = src.Width, h = src.Height;
        var work = (byte[])src.Bytes.Clone();   // BGRA; geometria inalterada (RF-103)

        // 1. Exclusões antes de tudo (RF-101), preenchidas para ficarem
        //    invisíveis ao OCR (RF-102).
        foreach (var e in exclusionsLocal)
        {
            int x1 = Math.Clamp(e.X, 0, w), y1 = Math.Clamp(e.Y, 0, h);
            int x2 = Math.Clamp(e.X + e.W, 0, w), y2 = Math.Clamp(e.Y + e.H, 0, h);
            if (x2 <= x1 || y2 <= y1) continue;
            if (mode == FilterMode.None)
                FillDominantBorder(work, w, h, x1, y1, x2, y2);
            else
                FillFailingColor(work, w, x1, y1, x2, y2, mode, groups, threshold);
        }

        // 2. Filtro → binária (RF-108/109) ou colorida sem binarizar (RF-110).
        byte[] bin;
        bool color;
        if (mode == FilterMode.None)
        {
            bin = work; color = true;
        }
        else
        {
            // Binarize já devolve array novo em 0/255 — sem cópia extra.
            bin = ColorFilter.Binarize(work, w, h, mode, groups, threshold);
            color = false;
        }

        // 3. Erosão 3×3, 1 iteração, sobre a binarizada e ANTES da ampliação (RF-112).
        if (erode && !color) bin = Erode3x3(bin, w, h);

        // 4. Ampliação (RF-113). Zoom 1:1 devolve o buffer, sem resize.
        int zw = Math.Max(1, (int)Math.Round(w * zoom));
        int zh = Math.Max(1, (int)Math.Round(h * zoom));
        byte[] scaled = (zw == w && zh == h) ? bin
            : color ? ResizeBgra(bin, w, h, zw, zh)
            : ResizeGray(bin, w, h, zw, zh);

        return new ProcessedImage
        {
            Width = zw, Height = zh,
            Channels = color ? 4 : 1, Bytes = scaled,
            Zoom = zoom, IsBinarized = !color,
        };
    }

    // ---- RF-102: preenchimento invisível ----

    /// <summary>
    /// Sem filtro: cor dominante da borda da própria região removida.
    /// </summary>
    internal static void FillDominantBorder(byte[] bgra, int w, int h,
        int x1, int y1, int x2, int y2)
    {
        var freq = new Dictionary<int, int>();
        void Sample(int x, int y)
        {
            int o = (y * w + x) * 4;
            int key = (bgra[o + 2] << 16) | (bgra[o + 1] << 8) | bgra[o];
            freq[key] = freq.TryGetValue(key, out var c) ? c + 1 : 1;
        }
        for (int x = x1; x < x2; x++) { Sample(x, y1); if (y2 - 1 != y1) Sample(x, y2 - 1); }
        for (int y = y1 + 1; y < y2 - 1; y++) { Sample(x1, y); if (x2 - 1 != x1) Sample(x2 - 1, y); }
        int best = 0, bestN = -1;
        foreach (var kv in freq) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
        byte r = (byte)(best >> 16), g = (byte)((best >> 8) & 255), b = (byte)(best & 255);
        for (int y = y1; y < y2; y++)
            for (int x = x1; x < x2; x++)
            {
                int o = (y * w + x) * 4;
                bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
            }
    }

    /// <summary>
    /// Com filtro: cor que o filtro reprova (= vira fundo), nunca preto/branco
    /// fixo de alto contraste — calculada contra os grupos ativos.
    /// </summary>
    internal static void FillFailingColor(byte[] bgra, int w,
        int x1, int y1, int x2, int y2, FilterMode mode,
        IReadOnlyList<(int R, int G, int B, int S1, int S2, int V1, int V2)> groups,
        int threshold)
    {
        var (r, g, b) = FailingColor(mode, groups, threshold);
        for (int y = y1; y < y2; y++)
            for (int x = x1; x < x2; x++)
            {
                int o = (y * w + x) * 4;
                bgra[o] = b; bgra[o + 1] = g; bgra[o + 2] = r; bgra[o + 3] = 255;
            }
    }

    internal static (byte R, byte G, byte B) FailingColor(FilterMode mode,
        IReadOnlyList<(int R, int G, int B, int S1, int S2, int V1, int V2)> groups,
        int threshold)
    {
        switch (mode)
        {
            case FilterMode.Threshold:
                // passa se cinza < limiar → cinza == limiar sempre reprova.
                int v = Math.Clamp(threshold, 0, 255);
                return ((byte)v, (byte)v, (byte)v);
            case FilterMode.Rgb:
                // Varre candidatos até achar um que não casa exatamente.
                byte[] cands = [0, 255, 1, 128, 64, 192];
                foreach (byte c in cands)
                {
                    var cand = new[] { (c, c, c), (c, (byte)(255 - c), c) };
                    foreach (var (cr, cg, cb) in cand)
                    {
                        bool pass = false;
                        foreach (var grp in groups)
                            if (cr == grp.R && cg == grp.G && cb == grp.B) { pass = true; break; }
                        if (!pass) return (cr, cg, cb);
                    }
                }
                return (123, 45, 67);
            default: // Hsv
                for (int s = 0; s <= 100; s += 5)
                    for (int vv = 0; vv <= 100; vv += 5)
                    {
                        bool covered = false;
                        foreach (var grp in groups)
                            if (s >= grp.S1 && s <= grp.S2 && vv >= grp.V1 && vv <= grp.V2)
                            { covered = true; break; }
                        if (!covered)
                        {
                            // Cinza tem S=0: serve quando s==0; senão vermelho com S,V alvo.
                            if (s == 0)
                            {
                                byte gv = (byte)(vv * 255 / 100);
                                return (gv, gv, gv);
                            }
                            return HsvToRgb(0, s * 255 / 100, vv * 255 / 100);
                        }
                    }
                return (255, 255, 255);   // filtro passa tudo: exclusão inócua
        }
    }

    internal static (byte R, byte G, byte B) HsvToRgb(double h, int s, int v)
    {
        double ss = s / 255.0, vv = v;
        double c = vv * ss, x = c * (1 - Math.Abs((h / 60 % 2) - 1)), m = vv - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return ((byte)(r + m), (byte)(g + m), (byte)(b + m));
    }

    // ---- RF-111/112: erosão 3×3, 1 iteração ----

    /// <summary>
    /// Afina traços: pixel de texto (0) sobrevive só se os 8 vizinhos também
    /// forem texto. Bordas tratadas como fundo. Remove pontos isolados.
    /// </summary>
    internal static byte[] Erode3x3(byte[] bin, int w, int h)
    {
        var out8 = new byte[bin.Length];
        for (int i = 0; i < out8.Length; i++) out8[i] = 255;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                bool all = true;
                for (int dy = -1; dy <= 1 && all; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (bin[(y + dy) * w + x + dx] != 0) { all = false; break; }
                if (all) out8[y * w + x] = 0;
            }
        return out8;
    }

    // ---- ampliação bilinear ----

    internal static byte[] ResizeGray(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh];
        double fx = sw / (double)dw, fy = sh / (double)dh;
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                double sx = (x + 0.5) * fx - 0.5, sy = (y + 0.5) * fy - 0.5;
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                double tx = sx - x0, ty = sy - y0;
                double acc = 0;
                for (int j = 0; j <= 1; j++)
                    for (int i = 0; i <= 1; i++)
                    {
                        int px = Math.Clamp(x0 + i, 0, sw - 1), py = Math.Clamp(y0 + j, 0, sh - 1);
                        double wgt = ((i == 0 ? 1 - tx : tx)) * ((j == 0 ? 1 - ty : ty));
                        acc += src[py * sw + px] * wgt;
                    }
                dst[y * dw + x] = (byte)Math.Round(acc);
            }
        return dst;
    }

    internal static byte[] ResizeBgra(byte[] src, int sw, int sh, int dw, int dh)
    {
        var dst = new byte[dw * dh * 4];
        double fx = sw / (double)dw, fy = sh / (double)dh;
        for (int y = 0; y < dh; y++)
            for (int x = 0; x < dw; x++)
            {
                double sx = (x + 0.5) * fx - 0.5, sy = (y + 0.5) * fy - 0.5;
                int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                double tx = sx - x0, ty = sy - y0;
                for (int c = 0; c < 4; c++)
                {
                    double acc = 0;
                    for (int j = 0; j <= 1; j++)
                        for (int i = 0; i <= 1; i++)
                        {
                            int px = Math.Clamp(x0 + i, 0, sw - 1), py = Math.Clamp(y0 + j, 0, sh - 1);
                            double wgt = ((i == 0 ? 1 - tx : tx)) * ((j == 0 ? 1 - ty : ty));
                            acc += src[(py * sw + px) * 4 + c] * wgt;
                        }
                    dst[(y * dw + x) * 4 + c] = (byte)Math.Round(acc);
                }
            }
        return dst;
    }

    // ---- RF-116: volta para coordenadas de tela ----

    /// <summary>
    /// Caixa no espaço ampliado → tela: divide pelo zoom, piso no canto
    /// superior/esquerdo, teto no inferior/direito, soma a origem da área.
    /// </summary>
    public static (int X, int Y, int W, int H) Unscale(
        int x0, int y0, int x1, int y1, double zoom, int ox, int oy)
    {
        double z = zoom <= 0 ? 1.0 : zoom;   // zoom inválido não gera coordenada absurda
        return (ox + (int)Math.Floor(x0 / z), oy + (int)Math.Floor(y0 / z),
         (int)Math.Ceiling(x1 / z) - (int)Math.Floor(x0 / z),
         (int)Math.Ceiling(y1 / z) - (int)Math.Floor(y0 / z));
    }

    // ---- RF-114/115: passos e padrão do zoom ----
    public static double ClampZoomSteps(double v)
    {
        v = Math.Round(v * 2) / 2;                       // passos P-25
        v = Math.Round(v, 1);                            // uma casa decimal
        return Math.Clamp(v, Core.Params.P23_ZoomMin, Core.Params.P24_ZoomMax);
    }

    public static double DefaultZoom => Core.Params.P22_ZoomDefault;   // RF-115 🔒

    // ---- RF-117: conversão de canais para cada motor ----

    /// <summary>
    /// 1→3 replica; 4→3 descarta alfa; →1 via luminância; demais diretos.
    /// 3 canais = BGR, 4 = BGRA, 1 = cinza.
    /// </summary>
    public static byte[] ConvertChannels(byte[] src, int w, int h, int fromCh, int toCh)
    {
        if (fromCh == toCh) return (byte[])src.Clone();
        var dst = new byte[w * h * toCh];
        for (int i = 0; i < w * h; i++)
        {
            byte b, g, r;
            if (fromCh == 1) { b = g = r = src[i]; }
            else { b = src[i * fromCh]; g = src[i * fromCh + 1]; r = src[i * fromCh + 2]; }
            if (toCh == 1) dst[i] = (byte)ColorFilter.Gray(r, g, b);
            else { dst[i * toCh] = b; dst[i * toCh + 1] = g; dst[i * toCh + 2] = r; }
        }
        return dst;
    }

    // ---- RF-119: assistente de configuração rápida 🔒 ----

    /// <summary>
    /// Texto escuro → dois grupos (P-26, P-27); texto claro → um (P-28).
    /// </summary>
    public static List<ColorGroup> QuickGroups(bool darkText)
    {
        if (darkText)
            return new List<ColorGroup>
            {
                new() { S1 = Core.Params.P26_DarkS1a, S2 = Core.Params.P26_DarkS1b,
                        V1 = Core.Params.P26_DarkV1a, V2 = Core.Params.P26_DarkV1b },
                new() { S1 = Core.Params.P27_DarkS2a, S2 = Core.Params.P27_DarkS2b,
                        V1 = Core.Params.P27_DarkV2a, V2 = Core.Params.P27_DarkV2b },
            };
        return new List<ColorGroup>
        {
            new() { S1 = Core.Params.P28_LightS1a, S2 = Core.Params.P28_LightS1b,
                    V1 = Core.Params.P28_LightV1a, V2 = Core.Params.P28_LightV1b },
        };
    }
}
