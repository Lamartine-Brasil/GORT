using System.Runtime.InteropServices;

namespace Gort.Platform.Windows;

/// <summary>P/Invoke Win32 para captura GDI + informações de janela (Etapa 2).</summary>
internal static class Win32
{
    public const int SRCCOPY = 0x00CC0020;   // sem CAPTUREBLT: janelas em camadas
                                             // (nossos overlays) ficam fora (C1)
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const int DIB_RGB_COLORS = 0;
    public const int BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int x, y; }

    internal delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint data);
    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")] internal static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hWnd, nint hDC);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint hdc);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] internal static extern bool BitBlt(nint hdc, int x, int y, int w, int h,
        nint hdcSrc, int x1, int y1, int rop);
    [DllImport("gdi32.dll")] internal static extern int GetDIBits(nint hdc, nint hbm, uint start,
        uint lines, [Out] byte[] bits, ref BITMAPINFO info, uint usage);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint hdc,
        nint clip, MonitorEnumProc proc, nint data);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] internal static extern bool PrintWindow(
        nint hWnd, nint hdc, uint flags);
    internal const uint PW_CLIENTONLY = 1;
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT pt);    [DllImport("user32.dll")] internal static extern int GetWindowTextW(nint hWnd,
        [Out] char[] text, int max);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hWnd);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hWnd, ref POINT pt);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hWnd, out RECT rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hWnd,
        int attr, out RECT rect, int size);
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
}
