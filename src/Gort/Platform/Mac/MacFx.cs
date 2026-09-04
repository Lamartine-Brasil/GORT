using System;
using System.Runtime.InteropServices;

namespace Gort.Platform.Mac;

/// <summary>
/// Efeitos de janela no macOS (C7): NSWindow.ignoresMouseEvents via
/// Objective-C runtime. Sem P/Invoke estático para AppKit — só libobjc,
/// sempre presente. Falha → sem efeito (capacidade reporta indisponível).
/// C8 não existe no macOS: a ocultação é feita pelo concealer no laço.
/// C9 é no-op (Avalonia/VSync cuidam da composição).
/// </summary>
public static class MacFx
{
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_view(nint receiver, nint selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(nint receiver, nint selector, byte value);

    /// <summary>C7: cliques atravessam a janela Avalonia (via NSView → NSWindow).</summary>
    public static bool SetClickThrough(nint nsViewHandle, bool through)
    {
        try
        {
            if (nsViewHandle == nint.Zero) return false;
            nint selWindow = sel_registerName("window");
            nint nsWindow = objc_msgSend_view(nsViewHandle, selWindow);
            if (nsWindow == nint.Zero) return false;
            nint selIgnore = sel_registerName("setIgnoresMouseEvents:");
            objc_msgSend_bool(nsWindow, selIgnore, through ? (byte)1 : (byte)0);
            return true;
        }
        catch { return false; }
    }
}
