using System;
using System.Collections.Generic;
using Gort.Input;
using Gort.Platform;
using Gort.Platform.Cli;
using SkiaSharp;
using Xunit;

namespace Gort.Tests;

/// <summary>Camada multiplataforma: recorte, teclas, fábrica e ferramentas.</summary>
public class CrossPlatformTests
{
    private static SKBitmap Solid(int w, int h, byte r, byte g, byte b)
    {
        var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(r, g, b));
        return bmp;
    }

    [Fact]
    public void Crop_FullImage_Exact()
    {
        using var full = Solid(800, 600, 10, 20, 30);
        var virt = new ScreenRect(0, 0, 800, 600);
        var img = CliCapture.Crop(full, 0, new ScreenRect(0, 0, 800, 600), virt, true);
        Assert.NotNull(img);
        Assert.Equal(800, img.Width);
        Assert.Equal(600, img.Height);
        Assert.Equal(4, img.Channels);
        Assert.NotNull(img.OrigBytes);   // needOriginal clona
        // BGRA do sólido (10,20,30) + alfa opaco.
        Assert.Equal(30, img.Bytes[0]);
        Assert.Equal(20, img.Bytes[1]);
        Assert.Equal(10, img.Bytes[2]);
        Assert.Equal(255, img.Bytes[3]);
    }

    [Fact]
    public void Crop_SubRect_And_Retina2x()
    {
        using var full = Solid(800, 600, 1, 2, 3);
        var virt = new ScreenRect(0, 0, 800, 600);
        var img = CliCapture.Crop(full, 2, new ScreenRect(10, 20, 100, 50), virt, false);
        Assert.NotNull(img);
        Assert.Equal(2, img.Index);
        Assert.Equal(100, img.Width);
        Assert.Equal(50, img.Height);
        Assert.Null(img.OrigBytes);

        // Retina: imagem 2× o virtual → mesmo retângulo lógico vale.
        using var retina = Solid(1600, 1200, 7, 8, 9);
        var img2 = CliCapture.Crop(retina, 0, new ScreenRect(10, 20, 100, 50), virt, false);
        Assert.NotNull(img2);
        Assert.Equal(100, img2.Width);
        Assert.Equal(50, img2.Height);
        Assert.Equal(9, img2.Bytes[0]);
    }

    [Fact]
    public void Crop_OriginOffset_And_Empty()
    {
        using var full = Solid(400, 300, 5, 6, 7);
        // Monitores com origem negativa: deslocamento respeitado.
        var virt = new ScreenRect(-400, 0, 800, 300);
        var img = CliCapture.Crop(full, 0, new ScreenRect(-400, 0, 400, 300), virt, false);
        Assert.NotNull(img);
        Assert.Equal(400, img.Width);
    }

    [Fact]
    public void Intersect_Clips()
    {
        var v = new ScreenRect(0, 0, 800, 600);
        var c = CliCapture.Intersect(new ScreenRect(700, 500, 200, 200), v);
        Assert.Equal(new ScreenRect(700, 500, 100, 100), c);
        var o = CliCapture.Intersect(new ScreenRect(900, 0, 100, 100), v);
        Assert.True(o.IsEmpty);
    }

    [Fact]
    public void SharpHookKeys_Map()
    {
        Assert.Equal(0x41, SharpHookKeys.Map("VcA"));
        Assert.Equal(0x5A, SharpHookKeys.Map("VcZ"));
        Assert.Equal(0x35, SharpHookKeys.Map("Vc5"));
        Assert.Equal(0x70, SharpHookKeys.Map("VcF1"));
        Assert.Equal(0x10, SharpHookKeys.Map("VcLeftShift"));
        Assert.Equal(0x10, SharpHookKeys.Map("VcRightShift"));
        Assert.Equal(0x11, SharpHookKeys.Map("VcLeftControl"));
        Assert.Equal(0x12, SharpHookKeys.Map("VcRightAlt"));
        Assert.Equal(0x5B, SharpHookKeys.Map("VcLeftMeta"));
        Assert.Equal(0x2C, SharpHookKeys.Map("VcPrintScreen"));
        Assert.Equal(0x1B, SharpHookKeys.Map("VcEscape"));
        Assert.Equal(0x0D, SharpHookKeys.Map("VcEnter"));
        Assert.Equal(0x20, SharpHookKeys.Map("VcSpace"));
        Assert.Equal(0x25, SharpHookKeys.Map("VcLeft"));
        Assert.Equal(0, SharpHookKeys.Map("VcUndefined"));
        Assert.Equal(0, SharpHookKeys.Map(""));
    }

    [Fact]
    public void Factory_Current_Matches_Os()
    {
        var layer = PlatformFactory.Current;
        Assert.NotNull(layer);
        var report = layer.Report();
        Assert.NotNull(report.Platform);
        if (OperatingSystem.IsWindows()) Assert.Equal("windows", report.Platform);
        if (OperatingSystem.IsMacOS()) Assert.Equal("macos", report.Platform);
        if (OperatingSystem.IsLinux()) Assert.Equal("linux", report.Platform);
        Assert.NotNull(report.Notes);
    }

    [Fact]
    public void Concealer_Null_Never_Fails()
    {
        using var scope = PlatformFactory.CaptureConcealer.Conceal(
            new List<ScreenRect> { new(0, 0, 10, 10) });
        Assert.NotNull(scope);
    }

    [Fact]
    public void Shell_Which_Never_Throws()
    {
        // Existe ou não — o contrato é nunca lançar.
        Assert.Null(Record.Exception(() => Shell.Which("definitely-not-a-tool-xyz")));
        Assert.Null(Record.Exception(() => Shell.Which("dotnet")));
        Assert.Null(Shell.Which("definitely-not-a-tool-xyz"));
    }

    [Fact]
    public void Fonts_Fallback_NonEmpty_Per_Os()
    {
        Assert.NotEmpty(UI.SkiaText.FallbackFamilies);
        Assert.Contains("sans-serif", UI.SkiaText.FallbackFamilies);
    }

    [Fact]
    public void Speech_Availability_No_Throw()
    {
        using var s = new Audio.SpeechService();
        var _ = s.IsAvailable;   // true/false por SO — nunca exceção
        Assert.Null(Record.Exception(() => s.Speak("", true, "//////")));
        Assert.Null(Record.Exception(() => s.Speak("   ", false, "//////")));
    }
}
