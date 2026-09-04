using System;
using System.Collections.Generic;
using System.Threading;
using Gort.Core;
using Gort.Lifecycle;
using Gort.Loop;
using Gort.Platform;

namespace Gort.Tests;

/// <summary>Etapa 8 — laço, controle (RF-009..014) e mudança (RF-192..205).</summary>
public class LoopTests
{
    private sealed class FakeBody : ILoopBody
    {
        public Func<LoopContext, bool> OnStep = _ => true;
        public int Begins;
        public int Steps;
        public int WorkerThread = -1;
        public void Begin(LoopMode mode) => Begins++;
        public bool Step(LoopContext ctx)
        {
            Steps++;
            WorkerThread = Thread.CurrentThread.ManagedThreadId;
            return OnStep(ctx);
        }
    }

    private static bool WaitFor(Func<bool> cond, int ms = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            if (cond()) return true;
            Thread.Sleep(20);
        }
        return cond();
    }

    // ---- ChangeTracker (RF-192..200) ----

    [Fact]
    public void Change_Full_Idle_And_Restart()
    {
        var t = new ChangeTracker();
        var now = DateTime.UtcNow;
        var d1 = t.Step("hello", now, overlayOrLayer: true);
        Assert.True(d1.FullPath);                       // diferente → completo
        Assert.Equal("hello", t.Previous);              // RF-198 atualiza
        var d2 = t.Step("hello", now, overlayOrLayer: true);
        Assert.False(d2.FullPath);                      // RF-195: igual, nada
        Assert.False(d2.RepaintIdle);
        var d3 = t.Step("hello", now.AddMilliseconds(Params.P47_IdleRepaintMs + 1), true);
        Assert.True(d3.RepaintIdle);                    // RF-196 🔒
        var d4 = t.Step("hello", now.AddHours(1), overlayOrLayer: false);
        Assert.False(d4.RepaintIdle);                   // escuro não repinta
        var d5 = t.Step("", now, overlayOrLayer: false);
        Assert.True(d5.FullPath);                       // RF-194: vazio = mudança
        var fresh = new ChangeTracker();                // RF-199: recomeça vazio
        Assert.True(fresh.Step("hello", now, true).FullPath);
    }

    // ---- OverlayReuseCache (RF-203/204) ----

    [Fact]
    public void Reuse_Identical_Prune_Missing()
    {
        var c = new OverlayReuseCache();
        var area = new ScreenRect(0, 0, 100, 50);
        var client = new ScreenRect(0, 0, 100, 50);
        Assert.False(c.ReuseOrStore(0, area, client, "ocr", "tr"));
        Assert.True(c.ReuseOrStore(0, area, client, "ocr", "tr"));   // RF-203
        Assert.False(c.ReuseOrStore(0, area, client, "ocr2", "tr")); // mudou
        Assert.True(c.ReuseOrStore(0, area, client, "ocr2", "tr"));
        c.ReuseOrStore(1, area, client, "x", "y");
        c.Prune(new HashSet<int> { 0 });                             // RF-204
        Assert.Equal(1, c.Count);
    }

    // ---- controlador ----

    [Fact]
    public void Start_Runs_On_Dedicated_Thread_And_Stops()
    {
        var ctl = new TranslationController();
        var body = new FakeBody();
        body.OnStep = ctx =>
        {
            if (ctx.StopRequested) return false;
            Thread.Sleep(1);
            return true;
        };
        Assert.True(ctl.StartLoop(body, LoopMode.Continuous));
        Assert.Equal(LoopState.Running, ctl.State);
        Assert.True(WaitFor(() => body.Steps > 0));
        Assert.NotEqual(Thread.CurrentThread.ManagedThreadId, body.WorkerThread);  // RF-009
        Assert.True(ctl.RequestStop(5000));
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle));
    }

    [Fact]
    public void Stop_Timeout_Keeps_Flag_And_Late_Finish_Idles()
    {
        var ctl = new TranslationController();
        var body = new FakeBody();
        var release = new ManualResetEventSlim();
        body.OnStep = _ => { release.Wait(); return true; };   // preso até liberar
        Assert.True(ctl.StartLoop(body, LoopMode.Continuous));
        Assert.True(WaitFor(() => body.Steps > 0));
        Assert.False(ctl.RequestStop(100));                       // RF-010: estoura
        Assert.Equal(LoopState.Stopping, ctl.State);              // flag mantida
        release.Set();                                            // termina depois
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle, 8000));
    }

    [Fact]
    public void Apply_Idle_Running_And_Timeout()
    {
        var ctl = new TranslationController();
        bool applied = false;
        Assert.True(ctl.ApplyChange(() => applied = true, 1000));  // ocioso: direto
        Assert.True(applied);

        var body = new FakeBody();
        body.OnStep = ctx => { Thread.Sleep(20); return !ctx.StopRequested; };
        Assert.True(ctl.StartLoop(body, LoopMode.Continuous));
        Assert.True(WaitFor(() => body.Steps > 0));
        int begins = body.Begins;
        applied = false;
        Assert.True(ctl.ApplyChange(() => applied = true, 5000));  // RF-012
        Assert.True(applied);
        Assert.True(body.Begins > begins);                          // retomou
        Assert.True(ctl.RequestStop(5000));

        var stuck = new FakeBody();
        var release = new ManualResetEventSlim();
        stuck.OnStep = _ => { release.Wait(); return true; };
        Assert.True(ctl.StartLoop(stuck, LoopMode.Continuous));
        Assert.True(WaitFor(() => stuck.Steps > 0));
        applied = false;
        Assert.False(ctl.ApplyChange(() => applied = true, 100));  // RF-012: aborta
        Assert.False(applied);
        release.Set();
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle, 8000));
    }

    [Fact]
    public void Start_Refused_When_Previous_Wont_Die()
    {
        var ctl = new TranslationController();
        var stuck = new FakeBody();
        var release = new ManualResetEventSlim();
        stuck.OnStep = _ => { release.Wait(); return true; };
        Assert.True(ctl.StartLoop(stuck, LoopMode.Continuous));
        Assert.True(WaitFor(() => stuck.Steps > 0));
        var other = new FakeBody();
        Assert.False(ctl.StartLoop(other, LoopMode.Continuous));   // RF-013
        Assert.Equal(0, other.Begins);
        release.Set();
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle, 8000));
    }

    [Fact]
    public void Rapid_Toggles_Stay_Single_Threaded()
    {
        var ctl = new TranslationController();                     // 20× em 100 ms
        int concurrent = 0, maxConcurrent = 0;
        var body = new FakeBody();
        body.OnStep = ctx =>
        {
            int c = Interlocked.Increment(ref concurrent);
            int init, cmp;
            do { init = maxConcurrent; cmp = Math.Max(init, c); }
            while (Interlocked.CompareExchange(ref maxConcurrent, cmp, init) != init);
            Thread.Sleep(5);
            Interlocked.Decrement(ref concurrent);
            return !ctx.StopRequested;
        };
        for (int i = 0; i < 20; i++)
        {
            ctl.StartLoop(body, LoopMode.Continuous);
            Thread.Sleep(10);
            ctl.RequestStop(2000);
        }
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle, 10000));
        Assert.Equal(1, maxConcurrent);
    }

    [Fact]
    public void Loop_Error_Reported_And_Ends_Clean()
    {
        var ctl = new TranslationController();
        Exception? seen = null;
        ctl.LoopError += ex => seen = ex;
        var body = new FakeBody();
        body.OnStep = _ => throw new InvalidOperationException("boom");  // RF-014
        Assert.True(ctl.StartLoop(body, LoopMode.Continuous));
        Assert.True(WaitFor(() => seen is not null));
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle));
    }

    [Fact]
    public void Once_Ends_After_One_Cycle()
    {
        var ctl = new TranslationController();
        var body = new FakeBody();   // sempre true, mas pontual encerra (RF-202)
        Assert.True(ctl.StartLoop(body, LoopMode.Once));
        Assert.True(WaitFor(() => ctl.State == LoopState.Idle));
        Assert.Equal(1, body.Steps);
    }

    // ---- descarte por imagem (economia de CPU) ----

    [Fact]
    public void ImageHash_Stable_And_Sensitive()
    {
        var a = new byte[] { 1, 2, 3, 4 };
        var b = new byte[] { 1, 2, 3, 4 };
        var c = new byte[] { 1, 2, 3, 5 };
        Assert.Equal(TranslationLoop.ImageHash(a), TranslationLoop.ImageHash(b));
        Assert.NotEqual(TranslationLoop.ImageHash(a), TranslationLoop.ImageHash(c));
        Assert.Equal(TranslationLoop.ImageHash(Array.Empty<byte>()),
            TranslationLoop.ImageHash(Array.Empty<byte>()));
    }

    [Fact]
    public void Fingerprint_Changes_With_Config()
    {
        var cfg = new Gort.Store.ConfigService();
        var mgr = new Gort.Regions.RegionManager(cfg);
        int F() => TranslationLoop.Fingerprint(cfg.Profile, mgr.BuildPlan(),
            false, cfg.Advanced, false, false);
        int f1 = F();
        Assert.Equal(f1, F());
        cfg.Profile.Zoom = 3.0;   // qualquer mudança invalida os hashes
        Assert.NotEqual(f1, F());
        cfg.Profile.Zoom = 2.0;
        cfg.Profile.UseDict = !cfg.Profile.UseDict;   // dicionário também muda o tratado
        Assert.NotEqual(f1, F());
    }
}
