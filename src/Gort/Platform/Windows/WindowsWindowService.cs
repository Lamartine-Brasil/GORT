using System;
using System.Collections.Generic;

namespace Gort.Platform.Windows;

/// <summary>
/// C3/C4/C12 — janelas no Windows. C2 (fluxo de janela coberta) chega na Etapa 16;
/// até lá <see cref="AttachedAvailable"/> é falso e a UI não oferece o modo (RF-576).
/// </summary>
public sealed class WindowsWindowService : IWindowService
{
    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public bool AttachedAvailable => false;   // Etapa 16 (RF-089..RF-097)

    public IReadOnlyList<WindowRef> ListCapturableWindows()
    {
        var list = new List<WindowRef>();
        // O delegate precisa sobreviver à chamada nativa: sem a raiz o GC
        // pode coletá-lo no meio da enumeração (crash intermitente nativo).
        Win32.EnumWindowsProc proc = (hWnd, _) =>
        {
            if (!Win32.IsWindowVisible(hWnd)) return true;
            var buf = new char[256];
            int n = Win32.GetWindowTextW(hWnd, buf, buf.Length);
            if (n == 0) return true;
            list.Add(new WindowRef(hWnd, new string(buf, 0, n)));
            return true;
        };
        try { Win32.EnumWindows(proc, nint.Zero); }
        finally { GC.KeepAlive(proc); }
        return list;
    }

    public WindowRef? ForegroundWindow()
    {
        var h = Win32.GetForegroundWindow();
        if (h == nint.Zero) return null;
        var buf = new char[256];
        int n = Win32.GetWindowTextW(h, buf, buf.Length);
        return new WindowRef(h, n > 0 ? new string(buf, 0, n) : "");
    }

    /// <summary>
    /// RF-092: limites estendidos do quadro (sem sombras), com queda para o
    /// retângulo simples — aqui aproximado pelo cliente; refino na Etapa 16.
    /// </summary>
    public ScreenRect FrameBounds(WindowRef window)
    {
        if (Win32.DwmGetWindowAttribute(window.Handle,
                Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var r,
                System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>()) == 0)
            return new ScreenRect(r.left, r.top, r.right - r.left, r.bottom - r.top);
        var o = ClientOrigin(window);
        return new ScreenRect(o.X, o.Y, 0, 0);
    }

    public ScreenRect ClientOrigin(WindowRef window)
    {
        var pt = new Win32.POINT { x = 0, y = 0 };
        return Win32.ClientToScreen(window.Handle, ref pt)
            ? new ScreenRect(pt.x, pt.y, 0, 0)
            : new ScreenRect(0, 0, 0, 0);
    }
}
