using System.Collections.Generic;
using Gort.Overlay;

namespace Gort.Tests;

public class ColorTests
{
    private static byte[] Solid(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < px.Length; i += 4)
        { px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
        return px;
    }

    private static void PaintWord(byte[] px, int w, int x, int y, int ww, int hh,
        byte r, byte g, byte b)
    {
        for (int yy = y; yy < y + hh; yy++)
            for (int xx = x; xx < x + ww; xx++)
            {
                int o = (yy * w + xx) * 4;
                px[o] = b; px[o + 1] = g; px[o + 2] = r; px[o + 3] = 255;
            }
    }

    private static ColorAnalysis.WordBox W(int x, int y, int w, int h) =>
        new() { X = x, Y = y, W = w, H = h };

    private static (byte[] Px, ColorAnalysis.WordBox Box) RenderWord(int w, int h,
        byte br, byte bg, byte bb, byte fr, byte fg, byte fb, string text)
    {
        using var bmp = new SkiaSharp.SKBitmap(w, h);
        using var canvas = new SkiaSharp.SKCanvas(bmp);
        canvas.Clear(new SkiaSharp.SKColor(br, bg, bb));
        using var font = new SkiaSharp.SKFont(
            SkiaSharp.SKTypeface.Default, 24);
        using var paint = new SkiaSharp.SKPaint
        {
            Color = new SkiaSharp.SKColor(fr, fg, fb),
            IsAntialias = false,
        };
        canvas.DrawText(text, 10, 30, SkiaSharp.SKTextAlign.Left, font, paint);
        // Caixa da palavra = extensão medida + folga (cantos ficam fundo).
        float tw = font.MeasureText(text);
        var px = new byte[w * h * 4];
        System.Runtime.InteropServices.Marshal.Copy(
            bmp.GetPixels(), px, 0, px.Length);
        // SKBitmap é BGRA pré-multiplicado opaco: bytes diretos servem.
        return (px, new ColorAnalysis.WordBox
        {
            X = 8, Y = 6, W = (int)tw + 4, H = 28,
        });
    }

    [Fact]
    public void White_On_DarkBlue()
    {
        var (px, box) = RenderWord(80, 44, 20, 40, 120, 255, 255, 255, "Hi");
        var r = ColorAnalysis.Analyze(px, 80, 44, 4, 4, 2, 72, 40,
            new List<ColorAnalysis.WordBox> { box });
        Assert.False(r.Failed);
        Assert.True(r.Font.R > 200 && r.Font.G > 200 && r.Font.B > 200);
        Assert.True(r.Background.B > r.Background.R + 30);   // azul escuro
        Assert.True(r.Contrast >= 2.5);
    }

    [Fact]
    public void Black_On_Beige()
    {
        var (px, box) = RenderWord(80, 44, 240, 230, 200, 0, 0, 0, "Hi");
        var r = ColorAnalysis.Analyze(px, 80, 44, 4, 4, 2, 72, 40,
            new List<ColorAnalysis.WordBox> { box });
        Assert.False(r.Failed);
        Assert.True(r.Font.R < 60 && r.Font.G < 60 && r.Font.B < 60);
        Assert.True(r.Contrast >= 2.5);
    }

    [Fact]
    public void Low_Contrast_Forced_To_Legible()
    {
        // Cinza-claro sobre branco: ilegível → preto ou branco com contraste.
        var px = Solid(60, 40, 255, 255, 255);
        PaintWord(px, 60, 10, 10, 40, 20, 230, 230, 230);
        var r = ColorAnalysis.Analyze(px, 60, 40, 4, 5, 5, 50, 30,
            new List<ColorAnalysis.WordBox> { W(10, 10, 40, 20) });
        Assert.False(r.Failed);
        Assert.True(r.Contrast >= 2.5);                        // RF-410
    }

    [Fact]
    public void Contrast_Known_Value()
    {
        double c = ColorAnalysis.ContrastRatio((255, 255, 255), (0, 0, 0));
        Assert.InRange(c, 20.9, 21.1);                         // RF-411
    }

    [Fact]
    public void Derived_Outlines()
    {
        var (c1, c2) = ColorAnalysis.DeriveOutlines((250, 250, 250));  // RF-393
        Assert.Equal(((byte)0, (byte)0, (byte)0), c2);                // clara → preto
        var (_, c2b) = ColorAnalysis.DeriveOutlines((10, 10, 10));
        Assert.Equal(((byte)255, (byte)255, (byte)255), c2b);         // escura → branco
    }

    [Fact]
    public void Empty_Rect_Fails()
    {
        var px = Solid(10, 10, 0, 0, 0);
        var r = ColorAnalysis.Analyze(px, 10, 10, 4, 50, 50, 0, 0,
            new List<ColorAnalysis.WordBox>());
        Assert.True(r.Failed);                                 // RF-404 → configuradas
    }
}
