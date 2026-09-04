using System;

namespace Gort.Regions;

/// <summary>
/// Geometria moldura↔captura (RF-073..RF-077). Molduras vivem em DIPs
/// (unidade da janela Avalonia); a captura é em pixels físicos:
/// físico = DIP × escala do monitor que contém a moldura (RF-075/076).
/// </summary>
public static class FrameGeometry
{
    /// <summary>Borda total = P-14+P-15 e barra = P-16, em pixels físicos (RF-074).</summary>
    public static (int Border, int Title) Chrome(double scale) =>
        (Math.Max(1, (int)Math.Round((Core.Params.P14_BorderInner + Core.Params.P15_BorderOuter) * scale)),
         Math.Max(1, (int)Math.Round(Core.Params.P16_TitleBarH * scale)));

    /// <summary>
    /// Moldura → captura: origem física (x+borda, y+barra), tamanho a partir do
    /// tamanho DIP (×escala) menos o cromo: (w−2b, h−barra−b), mínimo 1 px (RF-073).
    /// </summary>
    public static Platform.ScreenRect FrameToCapture(int physX, int physY,
        double dipW, double dipH, double scale)
    {
        var (b, t) = Chrome(scale);
        int fw = (int)Math.Round(dipW * scale), fh = (int)Math.Round(dipH * scale);
        return new Platform.ScreenRect(
            physX + b, physY + t,
            Math.Max(1, fw - 2 * b),
            Math.Max(1, fh - t - b));
    }

    /// <summary>
    /// Captura (físico) → posição física + tamanho DIP da moldura (RF-066).
    /// </summary>
    public static (int X, int Y, double W, double H) CaptureToFrame(Platform.ScreenRect cap, double scale)
    {
        var (b, t) = Chrome(scale);
        return (cap.X - b, cap.Y - t,
                (cap.W + 2 * b) / scale, (cap.H + t + b) / scale);
    }

    /// <summary>Largura arredondada para cima ao múltiplo de 4 (RF-077, P-144 🔒).</summary>
    public static int AlignWidth(int w) => (w + 3) / 4 * 4;

    public static bool IsAccidentalClick(int w, int h) =>
        w <= Core.Params.P145_MinSelectSize || h <= Core.Params.P145_MinSelectSize;  // RF-052

    public static (double W, double H) ClampMin(double w, double h) =>
        (Math.Max(Core.Params.P12_MinFrameSize, w),
         Math.Max(Core.Params.P12_MinFrameSize, h));  // RF-057

    /// <summary>Zona sensível de borda em DIP: P-11 = 31 (RF-056).</summary>
    public const double EdgeZoneDip = 31;
}
