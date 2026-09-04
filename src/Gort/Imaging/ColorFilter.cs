namespace Gort.Imaging;

using System;
using System.Collections.Generic;

/// <summary>Modos de filtro mutuamente exclusivos (RF-104).</summary>
public enum FilterMode { None, Rgb, Hsv, Threshold }

/// <summary>
/// Critério de filtro pixel a pixel (RF-105..RF-109). Núcleo puro compartilhado
/// pelo conta-gotas (RF-081: pré-visualização idêntica ao OCR) e pelo
/// pré-processamento (Etapa 4). Sem dependência de UI.
/// </summary>
public static class ColorFilter
{
    /// <summary>
    /// RGB→HSV (RF-107): V = max; S = (max−min)*255/max (0 se max=0); H em
    /// setores de 60°, normalizada 0–360. S e V devolvidos em 0–255.
    /// </summary>
    public static (double H, int S, int V) RgbToHsv(byte r, byte g, byte b)
    {
        int max = System.Math.Max(r, System.Math.Max(g, b));
        int min = System.Math.Min(r, System.Math.Min(g, b));
        int delta = max - min;
        int s = max == 0 ? 0 : delta * 255 / max;
        double h;
        if (delta == 0) h = 0;
        else if (max == r) h = 60.0 * (((g - b) / (double)delta) % 6);
        else if (max == g) h = 60.0 * ((b - r) / (double)delta + 2);
        else h = 60.0 * ((r - g) / (double)delta + 4);
        if (h < 0) h += 360;
        return (h, s, max);
    }

    /// <summary>Cinza pela matriz de luminância (RF-108, P-146): 0,30/0,59/0,11.</summary>
    public static int Gray(byte r, byte g, byte b) =>
        (int)(0.30 * r + 0.59 * g + 0.11 * b);

    /// <summary>
    /// Um pixel passa se satisfizer QUALQUER grupo ativo (RF-105 RGB exato,
    /// RF-106 faixas S/V em 0–100).
    /// </summary>
    public static bool Passes(byte r, byte g, byte b, FilterMode mode,
        IReadOnlyList<(int R, int G, int B, int S1, int S2, int V1, int V2)> groups,
        int threshold)
    {
        switch (mode)
        {
            case FilterMode.Rgb:
                foreach (var grp in groups)
                    if (r == grp.R && g == grp.G && b == grp.B) return true;
                return false;
            case FilterMode.Hsv:
                {
                    var (_, s, v) = RgbToHsv(r, g, b);
                    int s100 = s * 100 / 255, v100 = v * 100 / 255;
                    foreach (var grp in groups)
                        if (s100 >= grp.S1 && s100 <= grp.S2
                            && v100 >= grp.V1 && v100 <= grp.V2) return true;
                    return false;
                }
            case FilterMode.Threshold:
                return Gray(r, g, b) < threshold;   // RF-108
            default:
                return true;
        }
    }

    /// <summary>
    /// RF-570: quadro (quase) todo preto — tela cheia exclusiva ou janela
    /// minimizada devolve preto; sugere modo janela em vez de vazio repetido.
    /// </summary>
    public static bool IsBlack(byte[] bgra, int channels)
    {
        int ch = Math.Max(channels, 1);
        int pixels = Math.Max(1, bgra.Length / ch);
        int step = Math.Max(1, pixels / 4096);   // amostra esparsa limitada
        for (int p = 0; p < pixels; p += step)
        {
            int i = p * ch;
            byte r, g, b;
            if (ch == 1) r = g = b = bgra[i];
            else { b = bgra[i]; g = bgra[i + 1]; r = bgra[i + 2]; }
            if (r >= 16 || g >= 16 || b >= 16) return false;
        }
        return true;
    }

    /// <summary>
    /// Binariza BGRA: quem passa → 0 (preto), quem não passa → 255 (branco)
    /// (RF-082, RF-109). Sem filtro → imagem não binarizada (RF-110).
    /// </summary>
    public static byte[] Binarize(byte[] bgra, int w, int h, FilterMode mode,
        IReadOnlyList<(int R, int G, int B, int S1, int S2, int V1, int V2)> groups,
        int threshold)
    {
        var out8 = new byte[w * h];
        for (int i = 0, p = 0; i < out8.Length; i++, p += 4)
        {
            byte b = bgra[p], g = bgra[p + 1], r = bgra[p + 2];
            out8[i] = mode == FilterMode.None
                ? (byte)Gray(r, g, b)
                : (Passes(r, g, b, mode, groups, threshold) ? (byte)0 : (byte)255);
        }
        return out8;
    }
}
