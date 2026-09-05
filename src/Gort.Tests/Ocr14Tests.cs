using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gort.Imaging;
using Gort.Ocr;
using Gort.Ocr.Classic;
using Gort.Ocr.Cloud;
using Gort.Ocr.Venv;

namespace Gort.Tests;

public class Ocr14Tests
{
    [Fact]
    public void Tess_Urls_And_Fast_Rule()
    {
        Assert.Contains("tessdata_fast", ClassicEngine.TessUrl("eng", fast: true));
        Assert.Contains("github.com/tesseract-ocr", ClassicEngine.TessUrl("eng", fast: true));
        Assert.Contains("eng.traineddata", ClassicEngine.TessUrl("eng", fast: true));
        Assert.Contains("/tessdata/raw/", ClassicEngine.TessUrl("jpn", fast: false));
        Assert.Contains("jpn.traineddata", ClassicEngine.TessUrl("jpn", fast: false));
    }

    [Fact]
    public void Mapper_Preserve_And_Scope()
    {
        Assert.Equal("jpn", OcrLangMapper.Preserve("jpn", new List<string> { "eng", "jpn" }));
        Assert.Equal("eng", OcrLangMapper.Preserve("jpn", new List<string> { "eng" }));  // RF-149
        Assert.Equal(new List<string> { "eng" },
            OcrLangMapper.IntersectScope(new List<string> { "eng", "fra" }));             // RF-151
        Assert.Equal(new List<string> { "eng", "auto" },
            OcrLangMapper.IntersectScope(new List<string> { "eng", "auto" }));            // auto preservado
    }

    [Fact]
    public void Punctual_Flags()
    {
        OcrEngines.Initialize(() => false, () => "eng", () => false, () => "", () => 950);
        Assert.True(OcrEngines.Get("cloud")!.PunctualOnly);   // RF-122
        Assert.False(OcrEngines.Get("modern")!.PunctualOnly);
        Assert.False(OcrEngines.Get("classic")!.PunctualOnly);
        Assert.False(OcrEngines.Get("venv")!.PunctualOnly);
        var ids = new List<string>();
        foreach (var (id, _, _, _) in OcrEngines.List()) ids.Add(id);
        Assert.Contains("modern", ids);                       // RF-120/575
        Assert.Contains("classic", ids);
        Assert.Contains("venv", ids);
        Assert.Contains("cloud", ids);
        Assert.DoesNotContain("os", ids);                     // indisponível: fora da lista
    }

    [Fact]
    public void Cloud_Parse_Lines_By_FullText()
    {
        string sym(char c, int x) =>
            $"{{\"text\":\"{c}\",\"boundingBox\":{{\"vertices\":[{{\"x\":{x},\"y\":0}},{{\"x\":{x + 8},\"y\":0}},{{\"x\":{x + 8},\"y\":20}},{{\"x\":{x},\"y\":20}}]}}}}";
        string word(string w, int x0)
        {
            var parts = new List<string>();
            for (int i = 0; i < w.Length; i++) parts.Add(sym(w[i], x0 + i * 9));
            return "{\"symbols\":[" + string.Join(",", parts) + "]}";
        }
        string json = "{\"responses\":[{\"fullTextAnnotation\":{\"text\":\"Hello\\nWorld\\n\"," +
            "\"pages\":[{\"blocks\":[{\"paragraphs\":[{\"words\":[" +
            word("Hello", 0) + "," + word("World", 100) + "]}]}]}]}}]}";
        var r = CloudEngine.Parse(json);                       // RF-144
        Assert.Null(r.Error);
        Assert.Equal(2, r.LineCount);
        Assert.Equal(new List<int> { 1, 1 }, r.WordsPerLine);
        Assert.Equal("Hello", r.Words[0].Text);
        Assert.Equal("World", r.Words[1].Text);
        Assert.True(r.Words[1].X > r.Words[0].X);
    }

    [Fact]
    public void Cloud_Jwt_Shape()
    {
        string jwt = CloudEngine.JwtUnsigned("a@b.c");
        var parts = jwt.Split('.');
        Assert.Equal(2, parts.Length);   // header.claim (assinatura à parte)
        string header = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(parts[0].Replace('-', '+').Replace('_', '/')));
        Assert.Contains("\"RS256\"", header);
        string claim = System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/')
                .PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '=')));
        Assert.Contains("a@b.c", claim);
        Assert.Contains("exp", claim);
    }

    [Fact]
    public void Venv_Parse_Lines()
    {
        var r = VenvEngine.ParseLine(
            "[{\"text\":\"Ola mundo\",\"box\":[10,20,100,40]}]");
        Assert.Single(r.Words);                                // RF-141: 1 por linha
        Assert.Equal("Ola mundo", r.Words[0].Text);
        Assert.Equal((10, 20, 90, 20),
            (r.Words[0].X, r.Words[0].Y, r.Words[0].W, r.Words[0].H));
        var err = VenvEngine.ParseLine("[{\"error\":\"x\"}]");
        Assert.NotNull(err.Error);
    }

    [Fact]
    public async Task Classic_English_Recognizes()
    {
        // Baixa eng_fast sob demanda para o DataDir real (onde o app baixa);
        // sem rede, pula com nota.
        string dir = Path.Combine(ClassicEngine.DataDir, "fast");
        string dest = Path.Combine(dir, "eng.traineddata");
        try
        {
            if (!File.Exists(dest))
            {
                Directory.CreateDirectory(dir);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
                using var resp = await http.GetAsync(ClassicEngine.TessUrl("eng", true));
                resp.EnsureSuccessStatusCode();
                await using var fs = File.Create(dest);
                await resp.Content.CopyToAsync(fs);
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine("SKIP tessdata: " + ex.Message);
            return;
        }
        using var eng = new ClassicEngine(() => "eng", () => true);
        Assert.Contains("eng", eng.SupportedOcrLanguages());   // RF-151
        var proc = Preprocess.Run(RenderHello(),
            new System.Collections.Generic.List<Platform.ScreenRect>(),
            FilterMode.None,
            new System.Collections.Generic.List<(int, int, int, int, int, int, int)>(),
            127, erode: false, zoom: 2.0, disabled: false);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var r = await eng.RecognizeAsync(proc, "eng", cts.Token);
        Assert.Null(r.Error);
        Assert.False(r.IsEmpty);
        string text = string.Join(" ", r.Words.ConvertAll(w => w.Text));
        Assert.Contains("HELLO", text.ToUpperInvariant());
        // Dispose libera o nativo (antes vazava para o resto da suite).
    }

    private static RegionImage RenderHello()
    {
        var bmp = new SkiaSharp.SKBitmap(600, 160);
        using (var canvas = new SkiaSharp.SKCanvas(bmp))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            using var font = new SkiaSharp.SKFont(
                SkiaSharp.SKTypeface.FromFamilyName("Arial"), 72);
            using var paint = new SkiaSharp.SKPaint
            {
                Color = SkiaSharp.SKColors.Black,
            };
            canvas.DrawText("HELLO", 40, 110,
                SkiaSharp.SKTextAlign.Left, font, paint);
        }
        return new RegionImage
        {
            Index = 0, Width = bmp.Width, Height = bmp.Height,
            Channels = 4, Bytes = bmp.Bytes,
        };
    }
}
