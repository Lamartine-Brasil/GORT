using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gort.Imaging;
using Gort.Ocr;
using Gort.Ocr.Modern;
using SkiaSharp;
using Xunit.Abstractions;

namespace Gort.Tests;

/// <summary>Etapa 5 — imagem conhecida produz palavras+caixas; branco dá vazio.</summary>
public class OcrTests
{
    private readonly ITestOutputHelper _out;
    public OcrTests(ITestOutputHelper o) => _out = o;

    private static readonly System.Collections.Generic.List<(int R, int G, int B, int S1, int S2, int V1, int V2)> NoGroups = new();
    private static readonly System.Collections.Generic.List<Gort.Platform.ScreenRect> NoRects = new();

    private static RegionImage RenderLines(params string[] lines)
    {
        var bmp = new SKBitmap(900, 130 + lines.Length * 110);
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.White);
            using var font = new SKFont(
                SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal)
                    ?? SKTypeface.Default, 84);
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
            };
            for (int i = 0; i < lines.Length; i++)
                canvas.DrawText(lines[i], 30, 110 + i * 110,
                    SKTextAlign.Left, font, paint);
        }
        return new RegionImage
        {
            Index = 0, Width = bmp.Width, Height = bmp.Height,
            Channels = 4, Bytes = bmp.Bytes,
        };
    }

    private static RapidOcrEngine NewEngine() => new(
        Path.Combine(Path.GetTempPath(), "gort-test-ocr"),
        AppContext.BaseDirectory,
        () => false);

    [Fact]
    public void Quad_Never_Negative()
    {
        // Texto rotacionado 45°: min/max, nunca diferença direta (RF-142 🔒).
        var (x, y, w, h) = QuadBox.FromPoints((10, 0), (20, 10), (10, 20), (0, 10));
        Assert.Equal((0, 0, 20, 20), (x, y, w, h));
    }

    [Fact]
    public void Registry_Lists_Exactly_Available()
    {
        OcrEngines.Initialize(() => false);                  // RF-120
        var list = OcrEngines.List();
        Assert.Contains(list, e => e.Id == "modern");
        var modern = list.First(e => e.Id == "modern");
        _out.WriteLine("modern available=" + modern.Available + " reason=" + modern.Reason);
        Assert.True(modern.Available);   // modelos latinos inclusos no pacote
    }

    [Fact]
    public async Task Known_Image_Yields_Words_With_Boxes()
    {
        var engine = NewEngine();
        Assert.True(engine.IsAvailable, engine.UnavailableReason ?? "sem motivo");
        var img = RenderLines("HELLO WORLD", "TEST 123");
        var proc = Preprocess.Run(img, NoRects, FilterMode.None, NoGroups, 127,
            erode: false, zoom: 1.0, disabled: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var res = await engine.RecognizeAsync(proc, "eng", cts.Token);
        Assert.Null(res.Error);
        Assert.False(res.IsEmpty);
        var text = string.Join(" ", res.Words.Select(w => w.Text));
        _out.WriteLine("OCR: " + text);
        Assert.Contains("HELLO", text);
        Assert.True(res.LineCount >= 1);
        Assert.Equal(res.Words.Count, res.WordsPerLine.Sum());
        foreach (var wd in res.Words)
        {
            Assert.True(wd.W > 0 && wd.H > 0);
            Assert.InRange(wd.X, 0, proc.Width);
            Assert.InRange(wd.Y, 0, proc.Height);
        }
    }

    [Fact]
    public async Task Blank_Image_Is_Empty_Not_Error()
    {
        var engine = NewEngine();
        Assert.True(engine.IsAvailable, engine.UnavailableReason ?? "sem motivo");
        var img = new RegionImage
        {
            Index = 0, Width = 400, Height = 200, Channels = 4,
            Bytes = Enumerable.Repeat((byte)255, 400 * 200 * 4).ToArray(),
        };
        var proc = Preprocess.Run(img, NoRects, FilterMode.None, NoGroups, 127,
            erode: false, zoom: 1.0, disabled: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var res = await engine.RecognizeAsync(proc, "eng", cts.Token);
        Assert.Null(res.Error);
        Assert.True(res.IsEmpty);                             // RF-145: vazio, sem erro
    }

    [Fact]
    public async Task Unknown_Language_Fails_Cleanly()
    {
        var engine = NewEngine();
        var img = RenderLines("HELLO");
        var proc = Preprocess.Run(img, NoRects, FilterMode.None, NoGroups, 127,
            erode: false, zoom: 1.0, disabled: true);
        var res = await engine.RecognizeAsync(proc, "xx", CancellationToken.None);
        Assert.True(res.IsEmpty);
        Assert.NotNull(res.Error);                            // RF-145: mensagem no lugar
    }

    [Fact]
    public void Vertical_Reorder_Respects_Horizontals()
    {
        // RF-140 🔒: verticais por coluna (direita→esquerda, topo→base);
        // horizontais mantêm a posição.
        RapidOcrNet.TextBlock V(int x, int y, int w, int h, string t) => new()
        {
            Text = t,
            Chars = [],
            CharScores = [],
            BoxPoints = new[]
            {
                new SKPointI(x, y), new SKPointI(x + w, y),
                new SKPointI(x + w, y + h), new SKPointI(x, y + h),
            },
        };
        var blocks = new[]
        {
            V(0, 0, 200, 30, "H1"),     // horizontal na posição 0
            V(300, 0, 20, 100, "V-a"),  // vertical, coluna x=300
            V(100, 0, 20, 100, "V-b"),  // vertical, coluna x=100
        };
        var ordered = RapidOcrEngine.ReorderVertical(blocks);
        Assert.Equal("H1", ordered[0].Text);    // horizontal ficou
        Assert.Equal("V-a", ordered[1].Text);   // coluna da direita primeiro
        Assert.Equal("V-b", ordered[2].Text);
    }
}
