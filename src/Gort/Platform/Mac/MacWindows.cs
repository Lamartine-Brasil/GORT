using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gort.Platform.Mac;

/// <summary>
/// C3/C12 no macOS via CGWindowListCopyWindowInfo (CoreGraphics).
/// Devolve (título, limites) das janelas visíveis em ordem frente→fundo;
/// a primeira com área útil é a frontal (aproxima a janela ativa).
/// Coordenadas Quartz (origem inferior-esquerda, pontos) são convertidas
/// para origem superior-esquerda; a escala física é resolvida pelo recorte
/// (detecção automática em CliCapture.Crop). Qualquer falha → indisponível,
/// nunca exceção para o laço.
/// </summary>
public sealed class MacWindows : IWindowService
{
    public bool IsAvailable => IsSupported;
    public string? UnavailableReason => IsSupported ? null
        : "Lista de janelas indisponível (CoreGraphics inacessível). Use áreas fixas.";

    public static bool IsSupported
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return false;
            try { return CGWindowListCopyWindowInfo(0, 0) != nint.Zero; }
            catch { return false; }
        }
    }

    public IReadOnlyList<WindowRef> ListCapturableWindows()
    {
        var list = new List<WindowRef>();
        try
        {
            nint arr = CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly, kCGNullWindowID);
            if (arr == nint.Zero) return list;
            try
            {
                nuint n = CFArrayGetCount(arr);
                for (nuint i = 0; i < n && list.Count < 200; i++)
                {
                    nint dict = CFArrayGetValueAtIndex(arr, i);
                    if (dict == nint.Zero) continue;
                    string name = CFDictString(dict, "kCGWindowName");
                    string owner = CFDictString(dict, "kCGWindowOwnerName");
                    if (name.Length == 0 && owner.Length == 0) continue;
                    if (!TryDictRect(dict, out var r)) continue;
                    if (r.W < 50 || r.H < 50) continue;   // ícones/sombras
                    string title = name.Length > 0 ? $"{owner} — {name}" : owner;
                    list.Add(new WindowRef((nint)(1000 + list.Count), title));
                }
            }
            finally { CFRelease(arr); }
        }
        catch { }
        return list;
    }

    public WindowRef? ForegroundWindow()
    {
        var all = ListCapturableWindows();
        return all.Count > 0 ? all[0] : null;
    }

    public ScreenRect FrameBounds(WindowRef window) =>
        TryGetFrontmostRect(out var r) ? r : new ScreenRect(0, 0, 0, 0);

    public ScreenRect ClientOrigin(WindowRef window) =>
        TryGetFrontmostRect(out var r) ? new ScreenRect(r.X, r.Y, 0, 0) : new ScreenRect(0, 0, 0, 0);

    /// <summary>Limites da janela frontal em pixels de tela (origem superior-esquerda).</summary>
    internal static bool TryGetFrontmostRect(out ScreenRect rect)
    {
        rect = new ScreenRect(0, 0, 0, 0);
        try
        {
            nint arr = CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly, kCGNullWindowID);
            if (arr == nint.Zero) return false;
            try
            {
                nuint n = CFArrayGetCount(arr);
                for (nuint i = 0; i < n; i++)
                {
                    nint dict = CFArrayGetValueAtIndex(arr, i);
                    if (dict == nint.Zero) continue;
                    if (!TryDictRect(dict, out var r)) continue;
                    if (r.W < 50 || r.H < 50) continue;
                    rect = r;
                    return true;
                }
            }
            finally { CFRelease(arr); }
        }
        catch { }
        return false;
    }

    private static bool TryDictRect(nint dict, out ScreenRect rect)
    {
        rect = new ScreenRect(0, 0, 0, 0);
        try
        {
            nint bounds = CFDictValue(dict, "kCGWindowBounds");
            if (bounds == nint.Zero || CFDictionaryGetCount(bounds) == 0) return false;
            double x = CFDictDouble(bounds, "X"), y = CFDictDouble(bounds, "Y");
            double w = CFDictDouble(bounds, "Width"), h = CFDictDouble(bounds, "Height");
            if (w <= 0 || h <= 0) return false;
            // Quartz: y cresce para cima a partir da base do arranjo.
            double totalH = MainDisplayHeight();
            int ix = (int)Math.Round(x), iw = (int)Math.Round(w), ih = (int)Math.Round(h);
            int iy = (int)Math.Round(totalH - y - h);
            rect = new ScreenRect(ix, iy, iw, ih);
            return true;
        }
        catch { return false; }
    }

    private static double _mainH;
    private static double MainDisplayHeight()
    {
        if (_mainH > 0) return _mainH;
        try
        {
            var b = CGDisplayBounds(CGMainDisplayID());
            if (b.Height > 0) _mainH = b.Height;
        }
        catch { }
        if (_mainH <= 0) _mainH = 1080;
        return _mainH;
    }

    // ---- CoreGraphics / CoreFoundation (mínimo necessário) ----

    private const uint kCGWindowListOptionOnScreenOnly = 1;
    private const uint kCGNullWindowID = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern uint CGMainDisplayID();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGRect CGDisplayBounds(uint display);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFArrayGetCount(nint array);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFArrayGetValueAtIndex(nint array, nuint idx);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nuint CFDictionaryGetCount(nint dict);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFDictionaryGetValue(nint dict, nint key);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint obj);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringCreateWithCString(nint alloc, string str, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFStringGetCStringPtr(nint str, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern long CFStringGetLength(nint str);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern bool CFStringGetCString(nint str, byte[] buffer, long size, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern int CFGetTypeID(nint obj);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern int CFStringGetTypeID();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern int CFNumberGetTypeID();

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern bool CFNumberGetValue(nint num, int type, out double value);

    private const uint kCFStringEncodingUTF8 = 0x08000100;

    private static nint Key(string s)
    {
        nint k = CFStringCreateWithCString(nint.Zero, s, kCFStringEncodingUTF8);
        return k;   // liberado pelo chamador via CFRelease após uso imediato
    }

    private static nint CFDictValue(nint dict, string key)
    {
        nint k = Key(key);
        try { return CFDictionaryGetValue(dict, k); }
        finally { try { CFRelease(k); } catch { } }
    }

    private static string CFDictString(nint dict, string key)
    {
        try
        {
            nint v = CFDictValue(dict, key);
            if (v == nint.Zero || CFGetTypeID(v) != CFStringGetTypeID()) return "";
            nint p = CFStringGetCStringPtr(v, kCFStringEncodingUTF8);
            if (p != nint.Zero) return Marshal.PtrToStringUTF8(p) ?? "";
            long len = CFStringGetLength(v);
            if (len <= 0 || len > 4096) return "";
            var buf = new byte[(len + 1) * 4];
            if (!CFStringGetCString(v, buf, buf.Length, kCFStringEncodingUTF8)) return "";
            return System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
        }
        catch { return ""; }
    }

    private static double CFDictDouble(nint dict, string key)
    {
        try
        {
            nint v = CFDictValue(dict, key);
            if (v == nint.Zero || CFGetTypeID(v) != CFNumberGetTypeID()) return 0;
            // kCFNumberDoubleType = 13
            return CFNumberGetValue(v, 13, out double d) ? d : 0;
        }
        catch { return 0; }
    }
}
