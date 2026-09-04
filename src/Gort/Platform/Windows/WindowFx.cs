using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Gort.Platform.Windows;

/// <summary>Efeitos de janela do Windows (C7/C8/C9).</summary>
public static class WindowFx
{
    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x20;
    public const uint WDA_NONE = 0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [DllImport("user32.dll")] private static extern uint GetWindowLong(nint h, int i);
    [DllImport("user32.dll")] private static extern uint SetWindowLong(nint h, int i, uint v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint h, nint after,
        int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] private static extern uint SetWindowDisplayAffinity(nint h, uint a);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();

    private const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_FRAMECHANGED = 0x20;

    public static nint? HandleOf(Window w)
    {
        try { return w.TryGetPlatformHandle()?.Handle; }
        catch { return null; }
    }

    /// <summary>C7: cliques atravessam a janela (alternável em execução).</summary>
    public static void SetClickThrough(Window w, bool through)
    {
        var h = HandleOf(w);
        if (h is null) return;
        try
        {
            uint ex = GetWindowLong(h.Value, GWL_EXSTYLE);
            ex = through ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT;
            SetWindowLong(h.Value, GWL_EXSTYLE, ex);
            SetWindowPos(h.Value, nint.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        }
        catch { }
    }

    /// <summary>C8: exclui de capturas/gravações (alternável em execução).</summary>
    public static void SetCaptureExclusion(Window w, bool exclude)
    {
        var h = HandleOf(w);
        if (h is null) return;
        try { SetWindowDisplayAffinity(h.Value, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); }
        catch { }
    }

    /// <summary>C9: sincroniza com o compositor (DwmFlush).</summary>
    public static void SyncCompositor()
    {
        try { DwmFlush(); } catch { }
    }
}
