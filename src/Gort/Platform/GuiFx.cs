using System;
using Avalonia.Controls;

namespace Gort.Platform;

/// <summary>
/// Efeitos de janela multiplataforma (C7/C8/C9) atrás de uma fachada única.
/// A UI chama aqui; cada SO resolve com suas APIs e falha em silêncio
/// quando indisponível (a capacidade real está no CapabilityReport).
/// - Windows: WS_EX_TRANSPARENT / SetWindowDisplayAffinity / DwmFlush.
/// - macOS: NSWindow.ignoresMouseEvents; C8 via concealer; C9 no-op.
/// - Linux/X11: máscara de entrada XShape; C8 via concealer; C9 no-op.
/// - Wayland: sem C7/C8 (janelas normais; concealer evita auto-captura).
/// </summary>
public static class GuiFx
{
    public static nint? HandleOf(Window w)
    {
        try { return w.TryGetPlatformHandle()?.Handle; }
        catch { return null; }
    }

    /// <summary>C7: cliques atravessam a janela (alternável em execução).</summary>
    public static void SetClickThrough(Window w, bool through)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Windows.WindowFx.SetClickThrough(w, through);
                return;
            }
            var h = HandleOf(w);
            if (h is null) return;
            if (OperatingSystem.IsMacOS()) Mac.MacFx.SetClickThrough(h.Value, through);
            else if (OperatingSystem.IsLinux()) Linux.LinuxFx.SetClickThrough(h.Value, through);
        }
        catch { }
    }

    /// <summary>C8: exclui de capturas/gravações (só Windows tem a API).</summary>
    public static void SetCaptureExclusion(Window w, bool exclude)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Windows.WindowFx.SetCaptureExclusion(w, exclude);
            // macOS/Linux: sem afinidade por janela — o concealer do laço
            // (PlatformFactory.CaptureConcealer) cobre a auto-captura.
        }
        catch { }
    }

    /// <summary>C9: sincroniza com o compositor (só Windows precisa).</summary>
    public static void SyncCompositor()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Windows.WindowFx.SyncCompositor();
        }
        catch { }
    }
}
