using System;
using System.Collections.Generic;
using System.Threading;
using Gort.Audio;
using Gort.Config;
using Gort.Loop;
using Gort.Platform;
using Gort.Regions;
using Gort.Store;

namespace Gort.Tests;

public class Aux16Tests
{
    [Fact]
    public void Attached_Buffer_Ring_And_Freshness()
    {
        var now = DateTime.UtcNow;
        var buf = new Platform.Windows.AttachedBuffer(() => now);
        for (int i = 0; i < 6; i++)
            buf.Push(new byte[] { (byte)i }, 1, 1);
        Assert.Equal(5, buf.Count);                        // RF-093: P-17
        var fresh = buf.Fresh();
        Assert.NotNull(fresh);
        Assert.Equal(5, fresh.Value.Bytes[0]);             // último válido
        now = now.AddSeconds(1);                           // > P-19 (0,1 s)
        Assert.Null(buf.Fresh());                          // RF-095: expirou
        buf.Clear();
        Assert.Null(buf.Fresh());
    }

    [Fact]
    public void Follow_Centers_And_Throttles()
    {
        var cfg = new ConfigService();
        cfg.Profile.ColorGroups = new List<ColorGroup> { new() };
        var mgr = new RegionManager(cfg);
        int cx = 500, cy = 300;
        var svc = new FollowMouseService(mgr, _ => 1.0, () => false,
            () => (cx, cy), startTimer: false);
        bool needArea = false;
        svc.NeedsArea += () => needArea = true;
        svc.SetActive(true, compat: false);
        Assert.True(needArea);                             // RF-458
        svc.SetArea(new ScreenRect(0, 0, 100, 60));
        Assert.True(mgr.FollowActive);
        svc.Tick();
        // RF-456: x = 500−11−50, y = 300−20−30 (cromo escala 1).
        Assert.Equal(new ScreenRect(439, 250, 100, 60), mgr.FollowArea);
        var first = mgr.FollowArea;
        cx = 501;
        svc.Tick();                                        // < P-123: mantém
        Assert.Equal(first, mgr.FollowArea);               // RF-457/P-123
        Thread.Sleep(120);
        svc.Tick();
        Assert.NotEqual(first, mgr.FollowArea);
        svc.DestroyArea();                                 // RF-462
        Assert.False(mgr.FollowActive);
    }

    [Fact]
    public void Clipboard_Gating()
    {
        bool enabled = true, idle = true, overlay = false, busy = false;
        var w = new Clipboard.ClipboardWatcher(
            () => enabled, () => idle, () => overlay, () => busy,
            _ => false, () => false,
            _ => System.Threading.Tasks.Task.FromResult(""),
            _ => { }, _ => { });
        Assert.True(w.ShouldTranslate("novo"));            // RF-467
        enabled = false;
        Assert.False(w.ShouldTranslate("outro"));
        enabled = true; idle = false;
        Assert.False(w.ShouldTranslate("outro"));
        idle = true; overlay = true;
        Assert.False(w.ShouldTranslate("outro"));          // RF-467: sem overlay
        overlay = false; busy = true;
        Assert.False(w.ShouldTranslate("outro"));
        Assert.False(w.ShouldTranslate(""));               // RF-466
        Assert.False(w.ShouldTranslate(null));
    }

    [Fact]
    public void CopyOut_Formats()
    {
        var cfg = new ConfigService();
        var dm = new Translate.DisplayMemory(() => 5, () => 10);
        var fx = new RealEffects(dm, cfg, new SpeechService());
        var p = cfg.Profile;
        p.CopyToClipboard = true;
        p.CopyFormat = "both";
        fx.CopyOut("TR", "OCR", p);                        // RF-473: ambos
        p.CopyFormat = "translation-only";
        fx.CopyOut("TR", "OCR", p);
        p.CopyToClipboard = false;
        fx.CopyOut("TR", "OCR", p);
    }

    [Fact]
    public void Speech_Strips_Token_Without_Throw()
    {
        using var speech = new SpeechService();
        _ = speech.IsAvailable;                            // RF-480: sem erro
        speech.Speak("//////olá", waitPrevious: true, "//////");  // RF-477/478
        speech.Speak("", waitPrevious: false, "//////");
    }
}
