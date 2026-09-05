using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gort.Imaging;
using Gort.Ocr;
using Gort.Platform;
using Gort.Text;
using Gort.Translate;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Gort.Tests;

/// <summary>
/// Smoke funcional fim a fim com componentes REAIS (sem rede exceto onde
/// indicado): captura GDI da tela, OCR moderno em texto sintético e OCR da
/// captura, agrupamento, tradução web gratuita real e análise de cor.
/// Falha aqui = defeito funcional, não cosmético.
/// </summary>
public class FuncSmokeTests
{
    private readonly ITestOutputHelper _out;
    public FuncSmokeTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Smoke_Capture_Screen()
    {
        if (!OperatingSystem.IsWindows()) return;   // captura CLI exige SO real
        var cap = new Platform.Windows.WindowsCapture();
        var v = cap.VirtualScreen;
        _out.WriteLine($"VIRTUAL {v.X},{v.Y} {v.W}x{v.H}");
        Assert.True(v.W > 0 && v.H > 0);
        Assert.NotEmpty(cap.GetMonitors());
        // Captura um pedaço do monitor primário.
        var img = cap.CaptureRect(0, new ScreenRect(v.X, v.Y,
            Math.Min(800, v.W), Math.Min(600, v.H)), true);
        Assert.NotNull(img);   // nulo só se fora da tela
        Assert.Equal(4, img.Channels);
        Assert.True(img.Bytes.Length > 0);
        Assert.NotNull(img.OrigBytes);
        _out.WriteLine($"CAP {img.Width}x{img.Height}");
    }

    private static RegionImage RenderText(string text, int w = 640, int h = 160)
    {
        using var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var font = new SKFont(SKTypeface.FromFamilyName("Arial"), 64);
        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
        };
        canvas.DrawText(text, 20, 110, SKTextAlign.Left, font, paint);
        using var bgra = bmp.Copy(SKColorType.Bgra8888);
        var bytes = new byte[w * h * 4];
        System.Runtime.InteropServices.Marshal.Copy(bgra.GetPixels(), bytes, 0, bytes.Length);
        return new RegionImage
        {
            Index = 0, Width = w, Height = h, Channels = 4, Bytes = bytes,
        };
    }

    [Fact]
    public async Task Smoke_Ocr_Modern_Synthetic()
    {
        var engines = new List<(string Id, string Display, bool Available, string? Reason)>();
        OcrEngines.Initialize(() => false, () => "eng", () => false, () => "", () => 950);
        try
        {
            foreach (var e in OcrEngines.List()) engines.Add(e);
            var modern = OcrEngines.Get("modern");
            Assert.NotNull(modern);
            _out.WriteLine($"MODERN available={modern.IsAvailable} {modern.UnavailableReason}");
            Assert.True(modern.IsAvailable);
            var img = RenderText("HELLO WORLD");
            var proc = Preprocess.Run(img, new List<ScreenRect>(),
                FilterMode.None, new List<(int, int, int, int, int, int, int)>(),
                127, false, 1.0, false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var res = await modern.RecognizeAsync(proc, "eng", cts.Token);
            Assert.Null(res.Error);
            Assert.False(res.IsEmpty);
            string all = string.Join(" ", res.Words.ConvertAll(w => w.Text));
            _out.WriteLine("OCR: " + all);
            Assert.Contains("HELLO", all);
            // Agrupamento + pipeline textual funcionam sobre o resultado.
            var region = Lines.Build(res, 0, false);
            var blocks = Grouping.Group(region.Lines, true, false, false);
            Assert.NotEmpty(blocks);
        }
        finally { OcrEngines.ShutdownAll(); }
    }

    [Fact]
    public async Task Smoke_Translate_WebFree_Real()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var svc = new WebFreeTranslator();
        var r = await svc.TranslateAsync(new List<string> { "Hello" },
            "en", "pt", cts.Token);
        if (r.Error is not null)
        {
            _out.WriteLine("WEB-FREE erro (rede/limite, tolerado no smoke): " + r.Error);
            return;   // sem rede ou cota: o laço degradaria igual (P8)
        }
        Assert.NotNull(r.Translations);
        string tr = r.Translations[0];
        _out.WriteLine("WEB-FREE: Hello -> " + tr);
        Assert.Contains("Ol", tr);   // "Olá"
    }

    [Fact]
    public void Smoke_ColorAnalysis_TwoTone()
    {
        // Bloco claro com palavra escura no meio.
        int w = 200, h = 60;
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < bytes.Length; i += 4)
        { bytes[i] = 240; bytes[i + 1] = 240; bytes[i + 2] = 240; bytes[i + 3] = 255; }
        for (int y = 15; y < 45; y++)
            for (int x = 40; x < 160; x++)
            {
                int o = (y * w + x) * 4;
                bytes[o] = 20; bytes[o + 1] = 20; bytes[o + 2] = 20;
            }
        var words = new List<Gort.Overlay.ColorAnalysis.WordBox>
        {
            new() { X = 40, Y = 15, W = 120, H = 30 },
        };
        var res = Gort.Overlay.ColorAnalysis.Analyze(
            bytes, w, h, 4, 20, 5, 160, 50, words);
        Assert.False(res.Failed);
        Assert.True(res.Contrast > 1);   // par distinguível (legível)
        int dr = System.Math.Abs(res.Font.R - res.Background.R);
        int dg = System.Math.Abs(res.Font.G - res.Background.G);
        int db = System.Math.Abs(res.Font.B - res.Background.B);
        Assert.True(dr + dg + db > 200);   // fonte e fundo bem distintos
        _out.WriteLine($"COR fg=({res.Font.R},{res.Font.G},{res.Font.B}) " +
            $"bg=({res.Background.R},{res.Background.G},{res.Background.B}) " +
            $"contraste={res.Contrast:F1}");
    }
}
