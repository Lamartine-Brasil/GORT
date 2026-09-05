using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Gort.Config;
using Gort.Locale;

namespace Gort;

public partial class MainWindow
{
    private Control BuildText()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        var fontBtn = new Button { Content = Strings._("text.font") };
        fontBtn.Click += (_, _) => PickFont();
        p.Children.Add(H(Strings._("tab.text")));
        p.Children.Add(Row(fontBtn, _u.FontFam, new TextBlock { Text = Strings._("text.size") }, _u.FontSize));
        _u.FontFam.MinWidth = 160;
        _u.FontSize.Width = 60;
        foreach (var (btn, name) in new[] { (_u.SwText, "Texto"), (_u.SwC1, "Contorno 1"),
                     (_u.SwC2, "Contorno 2"), (_u.SwBg, "Fundo") })
        {
            btn.Content = name;
            btn.Click += (_, _) => PickColor(btn == _u.SwBg, c =>
            {
                if (btn == _u.SwText) _u.CText = c;
                else if (btn == _u.SwC1) _u.CC1 = c;
                else if (btn == _u.SwC2) _u.CC2 = c;
                else _u.CBg = c;
                PaintSwatches();
                RenderPreview();
            });
        }
        var restore = new Button { Content = Strings._("text.restore_colors") };
        restore.Click += (_, _) =>  // RF-390 P-101..104 🔒
        {
            _u.CText = [255, 255, 255]; _u.CC1 = [192, 192, 192];
            _u.CC2 = [0, 0, 0]; _u.CBg = [170, 0, 0, 0];
            PaintSwatches(); RenderPreview();
        };
        p.Children.Add(Row(_u.SwText, _u.SwC1, _u.SwC2, _u.SwBg, restore));
        _u.Center.Content = Strings._("text.center");
        _u.RmSpaces.Content = Strings._("text.remove_spaces");
        _u.UseBg.Content = Strings._("text.use_bg");
        _u.AreaNum.Content = Strings._("text.area_numbers");
        // Sem contorno: para caixa de texto fora da imagem do jogo, o texto
        // puro fica mais legível que com o contorno duplo.
        _u.Outline.Content = Strings._("adv.use_outline");
        _u.Center.IsCheckedChanged += (_, _) => RenderPreview();   // RF-508: imediato
        _u.RmSpaces.IsCheckedChanged += (_, _) => RenderPreview();
        _u.UseBg.IsCheckedChanged += (_, _) => RenderPreview();
        _u.AreaNum.IsCheckedChanged += (_, _) => RenderPreview();
        _u.Outline.IsCheckedChanged += (_, _) => RenderPreview();
        p.Children.Add(Row(_u.Center, _u.RmSpaces));
        p.Children.Add(Row(_u.UseBg, _u.AreaNum));
        p.Children.Add(Row(_u.Outline));
        // Sobreposição: tamanho/fusão/direção/cores automáticas (antes os
        // controles escreviam em outro lugar e nada funcionava).
        _u.OvAutoFont.Content = Strings._("adv.auto_font");
        _u.OvMerge.Content = Strings._("adv.merge_blocks");
        _u.OvKeepDir.Content = Strings._("adv.keep_dir");
        _u.OvAutoCol.Content = Strings._("adv.auto_color");
        _u.OvAutoFg.Content = Strings._("adv.auto_fg");
        _u.OvAutoBg.Content = Strings._("adv.auto_bg");
        p.Children.Add(H("Sobreposição"));
        p.Children.Add(Row(_u.OvAutoFont, _u.OvMerge, _u.OvKeepDir));
        p.Children.Add(Row(_u.OvAutoCol, _u.OvAutoFg, _u.OvAutoBg));
        p.Children.Add(new TextBlock { Text = "Pré-visualização:" });
        _u.Preview.Height = 120;
        // Moldura escura: o texto claro aparece como no jogo, não no branco.
        var frame = new Border
        {
            Background = new SolidColorBrush(
                UI.GortTheme.DarkPreview),
            BorderBrush = new SolidColorBrush(
                Color.FromRgb(0xE2, 0xE4, 0xEB)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = _u.Preview,
        };
        p.Children.Add(frame);
        return p;
    }

