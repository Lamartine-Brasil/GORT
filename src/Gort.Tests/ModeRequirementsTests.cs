using System;
using System.Collections.Generic;
using Gort.Config;

namespace Gort.Tests;

/// <summary>Pré-requisitos por modo: auto-ajuste mínimo sem tocar no pessoal.</summary>
public class ModeRequirementsTests
{
    private static (bool Available, bool WordBoxes, bool PunctualOnly) Ok =>
        (true, true, false);

    private static Func<string, (bool, bool, bool)?> Caps(
        Dictionary<string, (bool, bool, bool)> table) =>
        id => table.TryGetValue(id, out var c) ? c : null;

    private static Dictionary<string, (bool, bool, bool)> AllRealtime() =>
        new()
        {
            ["modern"] = Ok,
            ["os"] = Ok,
            ["classic"] = Ok,
            ["venv"] = (true, false, false),
            ["cloud"] = (true, true, true),
        };

    [Fact]
    public void Overlay_EngineOk_NothingChanges()
    {
        var p = new Profile { WindowMode = "overlay", OcrEngine = "modern" };
        Assert.Null(ModeRequirements.EnsureForMode(p, Caps(AllRealtime())));
        Assert.Equal("modern", p.OcrEngine);
    }

    [Fact]
    public void Overlay_Venv_SwitchesToModern()
    {
        var p = new Profile { WindowMode = "overlay", OcrEngine = "venv" };
        string? notice = ModeRequirements.EnsureForMode(p, Caps(AllRealtime()));
        Assert.Equal("modern", p.OcrEngine);
        Assert.NotNull(notice);
        Assert.Contains("Sobreposição", notice);
    }

    [Fact]
    public void Replace_Cloud_SwitchesToModern()
    {
        var p = new Profile { WindowMode = "replace", OcrEngine = "cloud" };
        string? notice = ModeRequirements.EnsureForMode(p, Caps(AllRealtime()));
        Assert.Equal("modern", p.OcrEngine);
        Assert.NotNull(notice);
        Assert.Contains("Substituição", notice);
    }

    [Fact]
    public void Overlay_ModernMissing_FallsBackToClassic()
    {
        var table = AllRealtime();
        table["modern"] = (false, true, false);
        table["os"] = (false, true, false);
        var p = new Profile { WindowMode = "overlay", OcrEngine = "venv" };
        string? notice = ModeRequirements.EnsureForMode(p, Caps(table));
        Assert.Equal("classic", p.OcrEngine);
        Assert.NotNull(notice);
    }

    [Fact]
    public void Overlay_NoCapableEngine_KeepsEngine()
    {
        var table = new Dictionary<string, (bool, bool, bool)>
        {
            ["modern"] = (false, true, false),
            ["os"] = (false, true, false),
            ["classic"] = (false, true, false),
            ["venv"] = (true, false, false),
        };
        var p = new Profile { WindowMode = "overlay", OcrEngine = "venv" };
        Assert.Null(ModeRequirements.EnsureForMode(p, Caps(table)));
        Assert.Equal("venv", p.OcrEngine);
    }

    [Fact]
    public void DarkAndLayer_NeverTouchEngine()
    {
        foreach (string mode in new[] { "dark", "layer" })
        {
            var p = new Profile { WindowMode = mode, OcrEngine = "venv" };
            Assert.Null(ModeRequirements.EnsureForMode(p, Caps(AllRealtime())));
            Assert.Equal("venv", p.OcrEngine);
            var q = new Profile { WindowMode = mode, OcrEngine = "cloud" };
            Assert.Null(ModeRequirements.EnsureForMode(q, Caps(AllRealtime())));
            Assert.Equal("cloud", q.OcrEngine);
        }
    }

    [Fact]
    public void Overlay_UnknownEngine_SwitchesToModern()
    {
        var p = new Profile { WindowMode = "overlay", OcrEngine = "fantasia" };
        string? notice = ModeRequirements.EnsureForMode(p, Caps(AllRealtime()));
        Assert.Equal("modern", p.OcrEngine);
        Assert.NotNull(notice);
    }

    [Fact]
    public void Fix_PreservesPersonalSettings()
    {
        var p = new Profile
        {
            WindowMode = "overlay",
            OcrEngine = "venv",
            TextBackground = false,
            AutoFontSize = true,
            FontSize = 22,
            TextColor = new byte[] { 10, 20, 30 },
            OcrLanguage = "ja",
            TargetLanguage = "pt-BR",
            TranslationService = "db",
        };
        ModeRequirements.EnsureForMode(p, Caps(AllRealtime()));
        Assert.Equal("modern", p.OcrEngine);
        Assert.False(p.TextBackground);
        Assert.True(p.AutoFontSize);
        Assert.Equal(22, p.FontSize);
        Assert.Equal(new byte[] { 10, 20, 30 }, p.TextColor);
        Assert.Equal("ja", p.OcrLanguage);
        Assert.Equal("pt-BR", p.TargetLanguage);
        Assert.Equal("db", p.TranslationService);
    }
}
