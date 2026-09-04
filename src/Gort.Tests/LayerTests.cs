using System.Collections.Generic;
using Gort.UI;
using SkiaSharp;

namespace Gort.Tests;

public class LayerTests
{
    [Fact]
    public void Vector_Check_Runs()
    {
        Assert.True(SkiaText.RunVectorCheck());   // RF-007 nesta máquina
        Assert.True(SkiaText.VectorOk);
    }

    [Fact]
    public void Font_Falls_Back()
    {
        Assert.NotNull(SkiaText.ResolveFont("Fonte Que Não Existe"));
        Assert.NotNull(SkiaText.ResolveFont(null));   // RF-387: SO ou reserva
    }

    [Fact]
    public void Wrap_Breaks_Long_Text()
    {
        using var face = SkiaText.ResolveFont(null);
        using var meas = new SKFont(face, 20);
        var lines = SkiaText.Wrap("alpha beta gamma delta epsilon", meas, 60);
        Assert.True(lines.Count > 1);
        Assert.Equal(new List<string> { "a" }, SkiaText.Wrap("a", meas, 500));
    }

    [Fact]
    public void Outlined_Draws_NonBlank()
    {
        using var bmp = new SKBitmap(200, 60);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var face = SkiaText.ResolveFont(null);
        var (w, h) = SkiaText.DrawOutlined(canvas,
            new List<string> { "Hi" }, 5, 5, face, 24,
            SKColors.White, new SKColor(192, 192, 192), SKColors.Black,
            SkiaText.HAlign.Left, 190, outline: true);
        Assert.True(w > 10 && h > 10);
        bool any = false;
        foreach (var px in bmp.Pixels)
            if (px.Alpha > 10) { any = true; break; }
        Assert.True(any);   // RF-336: contorno duplo + preenchimento
    }

    [Fact]
    public void NoOutline_Draws_Fill_Only()
    {
        using var bmp = new SKBitmap(200, 60);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        using var face = SkiaText.ResolveFont(null);
        var (w, h) = SkiaText.DrawOutlined(canvas,
            new List<string> { "Hi" }, 5, 5, face, 24,
            SKColors.White, SKColors.Black, SKColors.Black,
            SkiaText.HAlign.Left, 190, outline: false);
        Assert.True(w > 10 && h > 10);   // mesma extensão, só preenchimento
        bool any = false;
        foreach (var px in bmp.Pixels)
            if (px.Alpha > 10) { any = true; break; }
        Assert.True(any);
    }

    [Fact]
    public void Validate_Layer_Rect()
    {
        var mons = new List<(int, int, int, int)> { (0, 0, 1920, 1080) };
        // Indefinido → P-133 🔒.
        var d = LayerWindow.Validate(-1, -1, -1, -1, 1080, mons);
        Assert.Equal((20, 1080 - 300, 973, 192), d);
        // Fora de tudo → padrão.
        Assert.Equal(d, LayerWindow.Validate(5000, 0, 200, 100, 1080, mons));
        // Parcial → desloca para dentro (RF-041).
        var p = LayerWindow.Validate(-50, -20, 400, 200, 1080, mons);
        Assert.Equal((0, 0, 400, 200), p);
        // Dentro → igual.
        Assert.Equal((100, 100, 400, 200),
            LayerWindow.Validate(100, 100, 400, 200, 1080, mons));
    }

    [Fact]
    public void AutoFit_ShortText_KeepsFont()
    {
        var (w, h, pt) = LayerWindow.ComputeFit("Oi", 15, 1.0, 800, 600, 8);
        Assert.Equal(15, pt);                    // cabe: fonte intacta
        Assert.True(w <= 800 && h <= 600);
        Assert.True(w >= 200 && h >= 100);       // piso P-87/88
    }

    [Fact]
    public void AutoFit_TallText_ShrinksFontToMaxH()
    {
        string text = "Palavra " + string.Concat(System.Linq.Enumerable.Repeat("muito longa ", 60));
        var (w, h, pt) = LayerWindow.ComputeFit(text, 15, 1.0, 800, 120, 8);
        Assert.True(pt < 15);                    // estourou: encolheu
        Assert.True(pt >= 8);                    // nunca abaixo do mínimo
        Assert.True(h <= 120 || pt <= 8);        // cabe ou chegou ao piso
        Assert.True(w <= 800);
        // Piso configurável (legibilidade): com mínimo 12, nunca abaixo.
        var (_, _, pt12) = LayerWindow.ComputeFit(text, 15, 1.0, 800, 120, 12);
        Assert.True(pt12 >= 12);
    }

    [Fact]
    public void AutoFit_UnlimitedWidth_NoShrinkSideways()
    {
        var (w, h, pt) = LayerWindow.ComputeFit("Hello world", 15, 1.0, 0, 600, 8);
        Assert.Equal(15, pt);                    // sem teto lateral: sem quebra forçada
        Assert.True(h <= 600);
    }

    [Fact]
    public void AutoFit_EmptyText_FallsBackToMin()
    {
        var (w, h, pt) = LayerWindow.ComputeFit("  ", 15, 1.0, 800, 600, 8);
        Assert.Equal(15, pt);
        Assert.True(w >= 200 && h >= 100);
    }
}