    private void PaintSwatches()
    {
        _u.SwText.Background = new SolidColorBrush(Color.FromRgb(_u.CText[0], _u.CText[1], _u.CText[2]));
        _u.SwC1.Background = new SolidColorBrush(Color.FromRgb(_u.CC1[0], _u.CC1[1], _u.CC1[2]));
        _u.SwC2.Background = new SolidColorBrush(Color.FromRgb(_u.CC2[0], _u.CC2[1], _u.CC2[2]));
        var bg = _u.CBg;
        _u.SwBg.Background = new SolidColorBrush(Color.FromArgb(bg[0], bg[1], bg[2], bg[3]));
    }

    private void PickFont()
    {
        try
        {
            var fams = new List<string>();
            foreach (var f in SkiaSharp.SKFontManager.Default.GetFontFamilies())
                fams.Add(f);
            fams.Sort();
            var box = new Window
            {
                Title = Strings._("text.font"), Width = 320, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var list = new ListBox { ItemsSource = fams };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is string f) _u.FontFam.Text = f;
            };
            var ok = new Button { Content = "OK", MinWidth = 90 };
            ok.Click += (_, _) => { box.Close(); RenderPreview(); };
            box.Content = new DockPanel();
            var dock = (DockPanel)box.Content;
            var btnRow = new StackPanel { Margin = new Thickness(8) };
            btnRow.Children.Add(ok);
            dock.Children.Add(list);
            dock.Children.Add(btnRow);
            DockPanel.SetDock(btnRow, Dock.Bottom);
            box.ShowDialog(this);
        }
        catch { }
    }

    private void PickColor(bool withAlpha, Action<byte[]> onPick)
    {
        var dlg = new Window
        {
            Title = "Cor", Width = 340, Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var picker = new ColorPicker();
        var ok = new Button { Content = "OK", MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            var c = picker.Color;
            onPick(withAlpha
                ? new byte[] { c.A, c.R, c.G, c.B }
                : new byte[] { c.R, c.G, c.B });
            dlg.Close();
        };
        dlg.Content = new StackPanel
        {
            Margin = new Thickness(8), Spacing = 8,
            Children = { picker, ok },
        };
        dlg.ShowDialog(this);
    }

    private void RenderPreview()
    {
        try
        {
            // RF-509/510: latinos, japoneses, numerais, multi-área.
            string sample = _u.AreaNum.IsChecked == true
                ? "1 : Hello 世界 123\n2 : テスト Test 456"
                : "- Hello 世界 123";
            if (_u.RmSpaces.IsChecked == true) sample = sample.Replace(" ", "");
            double size = Num(_u.FontSize, 8, 72, 14);
            using var face = UI.SkiaText.ResolveFont(
                string.IsNullOrWhiteSpace(_u.FontFam.Text) ? null : _u.FontFam.Text);
            float px = (float)(size * 96 / 72);
            using var meas = new SkiaSharp.SKFont(face, px);
            var lines = UI.SkiaText.Wrap(sample, meas, 700);
            using var bmp = new SkiaSharp.SKBitmap(760, Math.Max(60, lines.Count * (int)(px * 1.4) + 20));
            using var canvas = new SkiaSharp.SKCanvas(bmp);
            canvas.Clear(SkiaSharp.SKColors.Transparent);
            var align = _u.Center.IsChecked == true
                ? UI.SkiaText.HAlign.Center : UI.SkiaText.HAlign.Left;
            UI.SkiaText.DrawOutlined(canvas, lines, 8, 8, face, px,
                new SkiaSharp.SKColor(_u.CText[0], _u.CText[1], _u.CText[2]),
                new SkiaSharp.SKColor(_u.CC1[0], _u.CC1[1], _u.CC1[2]),
                new SkiaSharp.SKColor(_u.CC2[0], _u.CC2[1], _u.CC2[2]),
                align, 744, _u.Outline.IsChecked == true);
            var wb = new WriteableBitmap(new PixelSize(bmp.Width, bmp.Height),
                new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = wb.Lock())
                System.Runtime.InteropServices.Marshal.Copy(
                    bmp.Bytes, 0, fb.Address, bmp.Bytes.Length);
            (_u.Preview.Source as System.IDisposable)?.Dispose();
            _u.Preview.Source = wb;
        }
        catch { }
    }

    // Fonte da imagem (jornada Captura): ampliação e janela. Frequência e modo
    // de janela moram nas próprias seções da jornada, cada assunto num lugar só.
    private Control BuildCaptureSourceSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H("Fonte de captura"));
        _u.ActiveWin.Content = Strings._("cap.active_window");
        _u.Zoom.Width = 60;
        var zoomDef = new Button { Content = Strings._("img.zoom_default") };
        zoomDef.Click += (_, _) => _u.Zoom.Text = "2";   // RF-115
        var attached = new Button { Content = Strings._("cap.attached") };
        attached.Click += (_, _) => new UI.AttachedPickerWindow().Show(this);
        // Gating por capacidade (RF-576): o indisponível explica, nunca falha.
        var caps = Platform.PlatformFactory.Current;
        if (!caps.Capture.SupportsClientArea)
        {
            _u.ActiveWin.IsEnabled = false;
            Avalonia.Controls.ToolTip.SetTip(_u.ActiveWin,
                caps.Windows.UnavailableReason ?? "Captura de janela indisponível neste sistema.");
        }
        // Anexada ao vivo (PrintWindow) só existe no Windows; sem ela o
        // picker abriria uma seleção que nunca anexa. Janela ativa segue a
        // capacidade de cada SO (macOS e X11 suportam; Wayland não).
        if (!OperatingSystem.IsWindows())
        {
            attached.IsEnabled = false;
            Avalonia.Controls.ToolTip.SetTip(attached,
                "Captura anexada (janela coberta) só funciona no Windows. Use áreas fixas.");
        }
        if (!caps.Report().ScreenCapture)
        {
            var warn = new TextBlock
            {
                Text = "⚠ " + (Platform.Linux.LinuxCapture.UnavailableHint
                    ?? "Captura de tela indisponível neste sistema."),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
            p.Children.Add(warn);
        }
        p.Children.Add(Row(_u.ActiveWin, new TextBlock { Text = Strings._("cap.zoom") },
            _u.Zoom, zoomDef, attached));
        return p;
    }

    // Ritmo do laço (P-05..P-09). Título "Frequência": jornada de captura.
    private Control BuildSpeedSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H("Frequência"));
        var speeds = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        string[] names = ["1 (300 ms)", "2 (1 s)", "3 (1,5 s)", "4 (2 s)", "5 (2,5 s)"];
        for (int i = 0; i < 5; i++)
        {
            _u.Speeds[i] = new RadioButton { Content = names[i], GroupName = "speed" };
            speeds.Children.Add(_u.Speeds[i]);
        }
        p.Children.Add(speeds);
        return p;
    }

    // Aba Mostrar: onde a tradução aparece.
    private Control BuildWindowModeSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H(Strings._("win.mode")));
        _u.MDark.Content = Strings._("win.dark");
        _u.MLayer.Content = Strings._("win.layer");
        _u.MOverlay.Content = Strings._("win.overlay");
        _u.MReplace.Content = Catalogs.WindowModes.First(c => c.Id == "replace").DisplayPtBr;
        _u.MDark.GroupName = _u.MLayer.GroupName = _u.MOverlay.GroupName = _u.MReplace.GroupName = "winmode";
        _u.Top.Content = Strings._("win.always_on_top");
        p.Children.Add(Row(_u.MDark, _u.MLayer, _u.MOverlay, _u.MReplace, _u.Top));
        // Camada — teto de tamanho: controle fácil (números) em vez de só
        // arrastar a borda — que nem dá com a janela atravessável traduzindo.
        p.Children.Add(H("Camada — tamanho"));
        _u.LayerFit.Content = "Ajustar ao texto (até o máximo)";
        _u.LayerMaxW.Width = 70; _u.LayerMaxH.Width = 70;
        p.Children.Add(Row(_u.LayerFit,
            new TextBlock { Text = "Largura máxima", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            _u.LayerMaxW,
            new TextBlock { Text = "Altura máxima", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
            _u.LayerMaxH,
            UI.GortTheme.Help("(0 = livre)")));
        // Camada — posição inicial da estreia (depois o usuário arrasta).
        p.Children.Add(H("Camada — posição inicial"));
        _u.LPlaceOutside.Content = "Fora das áreas (padrão)";
        _u.LPlaceTop.Content = "Em cima, dentro da captura";
        _u.LPlaceBottom.Content = "Embaixo, dentro da captura";
        _u.LPlaceOutside.GroupName = _u.LPlaceTop.GroupName
            = _u.LPlaceBottom.GroupName = "layerplace";
        p.Children.Add(Row(_u.LPlaceOutside, _u.LPlaceTop, _u.LPlaceBottom));
        return p;
    }

    private Control BuildSystemSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H("Perfis e comunidade"));
        var load = new Button { Content = Strings._("profile.load") };
        load.Click += async (_, _) =>
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp is null) return;
            var fs = await sp.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false });
            if (fs.Count > 0)
            {
                _cfg.LoadProfileIntoMain(fs[0].Path.LocalPath);
                LoadUiFromConfig();
                ReloadAdvancedPanel();   // painel também mostra chaves do perfil
            }
        };
        var save = new Button { Content = Strings._("profile.save") };
        save.Click += async (_, _) =>
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp is null) return;
            var f = await sp.SaveFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerSaveOptions { DefaultExtension = "toml" });
            if (f is not null) _cfg.SaveProfileTo(f.Path.LocalPath);
        };
        var defs = new Button { Content = Strings._("profile.defaults") };
        defs.Click += (_, _) =>
        {
            _cfg.RestoreDefaults();
            LoadUiFromConfig();
            ReloadAdvancedPanel();   // perfil padrão também aparece no painel
        };  // RF-022
        var comm = new Button { Content = Strings._("community.browse") };
        comm.Click += (_, _) => ((App)Application.Current!).OpenCommunity();
        var exp = new Button { Content = Strings._("community.export") };
        exp.Click += OnExport;
        // A janela avançada separada não tinha abridor (só existia a classe).
        var advWin = new Button { Content = Strings._("advanced.open") };
        advWin.Click += (_, _) => new UI.AdvancedWindow(_cfg).Show(this);
        UI.GortTheme.Secondary(load); UI.GortTheme.Secondary(save);
        UI.GortTheme.Secondary(defs); UI.GortTheme.Secondary(comm); UI.GortTheme.Secondary(exp);
        UI.GortTheme.Secondary(advWin);
        p.Children.Add(Row(load, save, defs));
        p.Children.Add(Row(comm, exp, advWin));

        // O ajuste fino mora nos expanders (nesta janela); sem botão que
        // abra janela separada duplicada.
        _u.CheckUpdate.Content = Strings._("app.check_update");
        _u.BasicDefault.Content = Strings._("app.basic_default");
        p.Children.Add(_u.CheckUpdate);
        p.Children.Add(_u.BasicDefault);
        return p;
    }

    private Control BuildTranslation()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(new TextBlock
        {
            Text = "Códigos por serviço: vazio = não oferecido.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
        });
        string[] svcs = ["web-free", "db", "web-nokey", "commercial-kr", "sheets",
            "embedded-browser", "commercial-eu", "llm", "local-worker", "custom"];
        // Grade com coluna de rótulo automática: alinha os combos e nunca
        // corta o rótulo mais largo ("...planilha em nuvem" era cortado com
        // largura fixa). Cabe no cartão (rótulo ~320 + 160 + 160 + folgas).
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions(
                string.Join(",", Enumerable.Repeat("Auto", svcs.Length))),
            ColumnSpacing = 8,
            RowSpacing = 4,
        };
        int row = 0;
        foreach (var s in svcs)
        {
            var src = new ComboBox { Width = 160 };
            var dst = new ComboBox { Width = 160 };
            var names = Translate.LangCodes.All
                .Where(e => e.Svc.TryGetValue(s, out var c) && c != "")
                .Select(e => e.Key).ToList();                    // RF-511
            // RF-313: destino padrão (pt-BR) primeiro na lista de destino.
            // RF-309/310: pares iniciais en/ja → pt-BR; tabela é dado, novos
            // idiomas entram sozinhos aqui sem mexer no núcleo.
            var dstNames = names.OrderByDescending(k =>
                k == Translate.LangCodes.DefaultTarget).ToList();
            src.ItemsSource = names;
            dst.ItemsSource = dstNames;
            _u.LangPairs[s] = (src, dst);
            string label = Config.Catalogs.TranslationServices
                .FirstOrDefault(e => e.Id == s)?.DisplayPtBr
                ?? Translate.Services.List()
                    .FirstOrDefault(e => e.Id == s).Display ?? s;
            var tb = new TextBlock
            {
                Text = label,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            Grid.SetColumn(tb, 0); Grid.SetRow(tb, row);
            Grid.SetColumn(src, 1); Grid.SetRow(src, row);
            Grid.SetColumn(dst, 2); Grid.SetRow(dst, row);
            grid.Children.Add(tb); grid.Children.Add(src); grid.Children.Add(dst);
            row++;
        }
        p.Children.Add(grid);
        _u.Tts.Content = Strings._("tts.enable");
        _u.TtsWait.Content = Strings._("tts.wait");
        if (!Platform.PlatformFactory.Current.Speech.IsAvailable)
        {
            _u.Tts.IsEnabled = false;
            Avalonia.Controls.ToolTip.SetTip(_u.Tts,
                Platform.PlatformFactory.Current.Speech.UnavailableReason
                ?? "Síntese de voz indisponível neste sistema.");
        }
        return p;
    }

    private Control BuildOther()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H(Strings._("hotkeys.title")));
        string[] actions =
        [
            Config.ShortcutActions.ToggleLoop, Config.ShortcutActions.Once,
            Config.ShortcutActions.Snapshot, Config.ShortcutActions.Quick,
            Config.ShortcutActions.DictEditor, Config.ShortcutActions.HideWindow,
            Config.ShortcutActions.FollowMouse,
        ];
        string[] labels =
        [
            Strings._("hotkeys.toggle"), Strings._("hotkeys.once"), Strings._("hotkeys.snapshot"),
            Strings._("hotkeys.quick"), Strings._("hotkeys.dict"), Strings._("hotkeys.hide"),
            Strings._("hotkeys.follow"),
        ];
        _u.Hotkeys.Clear();
        for (int i = 0; i < actions.Length; i++)
        {
            var field = new TextBox { Width = 180, IsReadOnly = true };
            string act = actions[i];
            field.AddHandler(KeyDownEvent, (s, e) => CaptureKey(e, field), handledEventsToo: true);
            field.GotFocus += (_, _) => Input.HotkeyGuard.CaptureFieldFocused = true;  // RF-514
            field.LostFocus += (_, _) => Input.HotkeyGuard.CaptureFieldFocused = false;
            var def = new Button { Content = Strings._("hotkeys.default") };
            def.Click += (_, _) => field.Text = DefaultFor(act);
            var clr = new Button { Content = Strings._("hotkeys.clear") };
            clr.Click += (_, _) => field.Text = "";
            UI.GortTheme.Secondary(def); UI.GortTheme.Secondary(clr);
            _u.Hotkeys.Add((act, field));
            p.Children.Add(Row(new TextBlock { Text = labels[i], Width = 220 }, field, def, clr));
        }
        var manual = new Button { Content = Strings._("help.manual") };
        manual.Click += (_, _) => OpenUrl(Catalogs.Links.Manual);
        var errors = new Button { Content = Strings._("help.errors") };
        errors.Click += (_, _) => OpenUrl(Catalogs.Links.KnownErrors);
        UI.GortTheme.Link(manual); UI.GortTheme.Link(errors);
        p.Children.Add(Row(manual, errors));
        foreach (var (label, url) in new[]
                 {
                     (Strings._("links.repo"), Catalogs.Links.Repo),
                     (Strings._("links.page"), Catalogs.Links.ProjectPage),
                     (Strings._("links.community"), Catalogs.Links.Community),
                 })
        {
            var b = new Button { Content = label };
            UI.GortTheme.Link(b);
            b.Click += (_, _) => OpenUrl(url);                    // RF-517? links (dados)
            p.Children.Add(b);
        }
        return p;
    }

    private static string DefaultFor(string action)
    {
        foreach (var (a, d) in Config.ShortcutActions.Defaults)
            if (a == action) return d;
        return "";
    }

    private static void CaptureKey(KeyEventArgs e, TextBox field)   // RF-513
    {
        if (e.Key == Key.Escape || e.Key == Key.Back)
        {
            field.Text = "";
            e.Handled = true;
            return;
        }
        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) parts.Add("Ctrl");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Win");
        string name = e.Key.ToString();
        if (name is "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift"
            or "LeftAlt" or "RightAlt" or "LWin" or "RWin") return;  // só modificador
        if (parts.Count >= 3) return;                               // RF-442
        parts.Add(name.Length == 1 ? name : name);
        field.Text = string.Join("+", parts);
        e.Handled = true;
    }

    private Control BuildDebug()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H("Depuração (cap. 27)"));
        foreach (var (label, get, set) in new (string, Func<bool>, Action<bool>)[]
                 {
                     ("Destravar velocidade", () => Debug.DebugFlags.UnlockSpeed, v => Debug.DebugFlags.UnlockSpeed = v),
                     ("Mostrar resultados de cache", () => Debug.DebugFlags.ShowCache, v => Debug.DebugFlags.ShowCache = v),
                     ("Uma linha por tradução", () => Debug.DebugFlags.OneLinePerBlock, v => Debug.DebugFlags.OneLinePerBlock = v),
                     ("Mostrar áreas de palavra", () => Debug.DebugFlags.ShowWordAreas, v => Debug.DebugFlags.ShowWordAreas = v),
                     ("Salvar resultado de análise", () => Debug.DebugFlags.SaveAnalysis, v => Debug.DebugFlags.SaveAnalysis = v),
                 })
        {
            var cb = new CheckBox { Content = label, IsChecked = get() };
            cb.IsCheckedChanged += (_, _) => set(cb.IsChecked == true);
            p.Children.Add(cb);
        }
        var clear = new Button { Content = "Limpar memória de resultados" };
        clear.Click += (_, _) =>
        {
            var app = (App)Application.Current!;
            if (app.ResultMemory.IsWriting) return;   // RF-499: desabilitado gravando
            app.ClearResultMemory();
        };
        var logBtn = new Button { Content = "Ver registro" };   // RF-498: acessível
        logBtn.Click += (_, _) =>
        {
            var box = new Window
            {
                Title = "Registro", Width = 560, Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = $"OCR: {Debug.DebugLog.OcrAttempts}  Traduções: {Debug.DebugLog.Translations}\n" +
                            string.Join("\n", Debug.DebugLog.Messages()),
                        Margin = new Thickness(8),
                    },
                },
            };
            box.Show(this);
        };
        p.Children.Add(clear);
        p.Children.Add(logBtn);
        return p;
    }
}
