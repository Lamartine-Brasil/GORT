using System;
using System.Collections.Generic;
using Gort.Platform.Cli;

namespace Gort.Platform.Linux;

/// <summary>
/// C3/C12 no Linux/X11 via `wmctrl -l -G` (lista com geometria) e
/// `xdotool getactivewindow getwindowgeometry --shell` (ativa).
/// No Wayland essas ferramentas não enxergam outras janelas por desenho —
/// então o serviço fica indisponível com motivo claro e a UI orienta para
/// áreas fixas. Nunca lança.
/// </summary>
public sealed class LinuxWindows : IWindowService
{
    internal static string? Wmctrl;
    internal static string? Xdotool;
    internal static bool Probed;

    internal static void Probe()
    {
        if (Probed) return;
        Probed = true;
        Wmctrl = Shell.Which("wmctrl");
        Xdotool = Shell.Which("xdotool");
    }

    public static bool IsSupported
    {
        get
        {
            Probe();
            // X11 tem DISPLAY; Wayland puro sem XWayland não serve.
            string? display = Environment.GetEnvironmentVariable("DISPLAY");
            return display?.Length > 0 && (Wmctrl is not null || Xdotool is not null);
        }
    }

    public bool IsAvailable => IsSupported;

    public string? UnavailableReason
    {
        get
        {
            if (IsSupported) return null;
            string? session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            if (session?.Equals("wayland", StringComparison.OrdinalIgnoreCase) == true)
                return "No Wayland o compositor não expõe janelas alheias. Use áreas fixas de OCR.";
            return "Lista de janelas indisponível. No X11, instale `wmctrl` ou `xdotool`. " +
                "Sem eles, use áreas fixas de OCR.";
        }
    }

    public IReadOnlyList<WindowRef> ListCapturableWindows()
    {
        var list = new List<WindowRef>();
        try
        {
            Probe();
            if (Wmctrl is null) return list;
            var (output, code) = Shell.Run(Wmctrl, "-l -G", 5000);
            if (code != 0) return list;
            foreach (var line in output.Split('\n'))
            {
                // id desktop X Y W H host título…
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 8) continue;
                if (!int.TryParse(parts[2], out int x)) continue;
                if (!int.TryParse(parts[3], out int y)) continue;
                if (!int.TryParse(parts[4], out int w)) continue;
                if (!int.TryParse(parts[5], out int h)) continue;
                if (w < 50 || h < 50) continue;
                string title = string.Join(" ", parts, 7, parts.Length - 7);
                if (title.Length == 0) continue;
                if (!nint.TryParse(parts[0],
                        System.Globalization.NumberStyles.HexNumber, null, out nint id))
                    id = (nint)(1000 + list.Count);
                list.Add(new WindowRef(id, title));
                if (list.Count >= 200) break;
            }
        }
        catch { }
        return list;
    }

    public WindowRef? ForegroundWindow()
    {
        try
        {
            Probe();
            if (Xdotool is null) return null;
            var (output, code) = Shell.Run(Xdotool,
                "getactivewindow getwindowname", 3000);
            if (code != 0 || output.Trim().Length == 0) return null;
            return new WindowRef(1, output.Trim());
        }
        catch { return null; }
    }

    public ScreenRect FrameBounds(WindowRef window) =>
        TryGetActiveRect(out var r) ? r : new ScreenRect(0, 0, 0, 0);

    public ScreenRect ClientOrigin(WindowRef window) =>
        TryGetActiveRect(out var r) ? new ScreenRect(r.X, r.Y, 0, 0) : new ScreenRect(0, 0, 0, 0);

    internal static bool TryGetActiveRect(out ScreenRect rect)
    {
        rect = new ScreenRect(0, 0, 0, 0);
        try
        {
            Probe();
            if (Xdotool is null) return false;
            var (output, code) = Shell.Run(Xdotool,
                "getactivewindow getwindowgeometry --shell", 3000);
            if (code != 0) return false;
            int x = 0, y = 0, w = 0, h = 0;
            foreach (var line in output.Split('\n'))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2) continue;
                switch (kv[0].Trim())
                {
                    case "X": int.TryParse(kv[1], out x); break;
                    case "Y": int.TryParse(kv[1], out y); break;
                    case "WIDTH": int.TryParse(kv[1], out w); break;
                    case "HEIGHT": int.TryParse(kv[1], out h); break;
                }
            }
            if (w <= 0 || h <= 0) return false;
            rect = new ScreenRect(x, y, w, h);
            return true;
        }
        catch { return false; }
    }
}
