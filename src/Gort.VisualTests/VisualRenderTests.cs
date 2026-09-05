using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
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
/// Além de construir sem exceção, afirma estrutura: aba selecionada,
/// motor selecionado, grade de idiomas (10 linhas), links do Sistema e
/// rótulos PT-BR do remoto. Os PNGs vão para releases/visual-tests
/// (nunca para dentro de src/ nem para o TEMP). Toda construção de
/// Window acontece dentro do Dispatch (thread da UI).
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

    private static void Shot(Func<Window> make, string name, Action<Window>? setup = null,
        Action<Window>? shown = null)
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
                // Árvore visual só existe após o Show: afirmações de conteúdo
                // vão aqui, não no setup.
                shown?.Invoke(w);
                using var bmp = w.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                string path = Path.Combine(OutDir, name + ".png");
                bmp.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                Console.WriteLine($"SHOT {path} {bmp.PixelSize}");
            }
            // Sem o guarda, fechar janela nunca exibida (setup falhou) lança
            // no OnClosing e mascara o erro real da afirmação.
            finally { if (w.IsVisible) w.Close(); }   // falha no meio não vaza a janela na sessão
        }, default).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Headless nunca passa pelo App real (que inicializa os motores):
    /// sem isso o combo de Motor de OCR sai vazio com placeholder.
    /// Construtores são leves (modelo só carrega no Recognize).
    /// </summary>
    private static void EnsureEngines() => Gort.Ocr.OcrEngines.Initialize(() => false);

    /// <summary>Quadro de demonstração da sobreposição (texto PT-BR).</summary>
    private static Gort.Loop.OverlayFrame DemoFrame() => DemoFrameFor("Olá, mundo traduzido!");

    /// <summary>Quadro de demonstração com texto arbitrário (mesma geometria).</summary>
    private static Gort.Loop.OverlayFrame DemoFrameFor(string text)
    {
        var frame = new Gort.Loop.OverlayFrame();
        var reg = new Gort.Loop.OverlayRegion
        {
            Index = 0,
            Rect = new Gort.Platform.ScreenRect(100, 100, 600, 200),
            Zoom = 1,
        };
        var block = new Gort.Loop.OverlayBlock
        {
            Text = text,
            OX = 100, OY = 100, OW = 600, OH = 200,
        };
        block.LineBoxes.Add((100, 100, 600, 90));
        block.LineBoxes.Add((100, 200, 500, 90));
        block.WordBoxes.Add((100, 100, 120, 90));
        reg.Blocks.Add(block);
        frame.Regions.Add(reg);
        return frame;
    }

    /// <summary>
    /// Sobreposição com 5 textos diferentes (+1 substituição): prova visual
    /// de posição/tamanho/quebra em cada caso.
    /// </summary>
    [Fact]
    public void Render_Overlay_Texts()
    {
        var cfg = new ConfigService();
        // Fonte automática: com a fixa de 14 pt o texto sairia minúsculo e
        // irrepresentativo (o padrão continua intacto no produto).
        cfg.Profile.AutoFontSize = true;
        var cases = new (string Shot, string Text, bool Substitute)[]
        {
            ("overlay-t1-curto", "Oi!", false),
            ("overlay-t2-medio", "Olá, mundo traduzido!", false),
            ("overlay-t3-longo", "Esta é uma frase bem mais longa que precisa quebrar em várias linhas para caber no retângulo da área de captura do jogo.", false),
            ("overlay-t4-linhas", "Linha um\nLinha dois\nLinha três", false),
            ("overlay-t5-cjk", "日本語テストです", false),
            ("replace-t2-medio", "Olá, mundo traduzido!", true),
        };
        foreach (var (shot, text, substitute) in cases)
        {
            HeadlessSetup.Session.Dispatch(() =>
            {
                Directory.CreateDirectory(OutDir);
                var overlay = new Gort.UI.OverlayWindow(cfg, _ => 1.0);
                try
                {
                    overlay.Substitute = substitute;
                    overlay.ApplyRunning(true);
                    overlay.Show();
                    using (overlay.CaptureRenderedFrame()) { }   // drena o ClearCanvas
                    overlay.DrawOverlay(DemoFrameFor(text));
                    for (int i = 0; i < 5; i++)
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    using var bmp = overlay.CaptureRenderedFrame();
                    Assert.NotNull(bmp);
                    AssertPainted(bmp);
                    bmp.Save(Path.Combine(OutDir, shot + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                finally
                {
                    overlay.ApplyRunning(false);
                    overlay.Close();
                }
            }, default).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Camada sempre na frente (pedido do dono): Topmost ligado ao criar,
    /// traduzindo ou parada — clicar no jogo não a cobre.
    /// </summary>
    [Fact]
    public void Layer_StaysOnTop()
    {
        var cfg = new ConfigService();
        HeadlessSetup.Session.Dispatch(() =>
        {
            var layer = new Gort.UI.LayerWindow(cfg);
            try
            {
                Assert.True(layer.Topmost);
                layer.ApplyRunning(true);
                Assert.True(layer.Topmost);
                layer.ApplyRunning(false);
                Assert.True(layer.Topmost);
            }
            finally { if (layer.IsVisible) layer.Close(); }
        }, default).GetAwaiter().GetResult();
    }

    [Fact]
    public void Render_MainWindow_AllTabs()
    {
        EnsureEngines();
        var cfg = new ConfigService();
        string[] tabs = ["home", "capture", "lang", "show", "system"];
        for (int i = 0; i < tabs.Length; i++)
        {
            int idx = i;
            Shot(() => new Gort.MainWindow(cfg, new TranslationController(), null),
                $"main-{idx}-{tabs[idx]}",
                w => w.FindControl<TabControl>("Tabs")!.SelectedIndex = idx,
                w =>
                {
                    var tabsCtl = w.FindControl<TabControl>("Tabs")!;
                    Assert.Equal(idx, tabsCtl.SelectedIndex);
                    if (idx == 1)
                    {
                        // Motores inicializados: o padrão modern sai selecionado,
                        // não o placeholder (regressão headless).
                        var combos = w.GetVisualDescendants().OfType<ComboBox>().ToList();
                        Assert.Contains(combos,
                            c => (c.SelectedItem as string ?? "").StartsWith("modern"));
                    }
                    if (idx == 3)
                    {
                        // Campo de fonte é TextBox com o nome da família
                        // (era ComboBox vazio que exibia branco). Name em
                        // código não registra namescope: busca na árvore.
                        var fam = w.GetVisualDescendants().OfType<TextBox>()
                            .FirstOrDefault(t => t.Name == "FontFam");
                        Assert.NotNull(fam);
                        Assert.True(fam.MinWidth >= 160);
                    }
                    if (idx == 2)
                    {                        // Grade de idiomas por serviço: 3 colunas × 10 linhas
                        // (sem RowDefinitions tudo caía na linha 0). Mora num
                        // expander colapsado: conteúdo colapsado não entra na
                        // árvore visual — a busca é na lógica; o cabeçalho do
                        // expander, sempre visível, vai na visual.
                        var expanders = w.GetVisualDescendants().OfType<Expander>().ToList();
                        Assert.Contains(expanders,
                            x => (x.Header as string) == "Idiomas por serviço");
                        var grid = w.GetLogicalDescendants().OfType<Grid>()
                            .FirstOrDefault(g => g.ColumnDefinitions.Count == 3
                                && g.RowDefinitions.Count == 10);
                        Assert.NotNull(grid);
                    }
                    if (idx == 4)
                    {
                        // Links de ajuda da aba Sistema (idem: busca na janela).
                        var labels = w.GetVisualDescendants().OfType<Button>()
                            .Select(b => b.Content as string).ToList();
                        Assert.Contains("Manual", labels);
                        Assert.Contains("Erros conhecidos", labels);
                        Assert.Contains("Repositório", labels);
                        Assert.Contains("Configuração avançada…", labels);
                    }
                });
        }
    }

    /// <summary>
    /// Expanders abertos: prova que as seções distribuídas do painel
    /// avançado renderizam no lugar (Conteúdo anexa ao expandir).
    /// </summary>
    [Fact]
    public void Render_ExpandedSections()
    {
        var cfg = new ConfigService();
        var cases = new (int Tab, string Shot, string[] Headers)[]
        {
            (1, "expanded-capture", ["Avançado"]),
            (2, "expanded-lang", ["Idiomas por serviço", "Avançado"]),
            (3, "expanded-show", ["Avançado"]),
            (4, "expanded-system", ["Comportamento", "Atalhos avançados"]),
        };
        foreach (var (tab, shot, headers) in cases)
        {
            HeadlessSetup.Session.Dispatch(() =>
            {
                Directory.CreateDirectory(OutDir);
                var w = new Gort.MainWindow(cfg, new TranslationController(), null);
                try
                {
                    w.FindControl<TabControl>("Tabs")!.SelectedIndex = tab;
                    w.Show();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    foreach (var h in headers)
                    {
                        var x = w.GetVisualDescendants().OfType<Expander>()
                            .FirstOrDefault(e => (e.Header as string) == h);
                        Assert.NotNull(x);
                        x.IsExpanded = true;
                    }
                    for (int i = 0; i < 5; i++)
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    // Conteúdo anexado ao expandir (prova na árvore visual) + rola
                    // até o fim para o PNG mostrar o aberto, não o cabeçalho.
                    foreach (var h in headers)
                    {
                        var x = w.GetVisualDescendants().OfType<Expander>()
                            .FirstOrDefault(e => (e.Header as string) == h);
                        Assert.NotNull(x);
                        Assert.True(x.IsExpanded);
                        Assert.True((x.Content as Control)?.IsVisible ?? false);
                    }
                    var sv = w.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                    if (sv is not null) sv.Offset = new Avalonia.Vector(0, double.MaxValue);
                    for (int i = 0; i < 5; i++)
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    using var bmp = w.CaptureRenderedFrame();
                    Assert.NotNull(bmp);
                    AssertPainted(bmp);
                    bmp.Save(Path.Combine(OutDir, shot + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                finally { if (w.IsVisible) w.Close(); }
            }, default).GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void Render_Narrow()
    {
        // Largura mínima (880): nada pode estourar para fora — WrapPanels
        // quebram linha em vez de cortar.
        EnsureEngines();
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
                w.FindControl<TabControl>("Tabs")!.SelectedIndex = 4;
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

    /// <summary>Controle remoto traduzindo: botão central vira "Parar".</summary>
    private sealed class StubRunningHost : Gort.UI.IRemoteHost
    {
        public bool RemoteAlwaysOnTop => false;
        public Gort.Lifecycle.LoopState LoopState => Gort.Lifecycle.LoopState.Running;
        public Gort.UI.HotkeyActions? HotkeyActions => null;
        public void OpenAreas() { }
        public void ToggleLoop() { }
        public void ShowMain() { }
    }

    /// <summary>Rótulos PT-BR da barrinha (regressão de texto/ícone).</summary>
    private static void AssertRemoteLabels(Avalonia.Controls.Window w, string toggle)
    {
        var texts = w.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        foreach (var s in new[] { "Gerenciar áreas", "Área rápida", "Área instantânea", toggle, "Sistema" })
            Assert.Contains(s, texts);
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
        // PT-BR (Gerenciar áreas/Área rápida/Área instantânea/Iniciar tradução-Parar/Sistema) sem o App real.
        Shot(() => new Gort.UI.RemoteWindow(new StubRemoteHost()), "remote",
            setup: null, shown: w => AssertRemoteLabels(w, "Iniciar tradução"));
        Shot(() => new Gort.UI.RemoteWindow(new StubRunningHost()), "remote-running",
            setup: null, shown: w => AssertRemoteLabels(w, "Parar"));
        // Conta-gotas com imagem sintética (era a única janela sem render).
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var bytes = new byte[120 * 80 * 4];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = 200;
            var img = new Gort.Imaging.RegionImage
            {
                Width = 120, Height = 80, Channels = 4, Bytes = bytes,
            };
            var drop = new Gort.UI.DropperWindow("Área 1 — 120×80", img,
                Gort.Imaging.FilterMode.None,
                new System.Collections.Generic.List<(int, int, int, int, int, int, int)>(),
                127);
            try
            {
                drop.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var bmp = drop.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                AssertPainted(bmp);
                bmp.Save(Path.Combine(OutDir, "dropper.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            finally { if (drop.IsVisible) drop.Close(); }
        }, default).GetAwaiter().GetResult();
        // Painel distribuído: 7 seções expostas sem TabControl interno
        // (a principal espalha em expanders; a janela Avançada usa abas).
        HeadlessSetup.Session.Dispatch(() =>
        {
            var ap = new Gort.UI.AdvancedPanel(cfg, distributed: true);
            Assert.True(ap.Distributed);
            Assert.Equal(7, ap.Sections.Count);
            foreach (var (title, content) in ap.Sections)
            {
                Assert.False(string.IsNullOrWhiteSpace(title));
                Assert.NotNull(content);
            }
            // Reload pós-perfil: reconstrói as seções e avisa para redistribuir.
            bool fired = false;
            ap.NeedsRebuild += () => fired = true;
            var before = ap.Sections.Select(s => s.Content).ToList();
            ap.Reload();
            Assert.True(fired);
            Assert.Equal(7, ap.Sections.Count);
            for (int i = 0; i < 7; i++)
                Assert.NotSame(before[i], ap.Sections[i].Content);
        }, default).GetAwaiter().GetResult();
    }

    [Fact]
    public void Render_TranslationWindows()
    {
        var cfg = new ConfigService();
        // Fonte automática na demonstração: com a fixa de 14 pt o texto da
        // sobreposição sairia minúsculo e irrepresentativo (não é o padrão).
        cfg.Profile.AutoFontSize = true;
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
            finally { if (dark.IsVisible) dark.Close(); }
        }, default).GetAwaiter().GetResult();
        Shot(() => new Gort.UI.LayerWindow(cfg), "layer",
            w => ((Gort.UI.LayerWindow)w).SetText(
                "Texto da camada com contorno duplo para leitura sobre o jogo."));
        // Sobreposição vazia não diz nada (só NotNull): desenha o quadro de
        // demonstração e exige tinta — pega regressão de render vazio.
        // Fase 2: modo novo usa a mesma janela com Substitute ligado.
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            foreach (var (name, substitute) in new[] { ("overlay", false), ("replace", true) })
            {
                var overlay = new Gort.UI.OverlayWindow(cfg, _ => 1.0);
                try
                {
                    overlay.Substitute = substitute;
                    overlay.ApplyRunning(true);
                    overlay.Show();
                    using (overlay.CaptureRenderedFrame()) { }   // drena o ClearCanvas
                    overlay.DrawOverlay(DemoFrame());
                    for (int i = 0; i < 5; i++)
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                    using var bmp = overlay.CaptureRenderedFrame();
                    Assert.NotNull(bmp);
                    AssertPainted(bmp);
                    Assert.True(!substitute || overlay.Substitute);
                    bmp.Save(Path.Combine(OutDir, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                finally
                {
                    overlay.ApplyRunning(false);
                    overlay.Close();
                }
            }
        }, default).GetAwaiter().GetResult();
        // Fiação saída→laço (Fase 1): o sink informa o retângulo da janela
        // visível para o laço apagar da captura; escondida, informa vazio.
        // Sobreposição coincide com a fonte por desenho: sempre vazio.
        HeadlessSetup.Session.Dispatch(() =>
        {
            var layer = new Gort.UI.LayerWindow(cfg);
            try
            {
                Gort.Loop.IDisplaySink sink = new Gort.UI.LayerSink(layer);
                Assert.Empty(sink.OutputOccluders());
                layer.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                var occ = sink.OutputOccluders();
                Assert.Single(occ);
                Assert.True(occ[0].W >= 1 && occ[0].H >= 1);
                layer.Hide();
                Assert.Empty(sink.OutputOccluders());
            }
            finally { if (layer.IsVisible) layer.Close(); }
            var dark = new Gort.UI.DarkWindow();
            try
            {
                Gort.Loop.IDisplaySink sink = new Gort.UI.DarkSink(dark);
                dark.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Assert.Single(sink.OutputOccluders());
                dark.Hide();
                Assert.Empty(sink.OutputOccluders());
            }
            finally { if (dark.IsVisible) dark.Close(); }
            var over = new Gort.UI.OverlayWindow(cfg, _ => 1.0);
            try
            {
                Gort.Loop.IDisplaySink sink = new Gort.UI.OverlaySink(over);
                over.ApplyRunning(true);
                over.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Assert.Empty(sink.OutputOccluders());
            }
            finally { over.ApplyRunning(false); over.Close(); }
        }, default).GetAwaiter().GetResult();
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
        // Com área definida: a lista mostra a área (estado populado).
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var cfg2 = new ConfigService();
            var mgr2 = new Gort.Regions.RegionManager(cfg2);
            mgr2.AddArea(new Gort.Platform.ScreenRect(100, 100, 400, 200), exclusion: false);
            var aw = new Gort.UI.AreasWindow(cfg2, mgr2);
            try
            {
                aw.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                Assert.True(mgr2.Managing);
                Assert.Single(mgr2.Working);
                var texts = aw.GetVisualDescendants().OfType<TextBlock>()
                    .Select(t => t.Text).ToList();
                Assert.Contains("Área 1 — 400×200 @ (100,100) [grupos 1]", texts);
                using var bmp = aw.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                bmp.Save(Path.Combine(OutDir, "areas-populated.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            finally { if (aw.IsVisible) aw.Close(); }
        }, default).GetAwaiter().GetResult();
        // Moldura de área (era a única janela sem render): borda verde + título.
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var frame = new Gort.UI.AreaFrameWindow(0,
                new Gort.Platform.ScreenRect(100, 100, 400, 200), 1.0, exclusion: false,
                _ => 1.0, () => new Gort.Platform.ScreenRect(0, 0, 1280, 720));
            try
            {
                frame.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                using var bmp = frame.CaptureRenderedFrame();
                Assert.NotNull(bmp);
                AssertPainted(bmp);   // borda de 8 px representa tinta real
                bmp.Save(Path.Combine(OutDir, "areaframe.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            finally { if (frame.IsVisible) frame.Close(); }
        }, default).GetAwaiter().GetResult();
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
            finally { if (sel.IsVisible) sel.Close(); }
        }, default).GetAwaiter().GetResult();
    }

    [Fact]
    public void Render_Overlay_With_Content()
    {
        var cfg = new ConfigService();
        // Fonte automática: o quadro de demonstração tem linhas de 90 px —
        // com a fonte fixa de 14 pt o texto sairia minúsculo e irrepresentativo.
        cfg.Profile.AutoFontSize = true;
        HeadlessSetup.Session.Dispatch(() =>
        {
            Directory.CreateDirectory(OutDir);
            var overlay = new Gort.UI.OverlayWindow(cfg, _ => 1.0);
            try
            {
                overlay.ApplyRunning(true);
                overlay.Show();
            var frame = DemoFrame();
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
            AssertPainted(bmp);
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
