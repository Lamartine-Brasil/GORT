using System;
using System.Collections.Generic;
using Gort.Config;
using Gort.Imaging;
using Gort.Platform;

namespace Gort.Tests;

/// <summary>Etapa 4 — critérios de aceite do cap. 13.</summary>
public class PreprocessTests
{
    private static readonly List<(int R, int G, int B, int S1, int S2, int V1, int V2)> NoGroups = new();

    private static RegionImage White(int w, int h)
    {
        var b = new byte[w * h * 4];
        for (int i = 0; i < b.Length; i += 4)
        { b[i] = b[i + 1] = b[i + 2] = b[i + 3] = 255; }
        return new RegionImage { Index = 0, Width = w, Height = h, Channels = 4, Bytes = b };
    }

    private static void BlackBar(RegionImage img, int y0, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int o = (y * img.Width + x) * 4;
                img.Bytes[o] = img.Bytes[o + 1] = img.Bytes[o + 2] = 0;
            }
    }

    private static int CountDark(byte[] gray, int below = 128)
    {
        int n = 0;
        foreach (var v in gray) if (v < below) n++;
        return n;
    }

    [Fact]
    public void Exclusion_Is_Invisible_To_Ocr()
    {
        // Texto preto em fundo branco; exclusão no meio do texto.
        var img = White(40, 20);
        BlackBar(img, 8, 12);
        var excl = new List<ScreenRect> { new(20, 0, 10, 20) };
        var outImg = Preprocess.Run(img, excl, FilterMode.Threshold, NoGroups, 127,
            erode: false, zoom: 1.0, disabled: false);
        Assert.Equal(1, outImg.Channels);
        // Dentro do apagado: tudo fundo (255). Fora: texto restante existe.
        for (int y = 0; y < 20; y++)
            for (int x = 20; x < 30; x++)
                Assert.Equal(255, outImg.Bytes[y * 40 + x]);
        Assert.True(CountDark(outImg.Bytes) > 0);
        // Geometria inalterada (RF-103).
        Assert.Equal(40, outImg.Width);
        Assert.Equal(20, outImg.Height);
    }

    [Fact]
    public void Exclusion_RGB_Uses_Failing_Color_Not_Fixed_White()
    {
        // Grupo = branco exato: preencher de branco fixo PASSARIA (erro).
        var img = White(30, 10);
        BlackBar(img, 3, 7);
        var groups = new List<(int, int, int, int, int, int, int)> { (255, 255, 255, 0, 0, 0, 0) };
        var excl = new List<ScreenRect> { new(10, 0, 10, 10) };
        var outImg = Preprocess.Run(img, excl, FilterMode.Rgb, groups, 127,
            erode: false, zoom: 1.0, disabled: false);
        for (int y = 0; y < 10; y++)
            for (int x = 10; x < 20; x++)
                Assert.Equal(255, outImg.Bytes[y * 30 + x]);   // virou fundo
    }

    [Fact]
    public void Exclusion_NoFilter_Fills_Border_Color()
    {
        var img = White(20, 20);
        BlackBar(img, 8, 12);
        var excl = new List<ScreenRect> { new(4, 4, 12, 12) };  // borda branca
        var outImg = Preprocess.Run(img, excl, FilterMode.None, NoGroups, 127,
            erode: false, zoom: 1.0, disabled: false);
        Assert.False(outImg.IsBinarized);                       // RF-110
        Assert.Equal(20, outImg.Width);                         // RF-103
        // Apagado virou a cor da borda (branca), inclusive sobre o texto;
        // o texto fora da exclusão permanece.
        for (int y = 4; y < 16; y++)
            for (int x = 4; x < 16; x++)
            {
                int o = (y * 20 + x) * 4;
                Assert.Equal(255, outImg.Bytes[o]);
            }
        bool restou = false;
        for (int y = 8; y < 12 && !restou; y++)
            for (int x = 0; x < 4 && !restou; x++)
                if (outImg.Bytes[(y * 20 + x) * 4] == 0) restou = true;
        Assert.True(restou);
    }

    [Fact]
    public void Disabled_Delivers_Original_Untouched()
    {
        var img = White(20, 20);
        BlackBar(img, 8, 12);
        var excl = new List<ScreenRect> { new(0, 0, 20, 20) };
        var outImg = Preprocess.Run(img, excl, FilterMode.Threshold, NoGroups, 127,
            erode: true, zoom: 4.0, disabled: true);            // RF-118
        Assert.Equal(img.Bytes, outImg.Bytes);
        Assert.False(outImg.IsBinarized);
    }

    [Fact]
    public void Erode_Thins_3x3_To_Center()
    {
        var img = White(7, 7);
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
            {
                int o = (y * 7 + x) * 4;
                img.Bytes[o] = img.Bytes[o + 1] = img.Bytes[o + 2] = 0;
            }
        var nos = new List<ScreenRect>();
        var eroded = Preprocess.Run(img, nos, FilterMode.Threshold, NoGroups, 127,
            erode: true, zoom: 1.0, disabled: false);
        Assert.Equal(1, CountDark(eroded.Bytes));               // só o centro
        var plain = Preprocess.Run(img, nos, FilterMode.Threshold, NoGroups, 127,
            erode: false, zoom: 1.0, disabled: false);
        Assert.Equal(9, CountDark(plain.Bytes));
    }

    [Fact]
    public void Erode_Removes_Isolated_Noise()
    {
        var img = White(9, 9);                                  // RF-111: ponto some
        int o = (4 * 9 + 4) * 4;
        img.Bytes[o] = img.Bytes[o + 1] = img.Bytes[o + 2] = 0;
        var outImg = Preprocess.Run(img, new List<ScreenRect>(), FilterMode.Threshold,
            NoGroups, 127, erode: true, zoom: 1.0, disabled: false);
        Assert.Equal(0, CountDark(outImg.Bytes));
    }

    [Fact]
    public void Erode_Runs_Before_Zoom()
    {
        var img = White(7, 7);
        for (int y = 2; y <= 4; y++)
            for (int x = 2; x <= 4; x++)
            {
                int oo = (y * 7 + x) * 4;
                img.Bytes[oo] = img.Bytes[oo + 1] = img.Bytes[oo + 2] = 0;
            }
        var outImg = Preprocess.Run(img, new List<ScreenRect>(), FilterMode.Threshold,
            NoGroups, 127, erode: true, zoom: 2.0, disabled: false);
        Assert.Equal(14, outImg.Width);                         // RF-112: amplia depois
        Assert.Equal(14, outImg.Height);
        Assert.True(CountDark(outImg.Bytes) > 0);
    }

    [Fact]
    public void Zoom_Dimensions()
    {
        var img = White(10, 6);
        var outImg = Preprocess.Run(img, new List<ScreenRect>(), FilterMode.None,
            NoGroups, 127, erode: false, zoom: 2.5, disabled: false);
        Assert.Equal(25, outImg.Width);
        Assert.Equal(15, outImg.Height);
    }

    [Fact]
    public void Unscale_Floors_Top_Ceils_Bottom()
    {
        var (x, y, w, h) = Preprocess.Unscale(10, 10, 20, 20, 2.0, 100, 50);
        Assert.Equal((100 + 5, 50 + 5, 5, 5), (x, y, w, h));    // RF-116
        var (x2, y2, w2, h2) = Preprocess.Unscale(11, 11, 19, 19, 2.0, 0, 0);
        Assert.Equal((5, 5, 5, 5), (x2, y2, w2, h2));
    }

    [Fact]
    public void Channels_Convert()
    {
        var gray = new byte[] { 0, 255 };
        var bgr = Preprocess.ConvertChannels(gray, 2, 1, 1, 3); // RF-117 replica
        Assert.Equal(new byte[] { 0, 0, 0, 255, 255, 255 }, bgr);
        var bgra = new byte[] { 10, 20, 30, 0, 40, 50, 60, 0 };
        var bgr2 = Preprocess.ConvertChannels(bgra, 2, 1, 4, 3); // descarta alfa
        Assert.Equal(new byte[] { 10, 20, 30, 40, 50, 60 }, bgr2);
        var g2 = Preprocess.ConvertChannels(bgr2, 2, 1, 3, 1);
        Assert.Equal(51, g2[1]);   // 0,30·60 + 0,59·50 + 0,11·40 = 51,9
        var gw = Preprocess.ConvertChannels(new byte[] { 255, 255, 255 }, 1, 1, 3, 1);
        Assert.Equal(255, gw[0]);
    }

    [Fact]
    public void Preview_Equals_Ocr_Input()
    {
        // Critério de aceite da Etapa 4: a pré-visualização produz exatamente
        // a imagem que vai ao OCR (mesmo critério, zoom 1, sem erosão).
        var img = White(24, 12);
        BlackBar(img, 4, 8);
        var groups = Preprocess.QuickGroups(darkText: false);
        var tuples = new List<(int, int, int, int, int, int, int)>();
        foreach (var g in groups) tuples.Add((g.R, g.G, g.B, g.S1, g.S2, g.V1, g.V2));
        var preview = ColorFilter.Binarize(img.Bytes, 24, 12, FilterMode.Hsv, tuples, 127);
        var ocr = Preprocess.Run(img, new List<ScreenRect>(), FilterMode.Hsv, tuples, 127,
            erode: false, zoom: 1.0, disabled: false);
        Assert.Equal(preview, ocr.Bytes);
        Assert.True(CountDark(preview) > 0);   // texto claro passa no filtro claro
    }

    [Fact]
    public void QuickGroups_Match_P26_P27_P28()
    {
        var dark = Preprocess.QuickGroups(darkText: true);      // RF-119 🔒
        Assert.Equal(2, dark.Count);
        Assert.Equal((0, 8, 0, 32), (dark[0].S1, dark[0].S2, dark[0].V1, dark[0].V2));
        Assert.Equal((95, 100, 0, 32), (dark[1].S1, dark[1].S2, dark[1].V1, dark[1].V2));
        var light = Preprocess.QuickGroups(darkText: false);
        Assert.Single(light);
        Assert.Equal((0, 10, 75, 100), (light[0].S1, light[0].S2, light[0].V1, light[0].V2));
    }

    [Fact]
    public void Zoom_Steps_And_Default()
    {
        Assert.Equal(2.5, Preprocess.ClampZoomSteps(2.3));       // passos P-25
        Assert.Equal(2.0, Preprocess.ClampZoomSteps(2.2));
        Assert.Equal(2.0, Preprocess.DefaultZoom);               // P-22 🔒 RF-115
    }

    [Fact]
    public void IsBlack_Empty_Is_Black()
    {
        Assert.True(ColorFilter.IsBlack(Array.Empty<byte>(), 4));  // sem ler fora
    }
}
