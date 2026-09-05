using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Gort.Config;
using Gort.Lifecycle;
using Gort.Locale;
using Gort.Platform;
using Gort.Regions;
using Gort.Store;

namespace Gort;

public partial class MainWindow : Window
{
    private readonly ConfigService _cfg;
    private readonly TranslationController _ctl;
    private readonly RegionManager _regions;
    private readonly DispatcherTimer _memTimer;
    private readonly DispatcherTimer _startTimer;
    private readonly DispatcherTimer _applyTimer;
    private bool _firstShow = true;

    public MainWindow() : this(new ConfigService(), new TranslationController(), null) { }

    public MainWindow(ConfigService cfg, TranslationController ctl, RegionManager? regions)
    {
        _cfg = cfg; _ctl = ctl;
        _regions = regions ?? new RegionManager(cfg);
        InitializeComponent();
        UI.BrandLogo.Apply(this);
        try
        {
            var logo = this.FindControl<Image>("LogoImg");
            if (logo is not null) logo.Source = UI.BrandLogo.Image(32);
        }
        catch { }
        Locale.Strings.Load(LocaleFile(), AppLang());

        var asm = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        var ver = this.FindControl<TextBlock>("VersionText");
        ver.Text = $"GORT v{asm}";
        ver.PointerPressed += (_, _) =>
        {
            if (++_verClicks >= 3) { Debug.DebugFlags.Enable(); ShowDebugTab(); }
        };
        Title = $"GORT v{asm}";

        BuildTabs();
        LoadUiFromConfig();
        // 6 abas (Traduzir=0 primeiro): abre sempre em Traduzir; a opção
        // "começar no assistente" apenas reinicia o assistente no topo dela.
        var tabs = this.FindControl<TabControl>("Tabs");
        tabs.SelectedIndex = 0;   // RF-501
        tabs.SelectionChanged += (_, _) => SyncAdvancedGuard();
        PaintTabHeaders();
        tabs.SelectionChanged += (_, _) => PaintTabHeaders();
        if (_cfg.App.BasicTabDefault) { _u.WizStep = 0; RenderWizard(); }

        this.FindControl<Button>("ApplyBtn").Click += OnApply;
        this.FindControl<Button>("DonateBtn").Click += (_, _)
            => OpenUrl(Catalogs.Links.Donate);                       // RF-544 (dado, não literal)
        this.FindControl<Button>("MemoryBtn").Click += OnMemoryDetail;

        // RF-502: arrastar por área vazia do corpo, além da barra de título.
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                && e.Source is DockPanel)
                BeginMoveDrag(e);
        };

        // RF-558..RF-560: amostragem em intervalo fixo, nunca no ciclo.
        _memTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _memTimer.Tick += (_, _) => UpdateMemory();
        _memTimer.Start();
        UpdateMemory();

        // Botão grande espelha a barrinha (500 ms, igual ao remoto).
        _startTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _startTimer.Tick += (_, _) => RefreshStartBtn();
        _startTimer.Start();

        // Confirmação do Aplicar: some sozinha, sem modal.
        _applyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _applyTimer.Tick += (_, _) =>
        {
            this.FindControl<TextBlock>("ApplyHint").Text = "";
            _applyTimer.Stop();
        };

        // RF-086/087: mudança de monitores/resolução com áreas fora da tela.
        Screens.Changed += (_, _) => CheckAreasOnScreen();
    }

    /// <summary>Mensagem transitória (Etapa 11 dá janela própria a isto).</summary>
    public async void Notify(string text)
    {
        var box = new Window
        {
            Title = "GORT", Width = 420, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock { Text = text, Margin = new Thickness(16) },
        };
        await box.ShowDialog(this);
    }

    private async void CheckAreasOnScreen()
    {
        if (_checkingAreas) return;   // Screens.Changed dispara em rajada
        _checkingAreas = true;
        try
        {
        var v = VirtualScreen();
        var bad = _regions.ValidateAgainst(v);
        if (bad.Count == 0) return;
        var box = new Window
        {
            Title = "Áreas fora da tela", Width = 440, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"A disposição dos monitores mudou e {bad.Count} área(s) " +
                               $"ficaram fora da tela: {string.Join(", ", bad.ConvertAll(i => (i + 1).ToString()))}. " +
                               "Corrija no gerenciamento de áreas.",
                    },
                    new Button { Content = "Gerenciar áreas de OCR…", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left },
                },
            },
        };
        ((Button)((StackPanel)box.Content!).Children[1]).Click += (_, _) =>
        {
            box.Close();
            ((App)Application.Current!).OpenAreas();
        };
        await box.ShowDialog(this);
        }
        finally { _checkingAreas = false; }
    }

    private ScreenRect VirtualScreen()
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
        foreach (var s in Screens.All)
        {
            x1 = Math.Min(x1, s.Bounds.X); y1 = Math.Min(y1, s.Bounds.Y);
            x2 = Math.Max(x2, s.Bounds.X + s.Bounds.Width);
            y2 = Math.Max(y2, s.Bounds.Y + s.Bounds.Height);
        }
        return new ScreenRect(x1, y1, Math.Max(1, x2 - x1), Math.Max(1, y2 - y1));
    }

    private void SyncAdvancedGuard()
    {
        var tabs = this.FindControl<TabControl>("Tabs");
        Input.HotkeyGuard.AdvancedOpen =
            tabs?.SelectedItem == this.FindControl<TabItem>("TabAdvanced");
    }

    // Abas com texto puro e cor forte (sem emoji apagado): ativa preta,
    // inativas cinza-escuro legível — nunca o cinza "desabilitado" do tema.
    private void PaintTabHeaders()
    {
        var tabs = this.FindControl<TabControl>("Tabs");
        string[] names = ["TabTranslate", "TabRead", "TabDict", "TabDisplay",
            "TabAdvanced", "TabSystem", "TabDebug"];
        string[] labels = ["Traduzir", "Ler", "Dicionário e idiomas", "Mostrar",
            "Avançado", "Sistema", "Depuração"];
        for (int i = 0; i < names.Length; i++)
        {
            var ti = this.FindControl<TabItem>(names[i]);
            if (ti is null) continue;
            bool sel = tabs?.SelectedItem == ti;
            ti.Header = new TextBlock
            {
                Text = labels[i],
                FontSize = 15,
                FontWeight = sel ? FontWeight.Bold : FontWeight.SemiBold,
                Foreground = new SolidColorBrush(sel
                    ? UI.GortTheme.Text
                    : Color.FromRgb(0x4B, 0x55, 0x63)),
            };
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SyncAdvancedGuard();   // reavalia ao reexibir (saída da bandeja)
        if (_firstShow)
        {
            _firstShow = false;
            // RF-505: cabe na tela sem estourar. As medidas do AXAML já são
            // DIPs (o Avalonia escala sozinho) — multiplicar por scaling
            // aqui duplicava a escala e jogava a janela para fora da tela
            // (ex.: 1080×700 virava ~1620×1050 em 150%, com o Aplicar
            // inalcançável). Apenas limita à área útil do monitor.
            try
            {
                var scr = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                if (scr is not null)
                {
                    // WorkingArea vem em pixels físicos; Width/Height são DIPs.
                    double sc = scr.Scaling <= 0 ? 1.0 : scr.Scaling;
                    double maxW = scr.WorkingArea.Width / sc;
                    double maxH = scr.WorkingArea.Height / sc;
                    if (Width > maxW) Width = Math.Max(320, maxW);
                    if (Height > maxH) Height = Math.Max(200, maxH);
                    double waX = scr.WorkingArea.X / sc, waY = scr.WorkingArea.Y / sc;
                    double nx = Math.Clamp(Position.X / sc, waX, Math.Max(waX, waX + maxW - Width));
                    double ny = Math.Clamp(Position.Y / sc, waY, Math.Max(waY, waY + maxH - Height));
                    Position = new PixelPoint((int)(nx * sc), (int)(ny * sc));
                }
            }
            catch { }
        }
    }

    /// <summary>RF-504: aplicar — sem meia-configuração (Etapa 8 completa o protocolo).</summary>
    private bool _applying;
    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        // Etapa 9: limpar teclas pressionadas. Etapa 3: descartar backup de áreas.
        // RF-012: pausa → aplica → retoma; se não parar, nada é aplicado.
        // Roda fora da thread de UI: com o laço vivo a parada pode levar
        // segundos, e a janela não pode congelar (era o travamento ao
        // configurar chave com tradução rodando).
        if (_applying) return;
        _applying = true;
        var applyBtn = this.FindControl<Button>("ApplyBtn");
        applyBtn.IsEnabled = false;
        this.FindControl<TextBlock>("ApplyHint").Text = "Aplicando…";
        var app = (App)Application.Current!;
        bool wasRunning = app.Controller.State != LoopState.Idle;
        try
        {
            bool ok = await System.Threading.Tasks.Task.Run(() => app.Controller.ApplyChange(() =>
            {
                _regions.Applying = true;   // RF-467: clipboard não traduz aplicando
                try
                {
                    // Controles só na thread de UI (a janela segue respondendo,
                    // pois ela está livre aguardando este Task).
                    Avalonia.Threading.Dispatcher.UIThread.Invoke(() =>
                    {
                        ApplyFromUi();   // UI → configuração (RF-504)
                        _u.AdvPanel?.Apply();   // aba Avançado edita um clone
                    });
                    _cfg.SaveProfile();
                    _cfg.SaveAdvanced();
                    _cfg.SaveApp();
                    _cfg.SaveShortcuts();
                }
                finally { _regions.Applying = false; }
            }, Core.Params.P03_LoopWaitMs));
            if (!ok)
            {
                Notify("Não foi possível aplicar: a tradução não parou a tempo.");
                return;
            }
            app.ClipWatcher?.Reset();                        // RF-472
            app.Windows.SaveLayerGeometry();              // RF-340
            Translate.Services.ReloadDb(_cfg.Profile);   // RF-241: banco no aplicar
            Translate.Services.InvalidateCustom();       // presets podem ter mudado
            app.ReloadHotkeys();                          // RF-443: atalhos no aplicar
            if (wasRunning)
            {
                // Troca de modo (camada/escuro/sobreposição) só vale mostrando
                // a janela nova e fechando a velha — o laço sozinho não mostra.
                app.Windows.ShowForMode(_cfg.Profile.WindowMode,
                    app.Regions.CaptureRects());
                app.CheckSelfCapture();
            }
            _u.LlmKeyState.Text = Store.ConfigService.LoadCreds("llm")
                    .Any(k => !string.IsNullOrEmpty(k.Secret))
                ? "chave salva ✔" : "sem chave — cole, teste e aplique";
            // Confirmação inline no rodapé (some sozinha): sem modal chato.
            this.FindControl<TextBlock>("ApplyHint").Text = "✔ " + Strings._("msg.applied");
            _applyTimer.Stop();
            _applyTimer.Start();
        }
        finally
        {
            applyBtn.IsEnabled = true;
            _applying = false;
        }
    }

    /// <summary>Ciclo único (Etapa 7; atalho na Etapa 9).</summary>
    private async void OnTranslateOnce(object? sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current!;
        if (!app.Regions.CanTranslate(out var msg))         // RF-065
        {
            Notify(msg);
            app.OpenAreas();
            return;
        }
        app.ConcludeAreas();                                // RF-085
        if (sender is Button b) b.IsEnabled = false;
        try
        {
            using var cts = new System.Threading.CancellationTokenSource(
                TimeSpan.FromSeconds(60));
            await app.OneShot.RunAsync(cts.Token);
        }
        finally { if (sender is Button b2) b2.IsEnabled = true; }
    }

    /// <summary>RF-046: exporta a configuração atual e abre a página de envio.</summary>
    private void OnExport(object? sender, RoutedEventArgs e)
    {
        try
        {
            TextCopy.ClipboardService.SetText(_cfg.BuildExportText());
            OpenUrl(Catalogs.Links.Community);
        }
        catch { /* RF-561 */ }
    }

    private TimeSpan _lastCpuTime;   // CPU: amostra anterior (TotalProcessorTime)
    private DateTime _lastCpuAt;     // CPU: instante da amostra anterior (UTC)
    private double _cpuPct;          // CPU: último percentual calculado

    private void UpdateMemory()
    {
        var proc = Process.GetCurrentProcess();
        var mb = proc.WorkingSet64 / 1048576.0;
        // CPU multiplataforma sem API de SO: fração do tempo de processador
        // consumido entre dois tiques, dividida pelos núcleos. O _memTimer
        // (2 s) dá a janela de média — mesma amostragem da memória (RF-558).
        var now = DateTime.UtcNow;
        if (_lastCpuAt != default)
        {
            double dt = (now - _lastCpuAt).TotalSeconds;
            double dcpu = (proc.TotalProcessorTime - _lastCpuTime).TotalSeconds;
            if (dt > 0)
                _cpuPct = 100.0 * dcpu / dt / Environment.ProcessorCount;
        }
        _lastCpuTime = proc.TotalProcessorTime;
        _lastCpuAt = now;
        this.FindControl<Button>("MemoryBtn").Content =
            $"Memória: {mb:F0} MB · CPU: {_cpuPct:F0}%";
    }

    private async void OnMemoryDetail(object? sender, RoutedEventArgs e)
    {
        // RF-559: detalhamento (imagens de região / cache / bitmap da sobreposição
        // ganham valores reais nas Etapas 6–12).
        var p = Process.GetCurrentProcess();
        var box = new Window
        {
            Title = Strings._("memory.title"), Width = 420, Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock
            {
                Margin = new(16),
                Text = $"Total do processo: {p.WorkingSet64 / 1048576.0:F1} MB\n" +
                       $"CPU do processo: {_cpuPct:F1}% (de {Environment.ProcessorCount} núcleos)\n" +
                       $"Imagens de região: 0,0 MB\n" +
                       $"Cache de traduções: 0 entradas\n" +
                       $"Bitmap da sobreposição: —",
            },
        };
        await box.ShowDialog(this);
    }

    /// <summary>RF-015: confirmar ao fechar; modo bandeja → ocultar.</summary>
    public bool TrayMode => _cfg.Advanced.TrayMode;

    private bool _reallyClosing;
    private bool _checkingAreas;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_reallyClosing) { base.OnClosing(e); return; }
        // Cancela primeiro: o diálogo é assíncrono e a janela não pode
        // fechar antes da escolha (condição de corrida do async void).
        e.Cancel = true;
        if (TrayMode)
        {
            Hide();
            Input.HotkeyGuard.AdvancedOpen = false;   // oculta: atalhos voltam
            return;
        }
        var yesBtn = new Button { Content = Strings._("exit.yes") };
        var minBtn = new Button { Content = Strings._("exit.minimize") };
        var noBtn = new Button { Content = Strings._("exit.no") };
        var dlg = new Window
        {
            Title = Strings._("exit.title"), Width = 320, Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new(16), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = Strings._("exit.question") },
                    yesBtn, minBtn, noBtn,
                },
            },
        };
        string choice = "no";
        yesBtn.Click += (_, _) => { choice = "yes"; dlg.Close(); };
        minBtn.Click += (_, _) => { choice = "min"; dlg.Close(); };
        noBtn.Click += (_, _) => { choice = "no"; dlg.Close(); };
        await dlg.ShowDialog(this);
        if (choice == "min")
        {
            Hide();
            Input.HotkeyGuard.AdvancedOpen = false;
        }
        else if (choice == "yes")
        {
            // Sair de verdade: encerra o aplicativo inteiro (todas as
            // janelas), não só a principal — senão ele "fica na bandeja".
            ((App)Application.Current!).ExitApp();
        }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* RF-561: nunca encerrar por falha externa */ }
    }
}
