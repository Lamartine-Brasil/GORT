using System.Collections.Generic;
using System.Threading;
using Gort.Config;
using Gort.Imaging;
using Gort.Platform;
using Gort.Regions;
using Gort.Store;

namespace Gort.Tests;

/// <summary>Critérios de aceite do capítulo 11 + geometria + filtro de cor.</summary>
public class RegionsTests
{
    private static RegionManager NewMgr()
    {
        var cfg = new ConfigService();
        cfg.Profile.ColorGroups = new List<ColorGroup> { new(), new(), new() };
        return new RegionManager(cfg);
    }

    private static ScreenRect R(int x, int y, int w, int h) => new(x, y, w, h);

    private static readonly List<(int R, int G, int B, int S1, int S2, int V1, int V2)> NoGroups = new();

    [Fact]
    public void Five_Areas_Remove_Third_Reindexes()
    {
        var m = NewMgr();
        for (int i = 0; i < 5; i++) m.AddArea(R(i * 10, 0, 100, 50), exclusion: false);
        Assert.Equal(5, m.Areas.Count);
        var third = m.Areas[2];
        m.RemoveAt(2);                                    // RF-064
        Assert.Equal(4, m.Areas.Count);
        Assert.DoesNotContain(third, m.Areas);
        // Índices = posição: antigas 4,5 agora em 2,3.
        Assert.Equal(30, m.Areas[2].Rect.X);
        Assert.Equal(40, m.Areas[3].Rect.X);
    }

