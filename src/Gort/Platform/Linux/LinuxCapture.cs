using System;
using Gort.Imaging;
using Gort.Platform.Cli;

namespace Gort.Platform.Linux;

/// <summary>
/// C1 no Linux via ferramentas externas (primeira que funcionar, sondada
/// uma vez): `grim` (Wayland/wlroots) → `maim` → `import` (ImageMagick/X11)
/// → `scrot` (X11). Captura cheia + recorte gerenciado (base CliCapture).
/// Sem ferramenta: indisponível com motivo que nomeia o que instalar.
/// </summary>
public sealed class LinuxCapture : CliCapture
{
    internal static string? ToolName;
    internal static string? ToolPath;
    internal static bool Probed;

    internal static void Probe()
    {
        if (Probed) return;
        Probed = true;
        foreach (var name in new[] { "grim", "maim", "import", "scrot" })
        {
            var p = Shell.Which(name);
            if (p is not null) { ToolName = name; ToolPath = p; return; }
        }
    }

    public static bool IsSupported
    {
        get { Probe(); return ToolPath is not null; }
    }

    public static string? UnavailableHint
    {
        get
        {
            Probe();
            if (ToolPath is not null) return null;
            return "Nenhuma ferramenta de captura encontrada. Instale uma: " +
                "`grim` (Wayland) ou `maim`/`import`/`scrot` (X11).";
        }
    }

    protected override string? CaptureFullToFile(string destPng)
    {
        try
        {
            Probe();
            if (ToolPath is null || ToolName is null) return null;
            string args = ToolName switch
            {
                "grim" => $"\"{destPng}\"",                          // grim ARQ
                "maim" => $"\"{destPng}\"",                          // maim ARQ
                "import" => $"-window root \"png:{destPng}\"",       // import
                "scrot" => $"\"{destPng}\"",                         // scrot ARQ
                _ => $"\"{destPng}\"",
            };
            if (!Shell.RunToFile(ToolPath, args, 8000)) return null;
            var info = new System.IO.FileInfo(destPng);
            if (!info.Exists || info.Length == 0) return null;
            return destPng;
        }
        catch { return null; }
    }

    public bool SupportsClientArea => LinuxWindows.IsSupported;

    /// <summary>RF-088 fonte 2: geometria via xdotool/wmctrl + recorte.</summary>
    public RegionImage? CaptureClientArea(int index, ScreenRect areaScreen, bool needOriginal)
    {
        try
        {
            if (!LinuxWindows.TryGetActiveRect(out var client)) return null;
            if (client.W <= 0 || client.H <= 0) return null;
            var full = CaptureRect(-1, client, needOriginal);
            if (full is null) return null;
            int x1 = Math.Max(areaScreen.X, client.X);
            int y1 = Math.Max(areaScreen.Y, client.Y);
            int x2 = Math.Min(areaScreen.X + areaScreen.W, client.X + client.W);
            int y2 = Math.Min(areaScreen.Y + areaScreen.H, client.Y + client.H);
            int w = x2 - x1, h = y2 - y1;
            if (w <= 0 || h <= 0) return null;
            var bytes = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(full.Bytes, ((y1 - client.Y + y) * full.Width + (x1 - client.X)) * 4,
                    bytes, y * w * 4, w * 4);
            return new RegionImage
            {
                Index = index, Width = w, Height = h, Channels = 4, Bytes = bytes,
                OrigWidth = needOriginal ? w : 0, OrigHeight = needOriginal ? h : 0,
                OrigBytes = needOriginal ? (byte[])bytes.Clone() : null,
            };
        }
        catch { return null; }
    }
}
