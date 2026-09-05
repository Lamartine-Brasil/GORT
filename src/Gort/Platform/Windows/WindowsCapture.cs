using System;
using System.Collections.Generic;
using Gort.Imaging;

namespace Gort.Platform.Windows;

/// <summary>
/// C1 — captura de tela via GDI BitBlt (RF-088 fonte 1, RF-100).
/// Pixels físicos globais, incluindo origens negativas. Sem CAPTUREBLT os
/// overlays em camadas do próprio programa ficam excluídos (C1).
/// Retângulo sem interseção com a área virtual → nulo, sem erro (6.2).
/// </summary>
public sealed class WindowsCapture : IScreenCapture
{
    /// <summary>
    /// Área virtual recalculada a cada chamada (troca de monitor/resolução
    /// com o app aberto usava geometria obsoleta até reiniciar).
    /// </summary>
    public ScreenRect VirtualScreen => new(
        Win32.GetSystemMetrics(Win32.SM_XVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_YVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CXVIRTUALSCREEN),
        Win32.GetSystemMetrics(Win32.SM_CYVIRTUALSCREEN));

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        // Raiz do delegate durante a chamada (ver WindowsWindowService).
        Win32.MonitorEnumProc proc = (nint _, nint _, ref Win32.RECT r, nint _) =>
        {
            list.Add(new MonitorInfo(
                new ScreenRect(r.left, r.top, r.right - r.left, r.bottom - r.top),
                1.0));   // Etapa 3+: escala real por monitor (RF-075) via Avalonia
            return true;
        };
        try
        {
            Win32.EnumDisplayMonitors(nint.Zero, nint.Zero, proc, nint.Zero);
        }
        finally { GC.KeepAlive(proc); }
        return list;
    }

    public RegionImage? CaptureRect(int index, ScreenRect rect, bool needOriginal)
    {
        var clip = Intersect(rect, VirtualScreen);
        if (clip.IsEmpty) return null;   // Parte VIII: fora da tela → pula sem erro

        nint screenDc = Win32.GetDC(nint.Zero);
        if (screenDc == nint.Zero) return null;
        try
        {
            nint memDc = Win32.CreateCompatibleDC(screenDc);
            if (memDc == nint.Zero) return null;
            try
            {
                nint bmp = Win32.CreateCompatibleBitmap(screenDc, clip.W, clip.H);
                if (bmp == nint.Zero) return null;
                try
                {
                    nint old = Win32.SelectObject(memDc, bmp);
                    try
                    {
                        if (!Win32.BitBlt(memDc, 0, 0, clip.W, clip.H,
                                screenDc, clip.X, clip.Y, Win32.SRCCOPY))
                            return null;

                        var bytes = new byte[clip.W * clip.H * 4];
                        var info = new Win32.BITMAPINFO
                        {
                            bmiHeader =
                            {
                                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
                                biWidth = clip.W,
                                biHeight = -clip.H,   // top-down
                                biPlanes = 1,
                                biBitCount = 32,
                                biCompression = Win32.BI_RGB,
                            },
                        };
                        int lines = Win32.GetDIBits(memDc, bmp, 0, (uint)clip.H,
                            bytes, ref info, Win32.DIB_RGB_COLORS);
                        if (lines == 0) return null;

                        // GetDIBits devolve BGRA — canal alfa do BitBlt de tela é 0;
                        // marca opaco para o pipeline não descartar (P-107).
                        for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;

                        return new RegionImage
                        {
                            Index = index,
                            Width = clip.W,
                            Height = clip.H,
                            Channels = 4,
                            Bytes = bytes,
                            OrigWidth = needOriginal ? clip.W : 0,
                            OrigHeight = needOriginal ? clip.H : 0,
                            OrigBytes = needOriginal ? (byte[])bytes.Clone() : null,
                        };
                    }
                    finally { Win32.SelectObject(memDc, old); }
                }
                finally { Win32.DeleteObject(bmp); }   // RF-555: liberar determinístico
            }
            finally { Win32.DeleteDC(memDc); }
        }
        finally { Win32.ReleaseDC(nint.Zero, screenDc); }
    }

    public bool SupportsClientArea => true;

    /// <summary>
    /// RF-088 fonte 2 — janela ativa: captura o cliente cheio da janela em
    /// primeiro plano e recorta a área (dada em tela) relativa a ele.
    /// </summary>
    public RegionImage? CaptureClientArea(int index, ScreenRect areaScreen, bool needOriginal)
    {
        var fg = Win32.GetForegroundWindow();
        if (fg == nint.Zero) return null;
        var pt = new Win32.POINT { x = 0, y = 0 };
        if (!Win32.ClientToScreen(fg, ref pt)) return null;
        if (!Win32.GetClientRect(fg, out var rc)) return null;
        int cw = rc.right - rc.left, ch = rc.bottom - rc.top;
        if (cw <= 0 || ch <= 0) return null;
        var full = CaptureRect(-1,
            new ScreenRect(pt.x, pt.y, cw, ch), needOriginal);
        if (full is null) return null;
        // full pode ser menor que o cliente (clip na tela): origem e
        // stride são os do recorte, não os do cliente cheio.
        int fx = System.Math.Max(pt.x, VirtualScreen.X);
        int fy = System.Math.Max(pt.y, VirtualScreen.Y);
        int x1 = System.Math.Max(areaScreen.X, fx);
        int y1 = System.Math.Max(areaScreen.Y, fy);
        int x2 = System.Math.Min(areaScreen.X + areaScreen.W, fx + full.Width);
        int y2 = System.Math.Min(areaScreen.Y + areaScreen.H, fy + full.Height);
        int w = x2 - x1, h = y2 - y1;
        if (w <= 0 || h <= 0) return null;
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            System.Buffer.BlockCopy(full.Bytes, ((y1 - fy + y) * full.Width + (x1 - fx)) * 4,
                bytes, y * w * 4, w * 4);
        return new RegionImage
        {
            Index = index, Width = w, Height = h, Channels = 4, Bytes = bytes,
            OrigWidth = needOriginal ? w : 0, OrigHeight = needOriginal ? h : 0,
            OrigBytes = needOriginal ? (byte[])bytes.Clone() : null,
        };
    }

    internal static ScreenRect Intersect(ScreenRect a, ScreenRect b)
    {
        int x1 = System.Math.Max(a.X, b.X), y1 = System.Math.Max(a.Y, b.Y);
        int x2 = System.Math.Min(a.X + a.W, b.X + b.W);
        int y2 = System.Math.Min(a.Y + a.H, b.Y + b.H);
        return new ScreenRect(x1, y1, System.Math.Max(0, x2 - x1), System.Math.Max(0, y2 - y1));
    }
}
