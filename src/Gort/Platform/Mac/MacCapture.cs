using System;
using Gort.Imaging;
using Gort.Platform.Cli;

namespace Gort.Platform.Mac;

/// <summary>
/// C1 no macOS via `screencapture -x` (silencioso, sem miniatura).
/// Requer permissão de "Gravação de Tela" nas Preferências (pedida pelo SO
/// na primeira captura); sem ela o arquivo sai vazio e devolvemos nulo
/// (P8: área pulada, laço continua — RF-569).
/// </summary>
public sealed class MacCapture : CliCapture
{
    internal static string? ToolPath;
    internal static bool Probed;

    internal static string? Tool()
    {
        if (Probed) return ToolPath;
        Probed = true;
        ToolPath = Shell.Which("screencapture");
        return ToolPath;
    }

    public static bool IsSupported => Tool() is not null;

    protected override string? CaptureFullToFile(string destPng)
    {
        try
        {
            string? tool = Tool();
            if (tool is null) return null;
            // -x: sem som/miniatura; -t png: formato; por último o destino.
            if (!Shell.RunToFile(tool, $"-x -t png \"{destPng}\"", 8000)) return null;
            var info = new System.IO.FileInfo(destPng);
            if (!info.Exists || info.Length == 0) return null;   // sem permissão?
            return destPng;
        }
        catch { return null; }
    }

    public bool SupportsClientArea => MacWindows.IsSupported;

    /// <summary>
    /// RF-088 fonte 2: geometria da janela frontal via CGWindowList,
    /// captura cheia + recorte relativo (mesmo contrato do Windows).
    /// </summary>
    public RegionImage? CaptureClientArea(int index, ScreenRect areaScreen, bool needOriginal)
    {
        try
        {
            if (!MacWindows.TryGetFrontmostRect(out var client)) return null;
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
                OrigChannels = needOriginal ? 4 : 0,
                OrigBytes = needOriginal ? (byte[])bytes.Clone() : null,
            };
        }
        catch { return null; }
    }
}