    [Fact]
    public void Notify_Throttled_To_P13()
    {
        var m = NewMgr();
        int n = 0;
        m.Changed += () => n++;
        for (int i = 0; i < 50; i++) m.AddArea(R(0, 0, 100, 50), false);
        Assert.Equal(1, n);                               // RF-059: 1 a cada 0,3 s
        Thread.Sleep(350);
        m.AddArea(R(0, 0, 100, 50), false);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Notify_Gated_When_NotReady()
    {
        var m = NewMgr();
        int n = 0;
        m.Changed += () => n++;
        m.Applying = true;
        m.NotifyChanged(force: true);
        Assert.Equal(0, n);                               // RF-060
        m.Applying = false;
        m.Initialized = false;
        m.NotifyChanged(force: true);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Plan_Aligns_Width_And_Keeps_Negative_Origin()
    {
        var m = NewMgr();
        m.AddArea(R(-1920, 100, 301, 200), false);        // monitor à esquerda
        var plan = m.BuildPlan();
        Assert.Single(plan.Rects);
        Assert.Equal(-1920, plan.Rects[0].X);             // origem negativa preservada
        Assert.Equal(304, plan.Rects[0].W);               // RF-077: múltiplo de 4
    }

    [Fact]
    public void Snapshot_Replaces_All()
    {
        var m = NewMgr();
        m.AddArea(R(0, 0, 100, 50), false);
        m.AddArea(R(0, 60, 100, 50), false);
        m.SetQuick(R(500, 500, 60, 60));
        m.SetSnapshot(R(10, 10, 200, 100));
        var plan = m.BuildPlan();
        Assert.True(plan.IsSnapshot);
        Assert.Single(plan.Rects);
        Assert.Equal(10, plan.Rects[0].X);
        Assert.NotNull(m.LastSnapshot);                   // RF-070 memoriza
        m.BeginNonSnapshotTranslation();
        Assert.Null(m.LastSnapshot);                      // RF-071 apaga
    }

    [Fact]
    public void Quick_Appended_But_Not_Persisted()
    {
        var cfg = new ConfigService();
        cfg.Profile.ColorGroups = new List<ColorGroup> { new() };
        var m = new RegionManager(cfg);
        m.AddArea(R(0, 0, 100, 50), false);
        m.SetQuick(R(500, 500, 64, 64));                  // RF-069: imediata
        var plan = m.BuildPlan();
        Assert.Equal(2, plan.Rects.Count);                // N normais + N rápida
        Assert.Single(m.Areas);                           // runtime tem a normal…
        Assert.Empty(cfg.Profile.Areas);                  // …mas o perfil não (não persiste)
    }

    [Fact]
    public void Temp_Edit_Apply_Revert()
    {
        var m = NewMgr();
        m.AddArea(R(0, 0, 100, 50), false);
        m.BeginManage();
        m.AddArea(R(200, 0, 100, 50), true);              // exclusão temporária
        Assert.Single(m.WorkingExcl);
        Assert.Empty(m.Exclusions);
        m.CancelManage();                                 // RF-061: reverte
        Assert.Empty(m.WorkingExcl);
        m.BeginManage();
        m.AddArea(R(200, 0, 100, 50), true);
        m.ApplyWorking();                                 // promove sem gravar
        Assert.Single(m.Exclusions);
    }

    [Fact]
    public void Frame_Capture_Roundtrip()
    {
        // Moldura (100,120) 300×100, escala 1: captura (111,140) 278×69.
        var cap = FrameGeometry.FrameToCapture(100, 120, 300, 100, 1.0);
        Assert.Equal(new ScreenRect(111, 140, 278, 69), cap);   // RF-073
        var (x, y, w, h) = FrameGeometry.CaptureToFrame(cap, 1.0);
        Assert.Equal(100, x); Assert.Equal(120, y);
        Assert.Equal(300, w); Assert.Equal(100, h);
        // Degenerada → mínimo 1 px.
        var tiny = FrameGeometry.FrameToCapture(0, 0, 5, 5, 1.0);
        Assert.True(tiny.W >= 1 && tiny.H >= 1);
    }

    [Fact]
    public void Align_And_Click_Rules()
    {
        Assert.Equal(4, FrameGeometry.AlignWidth(1));
        Assert.Equal(4, FrameGeometry.AlignWidth(4));
        Assert.Equal(8, FrameGeometry.AlignWidth(5));
        Assert.True(FrameGeometry.IsAccidentalClick(4, 100));    // RF-052
        Assert.True(FrameGeometry.IsAccidentalClick(100, 4));
        Assert.False(FrameGeometry.IsAccidentalClick(5, 5));
    }

    [Fact]
    public void Rgb_Filter_Exact_Or()
    {
        var groups = new List<(int, int, int, int, int, int, int)> { (255, 0, 0, 0, 0, 0, 0) };
        Assert.True(ColorFilter.Passes(255, 0, 0, FilterMode.Rgb, groups, 127));
        Assert.False(ColorFilter.Passes(254, 0, 0, FilterMode.Rgb, groups, 127));
        Assert.False(ColorFilter.Passes(255, 0, 0, FilterMode.None, NoGroups, 127) == false);
    }

    [Fact]
    public void Hsv_Filter_Light_And_Dark_Presets()
    {
        // Assistente: texto claro S 0–10, V 75–100 (P-28 🔒).
        var light = new List<(int, int, int, int, int, int, int)> { (0, 0, 0, 0, 10, 75, 100) };
        Assert.True(ColorFilter.Passes(255, 255, 255, FilterMode.Hsv, light, 127));
        Assert.False(ColorFilter.Passes(0, 0, 0, FilterMode.Hsv, light, 127));
        // Texto escuro faixa 1: S 0–8, V 0–32 (P-26 🔒).
        var dark = new List<(int, int, int, int, int, int, int)> { (0, 0, 0, 0, 8, 0, 32) };
        Assert.True(ColorFilter.Passes(20, 20, 20, FilterMode.Hsv, dark, 127));
        Assert.False(ColorFilter.Passes(255, 255, 255, FilterMode.Hsv, dark, 127));
    }

    [Fact]
    public void Hsv_And_Gray_Math()
    {
        var (h, s, v) = ColorFilter.RgbToHsv(255, 0, 0);
        Assert.Equal(0, (int)h); Assert.Equal(255, s); Assert.Equal(255, v);
        var (_, s0, v0) = ColorFilter.RgbToHsv(0, 0, 0);
        Assert.Equal(0, s0); Assert.Equal(0, v0);
        Assert.Equal(255, ColorFilter.Gray(255, 255, 255));
        Assert.Equal(0, ColorFilter.Gray(0, 0, 0));
        Assert.True(ColorFilter.Passes(0, 0, 0, FilterMode.Threshold, NoGroups, 127));
        Assert.False(ColorFilter.Passes(255, 255, 255, FilterMode.Threshold, NoGroups, 127));
    }

    [Fact]
    public void Binarize_Maps_Pass_To_Black()
    {
        var bgra = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 };  // preto, branco
        var out8 = ColorFilter.Binarize(bgra, 2, 1, FilterMode.Threshold, NoGroups, 127);
        Assert.Equal(0, out8[0]);     // RF-082: passa → preto
        Assert.Equal(255, out8[1]);
    }

    [Fact]
    public void Remove_Group_Renumbers()
    {
        var m = NewMgr();                                 // 3 grupos
        var a = m.AddArea(R(0, 0, 100, 50), false);
        a.Groups.Clear(); a.Groups.AddRange(new[] { 0, 1, 2 });
        m.RemoveColorGroup(1);                            // RF-079
        Assert.Equal(new List<int> { 0, 1 }, a.Groups);   // antigo 2 → 1
        m.AddColorGroup();                                // RF-079: todas incluem
        Assert.Contains(2, a.Groups);
    }

    [Fact]
    public void Single_Group_Cannot_Be_Removed()
    {
        var cfg = new ConfigService();
        cfg.Profile.ColorGroups = new List<ColorGroup> { new() };
        var m = new RegionManager(cfg);
        m.RemoveColorGroup(0);
        Assert.Single(cfg.Profile.ColorGroups);
    }

    [Fact]
    public void Areas_Persist_Roundtrip()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            System.IO.Path.GetRandomFileName() + ".toml");
        var cfg = new ConfigService();
        cfg.Profile.ColorGroups = new List<ColorGroup> { new(), new() };
        cfg.Profile.Areas.Add(new OcrArea { X = -10, Y = 20, W = 300, H = 100, ColorGroups = new() { 1 } });
        cfg.Profile.Exclusions.Add(new ExclusionArea { X = 0, Y = 0, W = 10, H = 10 });
        cfg.SaveProfileTo(path);

        var cfg2 = new ConfigService();
        cfg2.LoadProfile(path, isMain: false);
        var m = new RegionManager(cfg2);
        m.LoadFromProfile();                                   // RF-040/RF-066
        Assert.Single(m.Areas);
        Assert.Equal(-10, m.Areas[0].Rect.X);
        Assert.Equal(new List<int> { 1 }, m.Areas[0].Groups);
        Assert.Single(m.Exclusions);
    }

    [Fact]
    public void CanTranslate_Requires_Area()
    {
        var m = NewMgr();
        Assert.False(m.CanTranslate(out var msg));             // RF-065
        Assert.NotEmpty(msg);
        m.AddArea(R(0, 0, 100, 50), false);
        Assert.True(m.CanTranslate(out _));
    }

    [Fact]
    public void Validate_Finds_Outside()
    {
        var m = NewMgr();
        m.AddArea(R(0, 0, 100, 50), false);
        m.AddArea(R(99999, 0, 100, 50), false);
        var bad = m.ValidateAgainst(R(0, 0, 1920, 1080));      // RF-086
        Assert.Equal(new List<int> { 1 }, bad);
    }
}
