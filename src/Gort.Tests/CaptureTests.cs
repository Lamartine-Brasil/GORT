using System;
using Gort.Platform;
using Xunit.Abstractions;

namespace Gort.Tests;

/// <summary>
/// Etapa 2 — como testar: capturar uma região em cada monitor, incluindo
/// coordenadas negativas, e conferir o conteúdo (artefatos BMP em %TEMP%).
/// </summary>
public class CaptureTests
{
    private readonly ITestOutputHelper _out;
    public CaptureTests(ITestOutputHelper @out) => _out = @out;

    private bool RequireWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            _out.WriteLine("SKIP: captura GDI só no Windows.");
            return false;
        }
        return true;
    }

    [Fact]
    public void Capabilities_Report_Windows()
    {
        if (!RequireWindows()) return;
        var rep = PlatformFactory.Current.Report();
        Assert.Equal("windows", rep.Platform);
        Assert.True(rep.ScreenCapture);       // C1
        Assert.True(rep.FrameBounds);         // C4
        Assert.True(rep.ForegroundInfo);      // C12
        Assert.False(rep.AttachedCapture);    // Etapa 16
        Assert.False(rep.GlobalHotkey);       // Etapa 9
    }

    [Fact]
    public void Capture_Each_Monitor()
    {
        if (!RequireWindows()) return;
        var cap = PlatformFactory.Current.Capture;
        var mons = cap.GetMonitors();
        Assert.NotEmpty(mons);
        _out.WriteLine($"Virtual: {cap.VirtualScreen} | monitores: {mons.Count}");
        int i = 0;
        foreach (var m in mons)
        {
            _out.WriteLine($"monitor {i}: {m.Bounds} escala {m.Scale}");
            // Amostra 320x200 no canto do monitor (pode ter origem negativa).
            var img = cap.CaptureRect(i, new ScreenRect(m.Bounds.X, m.Bounds.Y, 320, 200), false);
            Assert.NotNull(img);
            Assert.Equal(320, img!.Width);
            Assert.Equal(200, img.Height);
            Assert.Equal(4, img.Channels);
            Assert.Equal(320 * 200 * 4, img.Bytes.Length);
            _out.WriteLine("  bmp: " + BmpWriter.Save(img, $"gort-cap-mon{i}.bmp"));
            i++;
        }
    }

    [Fact]
    public void Capture_Negative_And_Outside()
    {
        if (!RequireWindows()) return;
        var cap = PlatformFactory.Current.Capture;
        // Totalmente fora de qualquer monitor → sem imagem, sem erro (6.2).
        var outside = cap.CaptureRect(0, new ScreenRect(-100000, -100000, 64, 64), false);
        Assert.Null(outside);

        // Estradulando a borda da área virtual → recortado, não falha.
        var v = cap.VirtualScreen;
        var edge = cap.CaptureRect(0, new ScreenRect(v.X - 40, v.Y - 40, 120, 120), false);
        Assert.NotNull(edge);
        Assert.Equal(80, edge!.Width);
        Assert.Equal(80, edge.Height);
        _out.WriteLine("bmp borda: " + BmpWriter.Save(edge, "gort-cap-borda.bmp"));
    }

    [Fact]
    public void Capture_With_Original_Variant()
    {
        if (!RequireWindows()) return;
        var cap = PlatformFactory.Current.Capture;
        var v = cap.VirtualScreen;
        var img = cap.CaptureRect(0, new ScreenRect(v.X, v.Y, 160, 100), needOriginal: true);
        Assert.NotNull(img);
        Assert.NotNull(img!.OrigBytes);
        Assert.Equal(img.Bytes.Length, img.OrigBytes!.Length);
    }

    [Fact]
    public void Foreground_Window_Info()
    {
        if (!RequireWindows()) return;
        var ws = PlatformFactory.Current.Windows;
        Assert.True(ws.IsAvailable);
        var fg = ws.ForegroundWindow();
        Assert.NotNull(fg);   // no Windows com janela ativa há sempre uma
        Assert.NotEmpty(ws.ListCapturableWindows());
    }
}
