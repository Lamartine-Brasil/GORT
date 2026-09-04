using System;
using System.Collections.Generic;
using System.IO;
using Gort.Imaging;
using SkiaSharp;

namespace Gort.Platform.Cli;

/// <summary>
/// Captura por ferramenta externa (Linux/macOS): captura a TELA CHEIA em
/// arquivo temporário e recorta a área em código gerenciado. Estratégia
/// deliberada: evita as diferenças de sistema de coordenadas dos argumentos
/// de recorte de cada ferramenta (-R/-g/-crop usam origens e escalas
/// distintas por SO/compositor) e centraliza o mapeamento pixels-físicos
/// num só lugar, com detecção automática de escala (Retina/HiDPI).
/// Contrato igual ao Windows (6.2): fora da tela → nulo, sem erro.
/// </summary>
public abstract class CliCapture : IScreenCapture
{
    /// <summary>Geometria vinda da UI (Avalonia Screens, pixels físicos).</summary>
    protected IReadOnlyList<MonitorInfo> Geometry() =>
        PlatformFactory.DesktopGeometry?.Invoke() ?? [];

    public virtual ScreenRect VirtualScreen
    {
        get
        {
            int x1 = int.MaxValue, y1 = int.MaxValue;
            int x2 = int.MinValue, y2 = int.MinValue;
            foreach (var m in Geometry())
            {
                x1 = Math.Min(x1, m.Bounds.X); y1 = Math.Min(y1, m.Bounds.Y);
                x2 = Math.Max(x2, m.Bounds.X + m.Bounds.W);
                y2 = Math.Max(y2, m.Bounds.Y + m.Bounds.H);
            }
            if (x2 <= x1 || y2 <= y1) return new ScreenRect(0, 0, 0, 0);
            return new ScreenRect(x1, y1, x2 - x1, y2 - y1);
        }
    }

    public virtual IReadOnlyList<MonitorInfo> GetMonitors() => Geometry();

    /// <summary>Captura a tela cheia em PNG temporário. Nulo se falhar.</summary>
    protected abstract string? CaptureFullToFile(string destPng);

    public RegionImage? CaptureRect(int index, ScreenRect rect, bool needOriginal)
    {
        var virt = VirtualScreen;
        if (virt.IsEmpty) return null;
        var clip = Intersect(rect, virt);
        if (clip.IsEmpty) return null;   // Parte VIII: fora da tela → pula sem erro

        string tmp = Path.Combine(Path.GetTempPath(),
            "gort-cap-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            if (CaptureFullToFile(tmp) is null || !File.Exists(tmp)) return null;
            using var bmp = SKBitmap.Decode(tmp);
            if (bmp is null || bmp.Width <= 0 || bmp.Height <= 0) return null;
            return Crop(bmp, index, clip, virt, needOriginal);
        }
        catch { return null; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    /// <summary>
    /// Recorta com detecção de escala: a imagem pode estar em pixels físicos
    /// (2× no Retina) enquanto o retângulo está no espaço virtual. O fator é
    /// a razão imagem/virtual arredondada (1–3); fora disso, usa 1 e intersecta.
    /// </summary>
    internal static RegionImage? Crop(SKBitmap full, int index, ScreenRect clip,
        ScreenRect virt, bool needOriginal)
    {
        try
        {
            using var bgra = full.Copy(SKColorType.Bgra8888);
            if (bgra is null) return null;
            int fw = bgra.Width, fh = bgra.Height;
            double fx = virt.W > 0 ? (double)fw / virt.W : 1.0;
            double fy = virt.H > 0 ? (double)fh / virt.H : 1.0;
            int scale = (int)Math.Round((fx + fy) / 2.0);
            if (scale < 1 || scale > 3) scale = 1;

            int sx = (clip.X - virt.X) * scale;
            int sy = (clip.Y - virt.Y) * scale;
            int sw = clip.W * scale, sh = clip.H * scale;
            // Intersecta com a imagem real.
            int x1 = Math.Max(0, sx), y1 = Math.Max(0, sy);
            int x2 = Math.Min(fw, sx + sw), y2 = Math.Min(fh, sy + sh);
            int w = x2 - x1, h = y2 - y1;
            if (w <= 0 || h <= 0) return null;

            nint pixels = bgra.GetPixels();
            if (pixels == nint.Zero) return null;
            int stride = bgra.RowBytes;
            var bytes = new byte[clip.W * clip.H * 4];
            // Mapeia cada pixel de saída (downscale por vizinho mais próximo).
            unsafe
            {
                byte* src = (byte*)pixels.ToPointer();
                fixed (byte* dst = bytes)
                {
                    for (int y = 0; y < clip.H; y++)
                    {
                        int srcY = Math.Min(fh - 1, y1 + y * h / clip.H);
                        for (int x = 0; x < clip.W; x++)
                        {
                            int srcX = Math.Min(fw - 1, x1 + x * w / clip.W);
                            byte* s = src + srcY * stride + srcX * 4;
                            byte* d = dst + (y * clip.W + x) * 4;
                            d[0] = s[0]; d[1] = s[1]; d[2] = s[2]; d[3] = 255;  // P-107: opaco
                        }
                    }
                }
            }
            return new RegionImage
            {
                Index = index,
                Width = clip.W, Height = clip.H, Channels = 4, Bytes = bytes,
                OrigWidth = needOriginal ? clip.W : 0,
                OrigHeight = needOriginal ? clip.H : 0,
                OrigChannels = needOriginal ? 4 : 0,
                OrigBytes = needOriginal ? (byte[])bytes.Clone() : null,
            };
        }
        catch { return null; }
    }

    internal static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.W, b.X + b.W);
        int y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        return new ScreenRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }
}
