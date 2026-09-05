using System;
using System.Runtime.InteropServices;

namespace Gort.Platform.Linux;

/// <summary>
/// Efeitos de janela no Linux/X11 (C7): máscara de entrada vazia via
/// XFixes/XShape — cliques atravessam a janela. Abre conexão própria com o
/// display (operações X são por servidor, não por conexão). No Wayland não
/// há API equivalente → devolve false e a capacidade fica indisponível.
/// Tudo com try/catch; falha nunca quebra a UI.
/// </summary>
public static class LinuxFx
{
    public static bool IsX11 =>
        (Environment.GetEnvironmentVariable("DISPLAY")?.Length ?? 0) > 0;

    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")]
    private static extern nint XOpenDisplay(string? display);

    [DllImport("libX11.so.6", EntryPoint = "XCloseDisplay")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libXext.so.6", EntryPoint = "XShapeCombineMask")]
    private static extern void XShapeCombineMask(nint display, nint window,
        int destKind, int xOff, int yOff, nint pixmap, int op);

    private const int ShapeInput = 2;
    private const int ShapeSet = 0;

    /// <summary>C7 no X11: máscara de entrada vazia (through) ou restaura.</summary>
    public static bool SetClickThrough(nint xid, bool through)
    {
        if (!IsX11 || xid == nint.Zero) return false;
        nint? display = null;
        try
        {
            display = XOpenDisplay(null);
            if (display == nint.Zero) return false;
            if (through)
            {
                // Máscara vazia: nenhuma região recebe entrada do ponteiro.
                XShapeCombineMask(display.Value, xid, ShapeInput, 0, 0, nint.Zero, ShapeSet);
                return true;
            }
            // Sem "desfazer" portátil no X11 (só recriando a janela):
            // reporta o real; chamadores seguem sem quebrar.
            return false;
        }
        catch { return false; }
        finally
        {
            try { if (display.HasValue && display.Value != nint.Zero) XCloseDisplay(display.Value); }
            catch { }
        }
    }
}
