using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Gort.Config;
using Gort.Lifecycle;
using Gort.Ocr;
using Gort.Platform;
using Gort.Regions;
using Gort.Store;
using Gort.Translate;

namespace Gort;

public partial class App : Application, UI.IRemoteHost
{
    public ConfigService Config { get; } = new();
    public TranslationController Controller { get; } = new();
    public RegionManager Regions { get; private set; } = null!;
    public UI.TranslationWindows Windows { get; private set; } = null!;
    public Translate.TranslationPipeline Pipe { get; private set; } = null!;
    public Translate.OneShotTranslator OneShot { get; private set; } = null!;
    public Translate.ResultMemory ResultMemory { get; } = new();
    public Translate.Collectanea Collectanea { get; private set; } = null!;
    public Translate.DisplayMemory DisplayMemory { get; private set; } = null!;
    public Loop.ILoopEffects LoopEffects { get; private set; } = null!;
    public Audio.SpeechService Speech { get; } = new();
    public Clipboard.ClipboardWatcher? ClipWatcher { get; private set; }
    public Regions.FollowMouseService? Follow { get; private set; }
    public Input.HotkeyService Hotkeys { get; } = new();
    public UI.HotkeyActions HotkeyActions { get; private set; } = null!;
    public UI.RemoteWindow? Remote { get; private set; }
    public Loop.TranslationLoop? CurrentLoop { get; set; }
    public string LastRecognized { get; set; } = "";
    public bool DictEditorOpen { get; set; }          // RF-475: suspende cópia
    public CapabilityReport Capabilities { get; private set; } =
        new() { Platform = "unknown" };
    public MainWindow? MainWin { get; private set; }
    private UI.AreasWindow? _areasWin;
    private string? _pendingMajor;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Config.LoadAll();
        Regions = new RegionManager(Config);
        Windows = new UI.TranslationWindows(Config);
        Translate.Services.Configure(() => Config.Advanced, () => Config.Profile);
        Collectanea = new Translate.Collectanea(
            () => Config.Advanced.CollectActive,
            () => Config.Advanced.CollectAsDb,
            () => Config.Advanced.CollectIgnoreCase);
        DisplayMemory = new Translate.DisplayMemory(
            () => Config.Advanced.DisplayMemoryN, () => Config.Advanced.DisplayMemorySec);
        LoopEffects = new Loop.RealEffects(DisplayMemory, Config, Speech);
        Pipe = new Translate.TranslationPipeline(Translate.Services.Get,
            Collectanea, () => Config.Profile.OcrLanguage, ResultMemory);
        Pipe.CacheCount = ResultMemory.Count;   // RF-491: marcador ◈(n)
        OneShot = new Translate.OneShotTranslator(Config, Controller, Regions, Windows, Pipe,
            LoopEffects);
        Controller.LoopError += OnLoopError;   // RF-014
        Controller.LoopEnded += () => ResultMemory.FlushAsync();   // RF-211
        // Geometria da área de trabalho para as capturas CLI (nulo-seguro
        // antes da janela principal existir).
        PlatformFactory.DesktopGeometry = DesktopMonitors;
        // RF-576: capacidades detectadas na inicialização, antes de qualquer tradução.
        Capabilities = PlatformFactory.Current.Report();
        foreach (var n in Capabilities.Notes) Trace.WriteLine($"GORT cap: {n}");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // RF-004/005: splash com versão+data; tarefas rodam enquanto visível.
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version?.ToString(3) ?? "0.0.0";
            string date;
            try { date = new System.IO.FileInfo(asm.Location).LastWriteTime.ToString("yyyy-MM-dd"); }
            catch { date = "?"; }
            var splash = new UI.SplashWindow(ver, date);
            desktop.MainWindow = splash;
            splash.Opened += (_, _) => RunStartupTasks(splash, desktop);
            splash.Show();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>RF-005: atualizações, config remota, idiomas OCR, perfil.</summary>
    private void RunStartupTasks(UI.SplashWindow splash,
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            void Status(string s) => Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() => splash.SetStatus(s));
            Status("Verificando atualização…");
            try
            {
                if (Config.App.CheckUpdate)                       // RF-416: opcional
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    var ver = asm.GetName().Version?.ToString(3) ?? "0.0.0";
                    using var cts = new System.Threading.CancellationTokenSource(
                        TimeSpan.FromSeconds(20));
                    var (kind, vf) = await Update.Updater.CheckAsync(ver, cts.Token);
                    if (kind == Update.Updater.Verdict.Major)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            // A janela principal ainda não existe: registra para exibir depois.
                            _pendingMajor = vf.Version;
                        });
                    }
                    else if (kind == Update.Updater.Verdict.Minor)
                    {
                        if (await Update.Updater.StartMinorAsync(ver, vf, _ => { }, cts.Token))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ExitApp)
                                .GetAwaiter().OnCompleted(() => { });
                            return;   // RF-425: principal encerra; sem dicionários (RF-434)
                        }
                    }
                    try
                    {
                        using var http = new System.Net.Http.HttpClient();
                        string cfg = await http.GetStringAsync(
                            Update.Dist.RemoteConfigUrl, cts.Token);
                        Update.RemoteConfig.Apply(cfg);           // RF-417 🔒
                    }
                    catch { }                                     // RF-419/426: sem rede, segue
                    try { await Update.Updater.CheckDictsAsync(vf, cts.Token); }
                    catch { }                                     // RF-433
                }
            }
            catch { }   // sem rede → inicialização normal
            Status("Enumerando idiomas de OCR…");
            OcrEngines.Initialize(() => Config.Profile.ModernVertical,
                () => Config.Profile.ClassicDataset, () => Config.Profile.ClassicFast,
                () => Config.Profile.CloudCredFile, () => Config.Profile.CloudMonthlyLimit);
            Trace.WriteLine("GORT: idiomas OCR: " +
                string.Join(",", OcrEngines.Modern.SupportedOcrLanguages()));
            Status("Carregando perfil…");
            Regions.LoadFromProfile();   // RF-040: recria áreas nas posições salvas
            ResultMemory.LoadAll();      // RF-208
            Translate.Services.ReloadDb(Config.Profile);   // RF-241: dicionário no aplicar
            Status("Verificando desenho de texto…");
            if (!UI.SkiaText.RunVectorCheck())             // RF-007
            {
                Platform.PlatformRuntime.VectorText = false;
                Trace.WriteLine("GORT: desenho vetorial indisponível; caindo para texto simples.");
            }
            ValidateLayerRect();         // RF-041
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                MainWin = new MainWindow(Config, Controller, Regions);
                Windows.ScaleOf = ScaleOfRect;   // RF-075: escala por moldura/área
                desktop.MainWindow = MainWin;
                SetupTray();
                SetupHotkeys();
                SetupAux();                      // Etapa 16: mouse, clipboard
                desktop.ShutdownRequested += (_, _) => ShutdownTray();
                MainWin.Show();
                ShowRemote();   // jornada §2: remoto visível desde o início
                ValidateLayerRect();   // revalida com os monitores reais
                if (!OperatingSystem.IsWindows())
                    // Sem afinidade de captura: oculta as janelas próprias
                    // que intersectam as áreas durante cada captura.
                    PlatformFactory.CaptureConcealer = new AvaloniaConcealer(this);
                if (_pendingMajor is not null)                   // RF-421: maior
                {
                    MainWin.Notify($"Nova versão {_pendingMajor} disponível. " +
                        "Abrindo a página de download.");
                    OpenUrl(Update.Dist.DownloadPage);
                    _pendingMajor = null;
                }
                if (!Platform.PlatformRuntime.VectorText)
                    MainWin.Notify("Desenho vetorial de texto indisponível: o programa " +
                        "usará texto simples sem contorno. Veja a ajuda de erros conhecidos.");
                splash.FinishAsync();   // P-01 + fade P-02
            });
        });
    }

    /// <summary>RF-014: erro do laço → registra, exibe pela UI, termina limpo.</summary>
    private void OnLoopError(Exception ex)
    {
        try
        {
            var log = System.IO.Path.Combine(Core.Paths.BaseDir, "loop-errors.log");
            System.IO.Directory.CreateDirectory(Core.Paths.BaseDir);
            try
            {
                // Sem teto o recorrente cresce sem limite: roda o arquivo.
                var fi = new System.IO.FileInfo(log);
                if (fi.Exists && fi.Length > 256 * 1024)
                    System.IO.File.WriteAllText(log, "[anteriores descartados por tamanho]\n");
            }
            catch { }
            System.IO.File.AppendAllText(log,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n");
        }
        catch { }
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Windows.SetRunning(false);
            MainWin?.Notify("Erro na tradução: " + ex.Message);
        });
    }

    private TrayIcon? _tray;

    private NativeMenuItem? _toggleItem;
    private NativeMenuItem? _topItem;    // RF-017: sempre no topo
    private NativeMenuItem? _dictItem;   // RF-017: dicionário de correção

    /// <summary>RF-017..RF-019: ícone de bandeja com menu; rótulo reflete estado.</summary>
    private void SetupTray()
    {
        try
        {
            var menu = new NativeMenu();
            var openItem = new NativeMenuItem("Mostrar GORT (configurações)");
            openItem.Click += (_, _) => ShowMain();
            menu.Add(openItem);
            var remoteItem = new NativeMenuItem("Mostrar controle remoto");
            remoteItem.Click += (_, _) => ShowRemote();   // RF-517: janela própria
            menu.Add(remoteItem);
            var areasItem = new NativeMenuItem("Gerenciar áreas de OCR…");
            areasItem.Click += (_, _) => OpenAreas();
            menu.Add(areasItem);
            var showItem = new NativeMenuItem("Mostrar janela de tradução");
            // Traz a janela DO MODO ATUAL (antes abria sempre a escura, e
            // quem escondia a camada não tinha como trazê-la de volta).
            showItem.Click += (_, _) => Windows.ShowForMode(Config.Profile.WindowMode);
            menu.Add(showItem);
            _toggleItem = new NativeMenuItem("Iniciar tradução");
            _toggleItem.Click += (_, _) => ToggleLoop();
            menu.Add(_toggleItem);
            // RF-017 (completo): alternâncias e perfis também na bandeja.
            // IsChecked é definido pelo código (não pelo menu nativo), para
            // comportamento idêntico em todas as plataformas.
            _topItem = new NativeMenuItem("Janela de tradução sempre no topo")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = Config.App.TranslationAlwaysOnTop,
            };
            _topItem.Click += async (_, _) => await TrayToggleTopAsync();
            menu.Add(_topItem);
            _dictItem = new NativeMenuItem("Dicionário de correção")
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = Config.Profile.UseDict,
            };
            _dictItem.Click += async (_, _) => await TrayToggleDictAsync();
            menu.Add(_dictItem);
            var saveItem = new NativeMenuItem("Salvar perfil…");
            saveItem.Click += async (_, _) => await TraySaveProfileAsync();
            menu.Add(saveItem);
            var loadItem = new NativeMenuItem("Carregar perfil…");
            loadItem.Click += async (_, _) => await TrayLoadProfileAsync();
            menu.Add(loadItem);
            var defsItem = new NativeMenuItem("Restaurar padrões");
            defsItem.Click += async (_, _) => await TrayRestoreDefaultsAsync();
            menu.Add(defsItem);
            menu.Add(new NativeMenuItemSeparator());
            var exitItem = new NativeMenuItem("Sair");
            exitItem.Click += (_, _) => ExitApp();
            menu.Add(exitItem);
            menu.Add(new NativeMenuItemSeparator());
            var aboutItem = new NativeMenuItem("Sobre");
            aboutItem.Click += (_, _) => ShowAbout();            // RF-017/543
            menu.Add(aboutItem);
            var updateItem = new NativeMenuItem("Verificar atualização");
            updateItem.Click += (_, _) => _ = CheckUpdateManualAsync();  // RF-017
            menu.Add(updateItem);

            _tray = new TrayIcon
            {
                ToolTipText = "GORT",
                Menu = menu,
            };
            try { _tray.Icon = UI.BrandLogo.Icon(); } catch { }
            _tray.Clicked += (_, _) =>
            {
                RefreshToggleLabel();   // RF-018: rótulo reflete o estado
                RefreshTrayChecks();    // RF-017: checks refletem a config
                ShowMain();             // RF-019: duplo clique restaura
            };
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
        }
        catch
        {
            // RF-576: capacidade indisponível → segue sem bandeja, sem falhar.
            _tray = null;
        }
    }

    /// <summary>RF-018: rótulo iniciar/parar acompanha o estado do laço.</summary>
    private void RefreshToggleLabel()
    {
        if (_toggleItem is not null)
            _toggleItem.Header =
                Controller.State == LoopState.Idle ? "Iniciar tradução" : "Parar tradução";
    }

    /// <summary>RF-017: checks da bandeja acompanham a configuração.</summary>
    private void RefreshTrayChecks()
    {
        if (_topItem is not null)
            _topItem.IsChecked = Config.App.TranslationAlwaysOnTop;
        if (_dictItem is not null)
            _dictItem.IsChecked = Config.Profile.UseDict;
    }

    /// <summary>
    /// Aplica mudança de configuração pela bandeja com o protocolo
    /// pausar → aplicar → retomar (RF-012), fora da thread de UI.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> TrayApplyAsync(Action change)
    {
        bool ok = await System.Threading.Tasks.Task.Run(
            () => Controller.ApplyChange(change, Core.Params.P03_LoopWaitMs));
        if (!ok)
            MainWin?.Notify("Não foi possível aplicar agora (tradução ocupada).");
        return ok;
    }

    /// <summary>Ressincroniza bandeja, janelas e principal após toggle.</summary>
    private void TraySyncUi()
    {
        RefreshToggleLabel();
        RefreshTrayChecks();
        // Reaplica Topmost/estado às janelas vivas (RF-319/320), sem efeito
        // colateral além do redesenho com a nova configuração.
        Windows.SetRunning(Controller.State != LoopState.Idle);
        if (MainWin is not null)
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => MainWin.LoadUiFromConfig());
    }

    private async System.Threading.Tasks.Task TrayToggleTopAsync()
    {
        bool target = !Config.App.TranslationAlwaysOnTop;
        if (await TrayApplyAsync(() =>
        {
            Config.App.TranslationAlwaysOnTop = target;
            Config.SaveApp();
        }))
            TraySyncUi();
        else
            RefreshTrayChecks();
    }

    private async System.Threading.Tasks.Task TrayToggleDictAsync()
    {
        bool target = !Config.Profile.UseDict;
        if (await TrayApplyAsync(() =>
        {
            Config.Profile.UseDict = target;
            Config.SaveProfile();
        }))
            TraySyncUi();
        else
            RefreshTrayChecks();
    }

    private Avalonia.Platform.Storage.IStorageProvider? TrayStorage()
    {
        if (MainWin is null)
        {
            MainWin?.Notify("Abra a janela principal para escolher o arquivo.");
            return null;
        }
        var sp = TopLevel.GetTopLevel(MainWin)?.StorageProvider;
        if (sp is null)
            MainWin.Notify("Armazenamento indisponível nesta plataforma.");
        return sp;
    }

    private async System.Threading.Tasks.Task TraySaveProfileAsync()
    {
        var sp = TrayStorage();
        if (sp is null) return;
        var f = await sp.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions { DefaultExtension = "toml" });
        if (f is not null) Config.SaveProfileTo(f.Path.LocalPath);
    }

    private async System.Threading.Tasks.Task TrayLoadProfileAsync()
    {
        var sp = TrayStorage();
        if (sp is null) return;
        var fs = await sp.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false });
        if (fs.Count == 0) return;
        if (await TrayApplyAsync(() => Config.LoadProfileIntoMain(fs[0].Path.LocalPath)))
        {
            TraySyncUi();
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => MainWin?.ReloadAdvancedPanel());
            MainWin?.Notify("Perfil carregado.");
        }
    }

    private async System.Threading.Tasks.Task TrayRestoreDefaultsAsync()
    {
        // Mesmo comportamento do botão da aba Sistema (RF-022).
        if (await TrayApplyAsync(() => Config.RestoreDefaults()))
        {
            TraySyncUi();
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => MainWin?.ReloadAdvancedPanel());
            MainWin?.Notify("Padrões restaurados.");
        }
    }

    private void ShowMain()
    {
        if (MainWin is null) return;
        MainWin.Show();
        MainWin.WindowState = WindowState.Normal;
        MainWin.Activate();
    }

    public void ShowMainPublic() =>
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ShowMain);

    public void ToggleLoopPublic() =>
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(ToggleLoop);

    // Contrato do controle remoto (UI.IRemoteHost, RF-517).
    bool UI.IRemoteHost.RemoteAlwaysOnTop => Config.Advanced.RemoteAlwaysOnTop;
    Lifecycle.LoopState UI.IRemoteHost.LoopState => Controller.State;
    UI.HotkeyActions? UI.IRemoteHost.HotkeyActions => HotkeyActions;
    void UI.IRemoteHost.OpenAreas() => OpenAreas();
    void UI.IRemoteHost.ToggleLoop() => ToggleLoopPublic();
    void UI.IRemoteHost.ShowMain() => ShowMainPublic();

    /// <summary>Controle remoto (RF-517): instância única, reexibe.</summary>
    public void ShowRemote()
    {
        if (Remote is null)
        {
            Remote = new UI.RemoteWindow(this);
            Remote.Closed += (_, _) => Remote = null;
            // Estreia fora do caminho: à direita da janela principal (ou no
            // canto superior direito), nunca sobre as abas — antes abria no
            // padrão em cima do conteúdo.
            try
            {
                var scr = MainWin?.Screens.ScreenFromWindow(MainWin!)
                    ?? MainWin?.Screens.Primary;
                if (scr is not null && MainWin is not null)
                {
                    int rx = MainWin.Position.X + (int)(MainWin.Width * scr.Scaling) + 12;
                    int ry = MainWin.Position.Y;
                    if (rx + 360 > scr.Bounds.X + scr.Bounds.Width)
                        rx = scr.Bounds.X + scr.Bounds.Width - 372;
                    if (ry + 120 > scr.Bounds.Y + scr.Bounds.Height)
                        ry = scr.Bounds.Y + scr.Bounds.Height - 132;
                    Remote.Position = new PixelPoint(
                        Math.Max(scr.Bounds.X, rx), Math.Max(scr.Bounds.Y, ry));
                }
            }
            catch { }
            Remote.Show();
            try { Remote.Activate(); } catch { }   // na frente da principal
        }
        else
        {
            Remote.Show();
            Remote.Activate();
        }
    }

    /// <summary>HWNDs das janelas próprias (RF-452: esperar o foco sair delas).</summary>
    public HashSet<nint> OwnHandles()
    {
        var set = new HashSet<nint>();
        foreach (var w in new Avalonia.Controls.Window?[]
                 { MainWin, Remote, _areasWin, Windows.DarkWindowOrNull() })
        {
            if (w is null) continue;
            try
            {
                var h = w.TryGetPlatformHandle();
                if (h is not null) set.Add(h.Handle);
            }
            catch { }
        }
        return set;
    }

    /// <summary>Fluxo de seleção de área para atalhos (quick/snapshot).</summary>
    public async System.Threading.Tasks.Task<Platform.ScreenRect?> SelectAreaAsync(
        bool exclusion = false)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<Platform.ScreenRect?>();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            var v = VirtualScreen();
            var sel = new UI.SelectionWindow(v,
                MainWin?.Screens.Primary?.Scaling ?? 1.0,
                Config.Advanced.SelectBg, Config.Advanced.SelectAccent);
            sel.Selected += r => tcs.TrySetResult(r);
            sel.Cancelled += () => tcs.TrySetResult(null);
            sel.Closed += (_, _) => tcs.TrySetResult(null);
            sel.Show();
        });
        return await tcs.Task;
    }

    private Platform.ScreenRect VirtualScreen()
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
        var screens = MainWin?.Screens.All;
        if (screens is null) return new Platform.ScreenRect(0, 0, 800, 600);
        foreach (var s in screens)
        {
            x1 = Math.Min(x1, s.Bounds.X); y1 = Math.Min(y1, s.Bounds.Y);
            x2 = Math.Max(x2, s.Bounds.X + s.Bounds.Width);
            y2 = Math.Max(y2, s.Bounds.Y + s.Bounds.Height);
        }
        return new Platform.ScreenRect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
    }

    /// <summary>Editor de dicionário com o reconhecido pré-carregado (RF-537).</summary>
    public void OpenDictEditor() =>
        new UI.DictEditorWindow(LastRecognized,
            System.IO.Path.Combine(Core.Paths.DictDir, Config.Profile.DictFile))
        .Show(MainWin!);

    /// <summary>
    /// Geometria da área de trabalho em pixels físicos para as capturas CLI
    /// (nulo-seguro: antes da janela principal, lista vazia).
    /// </summary>
    private IReadOnlyList<Platform.MonitorInfo> DesktopMonitors()
    {
        var list = new List<Platform.MonitorInfo>();
        try
        {
            var screens = MainWin?.Screens.All;
            if (screens is null) return list;
            foreach (var s in screens)
                list.Add(new Platform.MonitorInfo(
                    new Platform.ScreenRect(s.Bounds.X, s.Bounds.Y,
                        s.Bounds.Width, s.Bounds.Height), s.Scaling));
        }
        catch { }
        return list;
    }

    /// <summary>
    /// Ocultação das janelas próprias durante a captura (macOS/Linux).
    /// Só age quando há interseção real com as áreas; a espera na thread de
    /// UI tem prazo curto para nunca travar a parada do laço (P-03).
    /// </summary>
    private sealed class AvaloniaConcealer(App app) : Platform.ICaptureConcealer
    {
        public IDisposable Conceal(IReadOnlyList<Platform.ScreenRect> rects)
        {
            try
            {
                var wins = app.OwnWindowRects();
                var hit = new List<Avalonia.Controls.Window>();
                foreach (var (w, b) in wins)
                {
                    foreach (var r in rects)
                    {
                        if (b.X < r.X + r.W && r.X < b.X + b.W
                            && b.Y < r.Y + r.H && r.Y < b.Y + b.H)
                        { hit.Add(w); break; }
                    }
                }
                if (hit.Count == 0) return Platform.NullConcealer.Instance.Conceal(rects);
                try
                {
                    var t = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var w in hit)
                            try { w.Opacity = 0; } catch { }
                    });
                    t.Wait(TimeSpan.FromMilliseconds(200));
                }
                catch { }
                return new Restorer(hit);
            }
            catch { return Platform.NullConcealer.Instance.Conceal(rects); }
        }

        private sealed class Restorer(List<Avalonia.Controls.Window> wins) : IDisposable
        {
            public void Dispose()
            {
                try
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        foreach (var w in wins)
                            try { w.Opacity = 1; } catch { }
                    });
                }
                catch { }
            }
        }
    }

    /// <summary>Janelas próprias visíveis com limites em pixels físicos.</summary>
    private List<(Avalonia.Controls.Window W, Platform.ScreenRect B)> OwnWindowRects()
    {
        var list = new List<(Avalonia.Controls.Window, Platform.ScreenRect)>();
        try
        {
            double scale = MainWin?.Screens.Primary?.Scaling ?? 1.0;
            foreach (var w in new Avalonia.Controls.Window?[]
                     { MainWin, Remote, _areasWin, Windows.DarkWindowOrNull(),
                       Windows.LayerWindowOrNull(), Windows.OverlayWindowOrNull() })
            {
                if (w is null || !w.IsVisible) continue;
                try
                {
                    list.Add((w, new Platform.ScreenRect(w.Position.X, w.Position.Y,
                        Math.Max(1, (int)(w.Width * scale)),
                        Math.Max(1, (int)(w.Height * scale)))));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    /// <summary>RF-041: valida a posição salva da camada contra os monitores.</summary>
    public void ValidateLayerRect()
    {
        var mons = new System.Collections.Generic.List<(int, int, int, int)>();
        int screenH = 900;
        foreach (var m in Platform.PlatformFactory.Current.Capture.GetMonitors())
        {
            mons.Add((m.Bounds.X, m.Bounds.Y, m.Bounds.W, m.Bounds.H));
            screenH = Math.Max(screenH, m.Bounds.Y + m.Bounds.H);
        }
        var p = Config.Profile;
        var (x, y, w, h) = UI.LayerWindow.Validate(
            p.LayerX, p.LayerY, p.LayerW, p.LayerH, screenH, mons);
        p.LayerX = x; p.LayerY = y; p.LayerW = w; p.LayerH = h;
    }

    /// <summary>
    /// Fase 1 exclui a saída da captura, então ela nunca é traduzida junto;
    /// o aviso restante é de UX: a janela sobre a área cobre o texto do jogo.
    /// Dura P-90.
    /// </summary>
    public void CheckSelfCapture()
    {
        string mode = Config.Profile.WindowMode;
        if (mode != "dark" && mode != "layer") return;
        var win = mode == "layer" ? (Avalonia.Controls.Window?)Windows.LayerWindowOrNull()
            : Windows.DarkWindowOrNull();
        if (win is null || !win.IsVisible) return;
        double scale = MainWin?.Screens.Primary?.Scaling ?? 1.0;
        var wr = new Platform.ScreenRect(win.Position.X, win.Position.Y,
            (int)(win.Width * scale), (int)(win.Height * scale));
        foreach (var a in Regions.Areas)
        {
            var r = a.Rect;
            if (wr.X < r.X + r.W && r.X < wr.X + wr.W && wr.Y < r.Y + r.H && r.Y < wr.Y + wr.H)
            {
                const string warn = "A janela de tradução está sobre uma área de OCR e pode cobrir o texto do jogo.";
                if (mode == "layer") Windows.LayerWindowOrNull()?.ShowWarning(
                    warn, Core.Params.P90_OverlapWarnSec);   // 🔒 10 s
                else MainWin?.Notify(warn);
                break;
            }
        }
    }

    /// <summary>RF-351: sobreposição exige OCR com posição de palavra.</summary>
    public bool EnsureOverlayOcr()
    {
        string mode = Config.Profile.WindowMode;
        if (mode != "overlay" && mode != "replace") return true;
        var eng = Ocr.OcrEngines.Get(Config.Profile.OcrEngine);
        if (eng is not null && eng.IsAvailable && eng.ProvidesWordBoxes) return true;
        MainWin?.Notify("O modo Sobreposição exige um motor de OCR com posição " +
            "de palavra. Veja a ajuda.");
        return false;
    }

    /// <summary>RF-122: nuvem só em modo pontual.</summary>
    public bool EnsureRealtimeOcr()
    {
        var eng = Ocr.OcrEngines.Get(Config.Profile.OcrEngine);
        if (eng is not null && eng.PunctualOnly)
        {
            MainWin?.Notify("O motor de nuvem só pode ser usado em Modo pontual.");
            return false;
        }
        return true;
    }

    /// <summary>RF-075: escala do monitor que contém o retângulo.</summary>
    public double ScaleOfRect(Platform.ScreenRect r)
    {
        try
        {
            var screens = MainWin?.Screens.All;
            if (screens is not null)
                foreach (var s in screens)
                    if (r.X < s.Bounds.X + s.Bounds.Width && s.Bounds.X < r.X + r.W
                        && r.Y < s.Bounds.Y + s.Bounds.Height && s.Bounds.Y < r.Y + r.H)
                        return s.Scaling;
        }
        catch { }
        return 1.0;
    }

    private void ToggleLoop()
    {
        if (Controller.State == LoopState.Idle)
        {
            // RF-065: sem área, não inicia — explica e oferece a seleção.
            if (!Regions.CanTranslate(out var msg))
            {
                OpenAreas();
                MainWin?.Notify(msg);
                return;
            }
            if (!EnsureOverlayOcr()) return;                     // RF-351
            if (!EnsureRealtimeOcr()) return;                    // RF-122
            ConcludeAreas();                                     // RF-085
            Windows.ScaleOf = ScaleOfRect;
            Windows.ShowForMode(Config.Profile.WindowMode, Regions.CaptureRects());   // RF-317
            CheckSelfCapture();                                          // RF-343
            var loop = new Loop.TranslationLoop(Config, Regions, Pipe,
                Windows.MakeSink(), LoopEffects);
            // Aviso do laço em torrada (some sozinha, sem modal — P2).
            loop.Notice = msg => Avalonia.Threading.Dispatcher.UIThread
                .InvokeAsync(() => MainWin?.NotifyToast(msg));            // RF-570
            // RF-351: sobreposição exige OCR com posição (Etapa 12 verifica o modo).
            // Só aponta o atual após o início confirmado (senão o dicionário
            // recarregava num objeto morto e o laço real ficava velho).
            if (Controller.StartLoop(loop, Loop.LoopMode.Continuous))
                CurrentLoop = loop;
            else
                MainWin?.Notify("Não foi possível iniciar: o laço anterior não parou.");
        }
        else
        {
            Controller.RequestStop(Core.Params.P03_LoopWaitMs);
            // Parou: fecha a saída (não deixa texto/erro velho na tela).
            // O pontual/instantâneo não passa aqui — o resultado dele permanece.
            Windows.HideAll();
        }
        RefreshToggleLabel();
    }

    /// <summary>RF-213/RF-499: limpa toda a memória de resultados.</summary>
    public void ClearResultMemory() => ResultMemory.ClearAll();

    /// <summary>Navegador da comunidade (RF-541).</summary>
    public void OpenCommunity() =>
        new UI.CommunityWindow().Show(MainWin!);

    /// <summary>
    /// RF-085: ao concluir/iniciar, as molduras ficam invisíveis durante
    /// toda a tradução. Confirma o gerenciamento aberto.
    /// </summary>
    public void ConcludeAreas()
    {
        if (_areasWin is null) return;
        try { Regions.ApplyManage(); } catch { }
        try { _areasWin.Close(); } catch { }
        _areasWin = null;
    }

    public void OpenAreas()
    {
        if (_areasWin is null)
        {
            _areasWin = new UI.AreasWindow(Config, Regions);
            _areasWin.Closed += (_, _) => _areasWin = null;
            // RF-533: posicionada onde está o controle remoto.
            if (Remote is not null && Remote.IsVisible)
                _areasWin.Position = new PixelPoint(Remote.Position.X, Remote.Position.Y);
            _areasWin.Show(MainWin!);
        }
        else _areasWin.Activate();
    }

    private void SetupHotkeys()
    {
        HotkeyActions = new UI.HotkeyActions(this);
        Hotkeys.Reload(Config.Shortcuts, Config.Advanced);
        Hotkeys.ActionFired += HotkeyActions.Execute;
        Hotkeys.ScreenshotKey += () =>   // RF-347: atalho de captura do SO
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                Windows.OverlayWindowOrNull()?.SetScreenshotCapture());
        if (!Hotkeys.InstallHook())
            Trace.WriteLine("GORT: hook global indisponível; use o controle remoto.");  // RF-569
    }

    public void ReloadHotkeys() => Hotkeys.Reload(Config.Shortcuts, Config.Advanced);

    /// <summary>Etapa 16: segue-mouse, área de transferência.</summary>
    private void SetupAux()
    {
        var follow = new Regions.FollowMouseService(Regions, ScaleOfRect,
            () => Config.Advanced.FollowCompat);
        follow.NeedsArea += async () =>                       // RF-458
        {
            var sel = await SelectAreaAsync();
            if (sel.HasValue)
            {
                follow.SetArea(sel.Value);
                if (MainWin?.IsVisible != true) FlashFollow(sel.Value);
            }
        };
        follow.Blink += () =>                                 // RF-461
        {
            if (_areasWin is null || !_areasWin.IsVisible)
                FlashFollow(follow.Dedicated?.Rect ?? new Platform.ScreenRect(0, 0, 1, 1));
        };
        Follow = follow;

        ClipWatcher = new Clipboard.ClipboardWatcher(
            enabled: () => Config.Advanced.ClipboardTranslate,
            idle: () => Controller.State == LoopState.Idle,
            overlay: () => Config.Profile.WindowMode == "overlay"
                || Config.Profile.WindowMode == "replace",
            busy: () => Regions.Applying,                     // RF-467: sem aplicar
            showOriginal: _ => Config.Advanced.ClipboardShowOriginal,
            showWorking: () => Config.Advanced.ClipboardShowWorking,
            translate: async text =>
            {
                var (srcCode, dstCode) = Loop.TranslationLoop.ResolvePair(
                    Config.Profile, Config.Profile.TranslationService);
                var batch = await Pipe.TranslateBatchAsync(
                    Config.Profile.TranslationService, new List<string> { text },
                    srcCode, dstCode,
                    Config.Advanced.Bridge,
                    System.Threading.CancellationToken.None);
                return batch.Error ?? batch.PerText[0] ?? "";
            },
            display: text =>
                Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // RF-471: janela ativa (nunca sobreposição — RF-467).
                    if (Config.Profile.WindowMode == "layer" && Windows.LayerWindowOrNull() is { } layer)
                    { layer.SetText(text); if (!layer.IsVisible) UI.TranslationWindows.ShowExcluded(layer); }
                    else
                    {
                        Windows.ShowDark();
                        Windows.Dark().ShowTranslation(text, "", false,
                            Config.Advanced.IgnoreEmpty);
                    }
                }),
            speak: text =>
            {
                if (Config.Profile.Tts)
                    Speech.Speak(text, Config.Profile.TtsWait,
                        Translate.RemoteDefaults.DefaultToken);
            });
    }

    private void FlashFollow(Platform.ScreenRect rect)
    {
        try
        {
            var f = new UI.AreaFrameWindow(0, rect, ScaleOfRect(rect), false,
                _ => ScaleOfRect(rect), () => new Platform.ScreenRect(0, 0, 30000, 30000),
                followStyle: true);   // RF-463
            f.Show();
            var t = new Avalonia.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(Core.Params.P124_FollowBlinkMs),  // 🔒
            };
            t.Tick += (_, _) => { t.Stop(); f.Close(); };
            t.Start();
        }
        catch { }
    }

    private void ShutdownTray()
    {
        try
        {
            Controller.RequestStop(Core.Params.P03_LoopWaitMs);  // RF-016: parar o laço
            try { ClipWatcher?.Stop(); } catch { }               // sem novas sondagens
            try { Follow?.Stop(); } catch { }                    // sem novos ticks
            Hotkeys.Dispose();                                   // RF-016: soltar hook
            Translate.Services.ShutdownAll();                    // Edge, workers
            Ocr.OcrEngines.ShutdownAll();                        // sessões, venv
            Speech.Dispose();
            TrayIcon.SetIcons(this, new TrayIcons());
            _tray?.Dispose();
        }
        catch { /* encerrar limpo */ }
    }

    public void ExitApp()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.Shutdown();
    }

    /// <summary>Sobre com versões (RF-435/543).</summary>
    public void ShowAbout()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var ver = asm.GetName().Version?.ToString(3) ?? "0.0.0";
        string date;
        try { date = new System.IO.FileInfo(asm.Location).LastWriteTime.ToString("yyyy-MM-dd"); }
        catch { date = "?"; }
        var dicts = string.Join(", ", Update.DataVersions.Load()
            .Select(kv => kv.Key + "=" + kv.Value));
        new UI.AboutWindow(ver, date, dicts == "" ? "—" : dicts).Show(MainWin!);
    }

    /// <summary>Verificação manual (RF-420/421): menor via ajudante, maior abre a página.</summary>
    public async System.Threading.Tasks.Task CheckUpdateManualAsync()
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version?.ToString(3) ?? "0.0.0";
            var (kind, vf) = await Update.Updater.CheckAsync(ver,
                System.Threading.CancellationToken.None);
            if (kind == Update.Updater.Verdict.None)
            {
                MainWin?.Notify("O programa já está atualizado.");
                return;
            }
            if (kind == Update.Updater.Verdict.Major)            // RF-421
            {
                MainWin?.Notify($"Nova versão {vf.Version} disponível (atual {ver}). Abrindo o download.");
                OpenUrl(Update.Dist.DownloadPage);
                return;
            }
            MainWin?.Notify($"Baixando atualização {vf.Version}… O programa será encerrado.");
            if (await Update.Updater.StartMinorAsync(ver, vf, _ => { },
                    System.Threading.CancellationToken.None))
                ExitApp();                                        // RF-425
            else MainWin?.Notify("Não foi possível atualizar agora.");
        }
        catch { MainWin?.Notify("Sem rede para verificar atualização."); }  // RF: sem rede, silêncio
    }

    public static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
}