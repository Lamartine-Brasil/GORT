using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Gort.Lifecycle;
using Gort.Store;

namespace Gort.Tests;

// xUnit1031 suprimido neste arquivo (intencional): o Dispatch headless
// precisa concluir antes do fim do teste e o xUnit não tem
// SynchronizationContext — sem risco de deadlock. O bloqueio espelha o
// laço de tradução, que é síncrono de ponta a ponta (RF-009).
#pragma warning disable xUnit1031 // blocking GetResult em teste headless

/// <summary>
/// Renders headless de todas as janelas/abas para PNG (inspeção visual).
/// Não afirma layout — apenas que tudo constrói, abre e renderiza sem
/// exceção. Os PNGs vão para releases/visual-tests (nunca para dentro
/// de src/ nem para o TEMP). Toda construção de Window acontece dentro
/// do Dispatch (thread da UI).
/// </summary>
[Collection("visual")]
public class VisualRenderTests
{
    // Raiz do repo = ancestral que contém src/Gort.sln, partindo da pasta
    // do assembly de teste. Com fallback para o TEMP se não achar.
    internal static string OutDir
    {
        get
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null
                && !File.Exists(Path.Combine(dir, "src", "Gort.sln")))
                dir = Directory.GetParent(dir)?.FullName;
            dir ??= Path.GetTempPath();
            return Path.Combine(dir, "releases", "visual-tests");
        }
    }

    private static void Shot(Func<Window> make, string name, Action<Window>? setup = null)
    {
        // Dispatch devolve Task — é preciso aguardar, senão o teste termina
        // antes da renderização.
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            // O app real usa FluentTheme (App.axaml) — sem ele os controles
            // renderizam sem template (abas invisíveis).
            var app = Avalonia.Application.Current!;
            if (!app.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().Any())
                app.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            var w = make();
            try
            {
                setup?.Invoke(w);
                w.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var bmp = w.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                string path = Path.Combine(OutDir, name + ".png");
                bmp.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                Console.WriteLine($"SHOT {path} {bmp.PixelSize}");
            }
            finally { w.Close(); }   // falha no meio não vaza a janela na sessão
        }, default).GetAwaiter().GetResult();
    }

    [Fact]
    public void Render_MainWindow_AllTabs()
    {
        var cfg = new ConfigService();
        string[] tabs = ["translate", "read", "dict", "display", "advanced", "system"];
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            Shot(() => new Gort.MainWindow(cfg, new TranslationController(), null),
                $"main-{idx}-{tabs[idx]}",
                w => w.FindControl<TabControl>("Tabs")!.SelectedIndex = idx);
        }
    }

    [Fact]
    public void Render_Narrow()
    {
        // Largura mínima (880): nada pode estourar para fora — WrapPanels
        // quebram linha em vez de cortar.
        var cfg = new ConfigService();
        Shot(() => new Gort.MainWindow(cfg, new TranslationController(), null),
            "narrow-translate",
            w =>
            {
                w.FindControl<TabControl>("Tabs")!.SelectedIndex = 0;
                w.Width = 880;
                w.Height = 680;
            });
        Shot(() => new Gort.MainWindow(cfg, new TranslationController(), null),
            "narrow-system",
            w =>
            {
                w.FindControl<TabControl>("Tabs")!.SelectedIndex = 5;
                w.Width = 880;
                w.Height = 680;
            });
    }

    /// <summary>
    /// Hospedeiro mínimo do controle remoto (UI.IRemoteHost): sem App real,
    /// sem rede, sem laço — só o suficiente para construir e renderizar.
    /// </summary>
    private sealed class StubRemoteHost : Gort.UI.IRemoteHost
    {
        public bool RemoteAlwaysOnTop => false;
        public Gort.Lifecycle.LoopState LoopState => Gort.Lifecycle.LoopState.Idle;
        public Gort.UI.HotkeyActions? HotkeyActions => null;
        public void OpenAreas() { }
        public void ToggleLoop() { }
        public void ShowMain() { }
    }

    [Fact]
    public void Render_AuxWindows()
    {
        var cfg = new ConfigService();
        Shot(() => new Gort.UI.SplashWindow("9.9.9", "2026-09-04"), "splash");
        Shot(() => new Gort.UI.AboutWindow("9.9.9", "2026-09-04", "ja=3"), "about");
        Shot(() => new Gort.UI.DictEditorWindow("texto reconhecido",
            Path.Combine(Path.GetTempPath(), "gort-dict-test.txt")), "dict-editor");
        Shot(() => new Gort.UI.KeyManagerWindow("commercial-kr", "Chaves KR"), "key-manager");
        Shot(() => new Gort.UI.AdvancedWindow(cfg), "advanced");
        Shot(() => new Gort.UI.CommunityWindow(), "community");
        Shot(() => new Gort.UI.VenvInstallWindow(), "venv-install");
        Shot(() => new Gort.UI.AttachedPickerWindow(), "attached-picker");
        Shot(() => new Gort.UI.ColorGroupsWindow("Área 1",
            new System.Collections.Generic.List<Gort.Config.ColorGroup>(),
            new System.Collections.Generic.List<int>()), "color-groups");
        // Controle remoto com hospedeiro de teste (RF-517): prova os rótulos
        // PT-BR (Área/Rápida/Instantâneo/▶/Config/×) sem o App real.
        Shot(() => new Gort.UI.RemoteWindow(new StubRemoteHost()), "remote");
    }

    [Fact]
    public void Render_TranslationWindows()
    {
        var cfg = new ConfigService();
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var dark = new Gort.UI.DarkWindow();
            try
            {
                // Ordem de produção (DarkSink): texto primeiro (Show interno).
                dark.ShowTranslation(
                    "Linha traduzida 1\nLinha traduzida 2", "OCR: Hello", true, false);
                for (int i = 0; i < 10; i++)
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var bmp = dark.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                AssertPainted(bmp);   // texto branco representa tinta real
                bmp.Save(Path.Combine(OutDir, "dark.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            finally { dark.Close(); }
        }, default).GetAwaiter().GetResult();
        Shot(() => new Gort.UI.LayerWindow(cfg), "layer",
            w => ((Gort.UI.LayerWindow)w).SetText(
                "Texto da camada com contorno duplo para leitura sobre o jogo."));
        Shot(() => new Gort.UI.OverlayWindow(cfg, _ => 1.0), "overlay",
            w => ((Gort.UI.OverlayWindow)w).ApplyRunning(true));
        // Fase 2: modo novo usa a mesma janela com Substitute ligado.
        Shot(() => new Gort.UI.OverlayWindow(cfg, _ => 1.0), "replace",
            w =>
            {
                var o = (Gort.UI.OverlayWindow)w;
                o.Substitute = true;
                o.ApplyRunning(true);
                Assert.True(o.Substitute);
            });
    }

    /// <summary>
    /// O bitmap da sobreposição precisa ter pixels visíveis (texto/fundo),
    /// não apenas o fundo transparente. Amostra a região do bloco em grade
    /// de 7 px e exige volume mínimo de tinta — pega regressão de fonte
    /// minúscula (ex.: FindFont encolhendo ao piso sem motivo).
    /// </summary>
    private static void AssertPainted(Avalonia.Media.Imaging.Bitmap src)
    {
        var px = src.PixelSize;
        Assert.True(px.Width > 0 && px.Height > 0);
        // Via SkiaSharp: conta pixels com alfa significativo na grade.
        using var ms = new System.IO.MemoryStream();
        src.Save(ms, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        ms.Position = 0;
        using var bmp = SkiaSharp.SKBitmap.Decode(ms);
        Assert.NotNull(bmp);
        using var bgra = bmp.Copy(SkiaSharp.SKColorType.Bgra8888);
        Assert.NotNull(bgra);
        nint pixels = bgra.GetPixels();
        Assert.True(pixels != nint.Zero);
        int hits = 0;
        unsafe
        {
            byte* p = (byte*)pixels.ToPointer();
            int stride = bgra.RowBytes;
            for (int y = 0; y < bgra.Height; y += 7)
                for (int x = 0; x < bgra.Width; x += 7)
                {
                    if (*(p + y * stride + x * 4 + 3) > 20) hits++;
                }
        }
        // "Olá, mundo traduzido!" a ~28 px cobre centenas de células da
        // grade; 10 é piso generoso só contra colapso patológico.
        Assert.True(hits >= 10, $"pouca tinta na sobreposição: {hits} células");
    }

    [Fact]
    public void Render_RegionWindows()
    {
        var cfg = new ConfigService();
        var mgr = new Gort.Regions.RegionManager(cfg);
        Shot(() => new Gort.UI.AreasWindow(cfg, mgr), "areas");
        Shot(() => new Gort.UI.SelectionWindow(
            new Gort.Platform.ScreenRect(0, 0, 1280, 720), 1.0, "#FFFFFFFF", "#FF000000"), "selection");

        // Arrasto a meio caminho: verifica o elástico (fill + borda verde).
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var sel = new Gort.UI.SelectionWindow(
                new Gort.Platform.ScreenRect(0, 0, 1280, 720), 1.0, "#FFFFFFFF", "#FF000000");
            try
            {
                sel.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                sel.MouseDown(new Avalonia.Point(200, 150),
                    Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
                sel.MouseMove(new Avalonia.Point(600, 400),
                    Avalonia.Input.RawInputModifiers.None);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var bmp = sel.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                AssertPainted(bmp);   // elástico tem tinta (fill + borda)
                bmp.Save(Path.Combine(OutDir, "selection-drag.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                sel.MouseUp(new Avalonia.Point(600, 400),
                    Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None);
            }
            finally { sel.Close(); }
        }, default).GetAwaiter().GetResult();
    }

    [Fact]
    public void Render_Overlay_With_Content()
    {
        var cfg = new ConfigService();
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var overlay = new Gort.UI.OverlayWindow(cfg, _ => 1.0);
            try
            {
                overlay.ApplyRunning(true);
                overlay.Show();
            var frame = new Gort.Loop.OverlayFrame();
            var reg = new Gort.Loop.OverlayRegion
            {
                Index = 0,
                Rect = new Gort.Platform.ScreenRect(100, 100, 600, 200),
                Zoom = 1,
            };
            var block = new Gort.Loop.OverlayBlock
            {
                Text = "Olá, mundo traduzido!",
                OX = 100, OY = 100, OW = 600, OH = 200,
            };
            block.LineBoxes.Add((100, 100, 600, 90));
            block.LineBoxes.Add((100, 200, 500, 90));
            block.WordBoxes.Add((100, 100, 120, 90));
            reg.Blocks.Add(block);
            frame.Regions.Add(reg);
            // Drena o trabalho postado (ex.: ClearCanvas do ApplyRunning):
            // no headless ele só roda no pump da captura e apagaria o quadro.
            using (overlay.CaptureRenderedFrame()) { }
            overlay.DrawOverlay(frame);
            // Verificação em nível de fonte: o bitmap pintado pelo
            // RenderItems, independente da composição da janela.
            Avalonia.Media.Imaging.Bitmap? src = null;
            foreach (var v in overlay.GetVisualDescendants())
            {
                if (v is Avalonia.Controls.Image img
                    && img.Source is Avalonia.Media.Imaging.Bitmap b)
                { src = b; break; }
            }
            Assert.NotNull(src);
            src.Save(Path.Combine(OutDir, "overlay-source.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            AssertPainted(src);
            using var bmp = overlay.CaptureRenderedFrame();
            Assert.NotNull(bmp);
            bmp.Save(Path.Combine(OutDir, "overlay-content.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            finally
            {
                overlay.ApplyRunning(false);
                overlay.Close();
            }
        }, default).GetAwaiter().GetResult();
    }
}
