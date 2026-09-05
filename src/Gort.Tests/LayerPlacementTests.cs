using System.Collections.Generic;
using Gort.Platform;
using Gort.UI;

namespace Gort.Tests;

/// <summary>Posição inicial da camada: fora, em cima ou embaixo — presa à tela.</summary>
public class LayerPlacementTests
{
    private static List<ScreenRect> Areas(params ScreenRect[] r) => new(r);

    private static readonly List<ScreenRect> Area1 =
        Areas(new ScreenRect(100, 100, 600, 200));

    [Fact]
    public void Top_SitsInsideCaptureTopLeft()
    {
        var (x, y) = TranslationWindows.ComputePlacement(
            "top", 200, 100, Area1, 0, 0, 1920, 1080);
        Assert.Equal(100, x);
        Assert.Equal(100, y);
    }

    [Fact]
    public void Bottom_SitsInsideCaptureBottomLeft()
    {
        var (x, y) = TranslationWindows.ComputePlacement(
            "bottom", 200, 100, Area1, 0, 0, 1920, 1080);
        Assert.Equal(100, x);
        Assert.Equal(200, y);   // 100 + 200 − 100
    }

    [Fact]
    public void Outside_PicksFirstFreeCorner()
    {
        var far = Areas(new ScreenRect(0, 0, 100, 100));
        var (x, y) = TranslationWindows.ComputePlacement(
            "outside", 200, 100, far, 0, 0, 1920, 1080);
        Assert.Equal(1920 - 200 - 12, x);   // inferior-direito
        Assert.Equal(1080 - 100 - 12, y);
    }

    [Fact]
    public void Outside_FullScreenCapture_FallsBackBottomRight()
    {
        var full = Areas(new ScreenRect(0, 0, 1920, 1080));
        var (x, y) = TranslationWindows.ComputePlacement(
            "outside", 200, 100, full, 0, 0, 1920, 1080);
        Assert.Equal(1920 - 200 - 12, x);
        Assert.Equal(1080 - 100 - 12, y);
    }

    [Fact]
    public void Top_ClampedToWorkArea()
    {
        var off = Areas(new ScreenRect(-500, -500, 600, 200));
        var (x, y) = TranslationWindows.ComputePlacement(
            "top", 200, 100, off, 0, 0, 1920, 1080);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Bottom_ClampedToWorkArea()
    {
        var low = Areas(new ScreenRect(100, 1000, 600, 200));
        var (x, y) = TranslationWindows.ComputePlacement(
            "bottom", 200, 300, low, 0, 0, 1920, 1080);
        Assert.Equal(100, x);
        Assert.Equal(1080 - 300, y);   // não passa da base
    }

    [Fact]
    public void Top_NoAreas_FallsBackOutside()
    {
        var (x, y) = TranslationWindows.ComputePlacement(
            "top", 200, 100, null, 0, 0, 1920, 1080);
        Assert.Equal(1920 - 200 - 12, x);
        Assert.Equal(1080 - 100 - 12, y);
    }

    [Fact]
    public void UnknownPlace_BehavesLikeOutside()
    {
        var (x, y) = TranslationWindows.ComputePlacement(
            "fantasia", 200, 100, Area1, 0, 0, 1920, 1080);
        // Fora não intersecta a área pequena: primeiro canto livre.
        Assert.Equal(1920 - 200 - 12, x);
        Assert.Equal(1080 - 100 - 12, y);
    }
}
