using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    private int _verClicks;
    private readonly U _u = new();

    private sealed class U
    {
        // Aba 0
        public ComboBox OcrEngine = new();
        public CheckBox ShowOcr = new(), SaveFile = new(), CopyClip = new();
        public StackPanel PClassic = new(), POs = new(), PModern = new(),
            PCloud = new(), PVenv = new();
        public TextBox Dataset = new();
        public ComboBox OcrLangClassic = new(), OsLang = new(), ModernLang = new(),
            CloudLang = new(), VenvLang = new();
        public CheckBox Fast = new(), ModernVertical = new(), CloudPriority = new();
        public RadioButton OcrEn = new(), OcrJa = new();
        public TextBox CloudCred = new();
        public TextBlock CloudUsage = new();
        public ComboBox TrService = new();
        public StackPanel PDb = new(), PWeb = new(), PNoKey = new(), PKr = new(),
            PSheets = new(), PBrowser = new(), PEu = new(), PLlm = new(),
            PLocal = new(), PCustom = new();
        public TextBox DbFile = new();
        public CheckBox DbIgnore = new(), DbPartial = new();
        public ComboBox WebQuality = new();
        public TextBlock WebState = new();
        public TextBox KrId = new(), KrSecret = new();
        public TextBox SheetId = new(), SheetsClient = new(), SheetsSecret = new(), SheetsCode = new();
        public TextBlock SheetsToken = new();
        public TextBlock BrowserState = new();
        public TextBox EuKey = new();
        public RadioButton EuFree = new(), EuPaid = new();
        public TextBox LlmKey = new();
        public ComboBox LlmModel = new();
        public TextBox LlmCustom = new();
        public TextBlock LlmKeyState = new();
        public CheckBox DictUse = new(), DictWord = new();
        public TextBox DictFile = new();
        public RadioButton FRgb = new(), FHsv = new(), FThr = new();
        public TextBox Threshold = new();
        public CheckBox Erode = new();
        public ComboBox Groups = new();
        public TextBox R = new(), G = new(), B = new(),
            S1 = new(), S2 = new(), V1 = new(), V2 = new();
        public TextBlock GroupCount = new();
        // Aba 1
        public ComboBox FontFam = new();
        public TextBox FontSize = new();
        public Button SwText = new(), SwC1 = new(), SwC2 = new(), SwBg = new();
        public byte[] CText = [255, 255, 255], CC1 = [192, 192, 192],
            CC2 = [0, 0, 0], CBg = [170, 0, 0, 0];
        public CheckBox Center = new(), RmSpaces = new(), UseBg = new(), AreaNum = new(), Outline = new();
        public Image Preview = new();
        // Aba 2
        public CheckBox ActiveWin = new();
        public TextBox Zoom = new();
        public RadioButton[] Speeds = new RadioButton[5];
        public RadioButton MDark = new(), MLayer = new(), MOverlay = new(), MReplace = new();
        public CheckBox Top = new(), CheckUpdate = new(), BasicDefault = new();
        public CheckBox LayerFit = new();
        public TextBox LayerMaxW = new(), LayerMaxH = new();
        // Aba 3
        public Dictionary<string, (ComboBox Src, ComboBox Dst)> LangPairs = new();
        public CheckBox Tts = new(), TtsWait = new();
        // Aba 4
        public List<(string Action, TextBox Field)> Hotkeys = new();
        // Aba 5
        public StackPanel Wizard = new();
        public int WizStep;
        public string WizColor = "";
        // Primeiro acesso
        public Button StartBtn = new();
        public TextBlock AreaStatus = new();
        public UI.AdvancedPanel? AdvPanel;
    }

    private string LocaleFile()
    {
        string[] cands =
        [
            System.IO.Path.Combine(AppContext.BaseDirectory, "Locale", "pt-BR.csv"),
            "src/Gort/Locale/pt-BR.csv",
            "/app/src/Gort/Locale/pt-BR.csv",
        ];
        foreach (var c in cands)
            if (System.IO.File.Exists(c)) return c;
        return cands[0];
    }

    private string AppLang() =>
        _cfg.App.UiLanguage == "" ? Strings.SystemLanguage() : _cfg.App.UiLanguage;  // RF-484

    private void ShowDebugTab() =>
        this.FindControl<TabItem>("TabDebug").IsVisible = Debug.DebugFlags.Enabled;

    // ================= construção =================

    private static TextBlock H(string t) => UI.GortTheme.SectionTitle(t);

    // Responsivo: WrapPanel quebra linha em telas estreitas / DPI alto /
    // fontes grandes, em vez de estourar como StackPanel horizontal.
    private static WrapPanel Row(params Control[] cs)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 4 };
        foreach (var c in cs) p.Children.Add(c);
        return p;
    }

    private static int Num(TextBox t, int min, int max, int def = 0)   // RF-042
    {
        if (!int.TryParse(t.Text, out int v)) return def;
        return Math.Clamp(v, min, max);
    }

    private static ScrollViewer Sv(Control c)
    {
        // Conteúdo com largura máxima: respira sem esticar até a borda.
        if (c is Panel p)
        {
            p.MaxWidth = 760;
            p.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        }
        return new()
        {
            Content = c,
            HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
    }

    private static Control Stack(params Control[] cs)
    {
        var p = new StackPanel { Spacing = 2, Margin = new Thickness(20, 8, 20, 8) };
        foreach (var c in cs) p.Children.Add(c);
        return p;
    }

    // Nova UI por tarefa (tudo por aba, com rolagem e nada em janela extra
    // nem dobrado): Traduzir, Ler, Dicionário e idiomas, Mostrar, Avançado,
    // Sistema. Uma só verdade por assunto.
    private void BuildTabs()
    {
        this.FindControl<TabItem>("TabTranslate").Content = Sv(Stack(
            UI.GortTheme.Card(BuildQuickStart()), UI.GortTheme.Card(BuildSpeedSection()),
            UI.GortTheme.Card(BuildServiceSection())));
        this.FindControl<TabItem>("TabRead").Content = Sv(Stack(
            UI.GortTheme.Card(BuildOcrSection()),
            UI.GortTheme.Card(BuildImageFilterSection()),
            UI.GortTheme.Card(BuildCaptureSourceSection())));
        this.FindControl<TabItem>("TabDict").Content = Sv(Stack(
            UI.GortTheme.Card(BuildDictSection()), UI.GortTheme.Card(BuildTranslation())));
        this.FindControl<TabItem>("TabDisplay").Content = Sv(Stack(
            UI.GortTheme.Card(BuildWindowModeSection()), UI.GortTheme.Card(BuildText())));
        _u.AdvPanel = new UI.AdvancedPanel(_cfg);
        _u.AdvPanel.NeedsRebuild += () =>
        {
            _u.AdvPanel = new UI.AdvancedPanel(_cfg);
            this.FindControl<TabItem>("TabAdvanced").Content = Sv(_u.AdvPanel);
        };
        this.FindControl<TabItem>("TabAdvanced").Content = Sv(_u.AdvPanel);
        this.FindControl<TabItem>("TabSystem").Content = Sv(Stack(
            BuildSystemSection(), BuildOther()));
        this.FindControl<TabItem>("TabDebug").Content = Sv(BuildDebug());
        BuildWizard();
    }

    // === Seções por função (nova UI agrupada) ===
    // Cada seção popula seus controles _u uma única vez (BuildTabs roda uma vez).

    private Control BuildOcrSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        // Fonte única da verdade do idioma (en/ja): vale para todos os
        // motores; os combos por motor acompanham e continuam editáveis.
        p.Children.Add(H("Idioma do texto"));
        _u.OcrEn.Content = "Inglês"; _u.OcrJa.Content = "Japonês";
        _u.OcrEn.GroupName = _u.OcrJa.GroupName = "ocrlang";
        _u.OcrEn.IsCheckedChanged += (_, _) =>
        { if (_u.OcrEn.IsChecked == true) SetOcrLang("en"); };
        _u.OcrJa.IsCheckedChanged += (_, _) =>
        { if (_u.OcrJa.IsChecked == true) SetOcrLang("ja"); };
        p.Children.Add(Row(_u.OcrEn, _u.OcrJa));
        p.Children.Add(H("Motor de OCR"));
        _u.OcrEngine.ItemsSource = Ocr.OcrEngines.List()
            .Select(e => e.Id + " — " + e.Display
                + (e.Available ? "" : " (indisponível)")).ToList();
        _u.OcrEngine.PlaceholderText = "Selecione o motor…";
        _u.OcrEngine.MinWidth = 320;
        _u.OcrEngine.SelectionChanged += (_, _) => ShowOcrPanel();
        _u.ShowOcr.Content = Strings._("ocr.show_result");
        _u.SaveFile.Content = Strings._("ocr.save_file");
        _u.CopyClip.Content = Strings._("ocr.copy_clipboard");
        p.Children.Add(_u.OcrEngine);
        p.Children.Add(_u.ShowOcr); p.Children.Add(_u.SaveFile); p.Children.Add(_u.CopyClip);

        _u.Dataset.Width = 120;
        _u.Fast.Content = Strings._("ocr.fast_mode");
        _u.OcrLangClassic.ItemsSource = new List<string> { "en", "ja" };
        _u.CloudLang.ItemsSource = new List<string> { "auto", "en", "ja" };
        _u.VenvLang.ItemsSource = new List<string> { "en", "ja" };
        _u.PClassic.Children.Add(Row(new TextBlock { Text = Strings._("ocr.dataset") }, _u.Dataset));
        _u.PClassic.Children.Add(Row(new TextBlock { Text = "Idioma" }, _u.OcrLangClassic));
        _u.PClassic.Children.Add(_u.Fast);
        _u.OsLang.ItemsSource = new List<string>();
        var addLang = new Button { Content = Strings._("ocr.add_language") };
        // ms-settings: só existe no Windows — nas outras plataformas o
        // botão mostra orientação em vez de falhar em silêncio (RF-136).
        if (!OperatingSystem.IsWindows())
        {
            addLang.IsEnabled = false;
            Avalonia.Controls.ToolTip.SetTip(addLang, "Disponível apenas no Windows");
        }
        else addLang.Click += (_, _) => OpenUrl("ms-settings:regionlanguage");  // RF-136
        _u.POs.Children.Add(Row(new TextBlock { Text = "Idioma" }, _u.OsLang));
        _u.POs.Children.Add(addLang);
        _u.ModernVertical.Content = "Linhas verticais (reordenar por coluna)";
        _u.PModern.Children.Add(Row(new TextBlock { Text = "Idioma" }, _u.ModernLang));
        _u.PModern.Children.Add(_u.ModernVertical);
        var credBtn = new Button { Content = Strings._("ocr.cloud_cred") };
        credBtn.Click += (_, _) => PickFile(_u.CloudCred, "JSON (*.json)|*.json");
        _u.CloudPriority.Content = Strings._("ocr.cloud_priority");
        _u.PCloud.Children.Add(Row(new TextBlock { Text = "Idioma" }, _u.CloudLang));
        _u.PCloud.Children.Add(Row(_u.CloudCred, credBtn));
        _u.PCloud.Children.Add(_u.CloudUsage);
        _u.PCloud.Children.Add(_u.CloudPriority);
        var venvBtn = new Button { Content = Strings._("ocr.install") };
        venvBtn.Click += (_, _) => new UI.VenvInstallWindow().Show(this);
        _u.PVenv.Children.Add(Row(new TextBlock { Text = "Idioma" }, _u.VenvLang));
        _u.PVenv.Children.Add(venvBtn);
        p.Children.Add(_u.PClassic); p.Children.Add(_u.POs); p.Children.Add(_u.PModern);
        p.Children.Add(_u.PCloud); p.Children.Add(_u.PVenv);
        return p;
    }

    private Control BuildServiceSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H(Strings._("tr.service")));
        _u.TrService.ItemsSource = Translate.Services.List()
            .Select(e => e.Id + " — " + e.Display).ToList();
        _u.TrService.SelectionChanged += (_, _) => ShowTrPanel();
        var trHelp = new Button { Content = Strings._("tr.help") };
        trHelp.Click += (_, _) => OpenUrl(Catalogs.Links.Manual);
        UI.GortTheme.Link(trHelp);
        p.Children.Add(Row(_u.TrService, trHelp));

        _u.DbFile.Width = 200;
        _u.DbIgnore.Content = Strings._("tr.ignore_case");
        _u.DbPartial.Content = Strings._("tr.partial_match");
        _u.PDb.Children.Add(Row(new TextBlock { Text = Strings._("tr.db_file") }, _u.DbFile));
        _u.PDb.Children.Add(_u.DbIgnore); _u.PDb.Children.Add(_u.DbPartial);
        _u.PWeb.Children.Add(UI.GortTheme.Help("Google — GET público — alta qualidade (cai para baixa em 429)"));
        _u.WebQuality.ItemsSource = new List<string>
            { "auto — Automática", "high — Alta", "low — Baixa (rápida)" };
        _u.PWeb.Children.Add(Row(new TextBlock { Text = "Qualidade" }, _u.WebQuality));
        _u.PWeb.Children.Add(_u.WebState);
        _u.PNoKey.Children.Add(new TextBlock { Text = "POST público com espaçamento anti-bloqueio." });
        _u.KrId.Width = 160; _u.KrSecret.Width = 220;
        var krBtn = new Button { Content = Strings._("tr.manage_keys") };
        krBtn.Click += (_, _) =>
            new UI.KeyManagerWindow("commercial-kr", "Chaves KR").Show(this);
        _u.PKr.Children.Add(Row(new TextBlock { Text = Strings._("tr.key_id") }, _u.KrId));
        _u.PKr.Children.Add(Row(new TextBlock { Text = Strings._("tr.key_secret") }, _u.KrSecret));
        _u.PKr.Children.Add(krBtn);
        _u.SheetId.Width = 280;
        _u.SheetsClient.Width = 200; _u.SheetsSecret.Width = 200;
        var shAuth = new Button { Content = "Autenticar…" };
        shAuth.Click += (_, _) => OpenUrl(Translate.SheetsTranslator.ConsentUrl(
            _u.SheetsClient.Text ?? "", "urn:ietf:wg:oauth:2.0:oob"));
        var shClear = new Button { Content = Strings._("tr.clear_tokens") };
        shClear.Click += (_, _) => new Translate.SheetsTranslator(() => "", () => false).ClearTokens();
        _u.PSheets.Children.Add(Row(new TextBlock { Text = Strings._("tr.sheet") }, _u.SheetId));
        _u.PSheets.Children.Add(Row(new TextBlock { Text = Strings._("tr.client_id") }, _u.SheetsClient));
        _u.SheetsCode.Width = 200;
        _u.SheetsCode.PlaceholderText = "Cole aqui o código do Google…";
        var shExchange = new Button { Content = "Trocar código por token" };
        shExchange.Click += async (_, _) =>
        {
            try
            {
                var t = new Translate.SheetsTranslator(() => "", () => false);
                bool ok = await t.ExchangeCodeAsync(
                    _u.SheetsClient.Text ?? "", _u.SheetsSecret.Text ?? "",
                    _u.SheetsCode.Text ?? "", "urn:ietf:wg:oauth:2.0:oob",
                    System.Threading.CancellationToken.None);
                _u.SheetsToken.Text = "token: " + (t.HasToken() ? "ok" : "ausente");
                Notify(ok ? "Planilha autenticada." :
                    "Falha ao trocar o código. Confira cliente, segredo e código.");
            }
            catch (System.Exception ex) { Notify("Falha de rede: " + ex.Message); }
        };
        _u.PSheets.Children.Add(Row(_u.SheetsSecret, shAuth, shClear));
        _u.PSheets.Children.Add(Row(_u.SheetsCode, shExchange));
        _u.PSheets.Children.Add(_u.SheetsToken);
        var brBtn = new Button { Content = Strings._("tr.check_state") };
        brBtn.Click += (_, _) => new Translate.BrowserTranslator(() => null, () => false)
            .ShowInspector();
        _u.PBrowser.Children.Add(new TextBlock { Text = "Edge sem interface via CDP." });
        _u.PBrowser.Children.Add(Row(_u.BrowserState, brBtn));
        _u.EuKey.Width = 260;
        _u.EuFree.Content = Strings._("tr.endpoint_free");
        _u.EuPaid.Content = Strings._("tr.endpoint_paid");
        _u.PEu.Children.Add(Row(new TextBlock { Text = Strings._("tr.eu_key") }, _u.EuKey));
        _u.PEu.Children.Add(Row(_u.EuFree, _u.EuPaid));
        _u.LlmKey.Width = 260;
        _u.LlmCustom.Width = 200;
        _u.LlmKey.PlaceholderText = "Cole aqui a chave do Google AI Studio…";
        _u.PLlm.Children.Add(Row(new TextBlock { Text = Strings._("tr.llm_key") }, _u.LlmKey));
        _u.PLlm.Children.Add(UI.GortTheme.Help("Chave do Google AI Studio (aistudio.google.com). Fica só na sua máquina."));
        _u.PLlm.Children.Add(Row(new TextBlock { Text = Strings._("tr.llm_model") }, _u.LlmModel));
        _u.PLlm.Children.Add(_u.LlmCustom);
        _u.LlmModel.SelectionChanged += (_, _) => SyncLlmCustom();
        SyncLlmCustom();
        // Testa a chave na hora (sem aplicar nem travar): chama o modelo
        // com texto mínimo e mostra o resultado. Vale para qualquer modelo
        // da lista — e o mecanismo de chave em arquivo serve a todos os
        // serviços com chave (escalável, sem nada hardcoded).
        var llmTest = new Button { Content = "Testar chave…" };
        llmTest.Click += async (_, _) => await TestLlmKeyAsync();
        _u.PLlm.Children.Add(Row(llmTest, _u.LlmKeyState));
        _u.PLocal.Children.Add(new TextBlock { Text = "Processo auxiliar (biblioteca local)" });
        _u.PCustom.Children.Add(new TextBlock { Text = Strings._("tr.custom_hint") });
        p.Children.Add(_u.PDb); p.Children.Add(_u.PWeb); p.Children.Add(_u.PNoKey);
        p.Children.Add(_u.PKr); p.Children.Add(_u.PSheets); p.Children.Add(_u.PBrowser);
        p.Children.Add(_u.PEu); p.Children.Add(_u.PLlm); p.Children.Add(_u.PLocal);
        p.Children.Add(_u.PCustom);
        return p;
    }

    private Control BuildDictSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H(Strings._("dict.use")));
        _u.DictUse.Content = Strings._("dict.use");
        _u.DictWord.Content = Strings._("dict.by_word");
        _u.DictFile.Width = 160;
        p.Children.Add(Row(_u.DictUse, _u.DictFile, _u.DictWord));
        return p;
    }

    private Control BuildImageFilterSection()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(H("Correção de imagem"));
        _u.FRgb.Content = Strings._("img.filter_rgb");
        _u.FHsv.Content = Strings._("img.filter_hsv");
        _u.FThr.Content = Strings._("img.filter_threshold");
        _u.Erode.Content = Strings._("img.erode");
        var viewBtn = new Button { Content = Strings._("img.view_result") };
        viewBtn.Click += (_, _) => ((App)Application.Current!).OpenAreas();
        UI.GortTheme.Secondary(viewBtn);
        _u.Groups.MinWidth = 280;
        p.Children.Add(Row(_u.FRgb, _u.FHsv, _u.FThr, _u.Threshold, _u.Erode));
        p.Children.Add(Row(new TextBlock { Text = Strings._("img.group") },
            _u.Groups, _u.GroupCount, viewBtn));
        _u.Groups.SelectionChanged += (_, _) => LoadGroupFields();
        _u.Groups.SelectionChanged += (_, _) => GroupComboPicked();
        p.Children.Add(Row(new TextBlock { Text = "R" }, _u.R, new TextBlock { Text = "G" }, _u.G,
            new TextBlock { Text = "B" }, _u.B));
        p.Children.Add(Row(new TextBlock { Text = "S1" }, _u.S1, new TextBlock { Text = "S2" }, _u.S2,
            new TextBlock { Text = "V1" }, _u.V1, new TextBlock { Text = "V2" }, _u.V2));
        return p;
    }

    private void ShowOcrPanel()
    {
        string id = OcrId();
        _u.PClassic.IsVisible = id == "classic";
        _u.POs.IsVisible = id == "os";
        _u.PModern.IsVisible = id == "modern";
        _u.PCloud.IsVisible = id == "cloud";
        _u.PVenv.IsVisible = id == "venv";
    }

    private string OcrId()
    {
        string s = _u.OcrEngine.SelectedItem as string ?? "";
        int cut = s.IndexOf(' ');
        return cut > 0 ? s[..cut] : s;
    }

    private void ShowTrPanel()
    {
        string id = TrId();
        _u.PDb.IsVisible = id == "db";
        _u.PWeb.IsVisible = id == "web-free";
        _u.PNoKey.IsVisible = id == "web-nokey";
        _u.PKr.IsVisible = id == "commercial-kr";
        _u.PSheets.IsVisible = id == "sheets";
        _u.PBrowser.IsVisible = id == "embedded-browser";
        _u.PEu.IsVisible = id == "commercial-eu";
        _u.PLlm.IsVisible = id == "llm";
        _u.PLocal.IsVisible = id == "local-worker";
        _u.PCustom.IsVisible = id == "custom" || id.StartsWith("custom:");
    }

    private string TrId()
    {
        string s = _u.TrService.SelectedItem as string ?? "";
        int cut = s.IndexOf(' ');
        return cut > 0 ? s[..cut] : s;
    }

    // Fonte única da verdade do idioma de OCR (en/ja): os rádios no topo da
    // aba Ler sincronizam todos os combos por motor (que continuam editáveis
    // individualmente). Sem isso o usuário não achava onde trocar o idioma.
    private bool _syncingOcrLang;

    private void SetOcrLang(string lang)
    {
        if (_syncingOcrLang) return;
        _syncingOcrLang = true;
        try
        {
            _u.OcrEn.IsChecked = lang == "en";
            _u.OcrJa.IsChecked = lang == "ja";
            SetLangCombo(_u.OcrLangClassic, ["en", "ja"], lang,
                "Só há uma opção disponível.");
            SetLangCombo(_u.CloudLang, ["auto", "en", "ja"], lang,
                "Só há uma opção disponível.");
            SetLangCombo(_u.VenvLang, ["en", "ja"], lang,
                "Só há uma opção disponível.");
            SetLangCombo(_u.ModernLang,
                Ocr.OcrEngines.Get("modern")?.SupportedOcrLanguages()
                    .Select(Translate.LangCodes.KeyForOcr).Where(k => k != "").ToList() ?? ["en"],
                lang,
                "Só inglês disponível neste motor. O japonês exige o par de modelos japoneses.");
        }
        finally { _syncingOcrLang = false; }
    }

    /// <summary>
    /// Campo de modelo personalizado só aparece com "custom" selecionado
    /// (fora isso polui a seção à toa).
    /// </summary>
    private void SyncLlmCustom()
    {
        _u.LlmCustom.IsVisible =
            (_u.LlmModel.SelectedItem as string) == "custom";
    }

    /// <summary>Resolve o modelo do teste da chave (puro, testável).</summary>
    internal static string ResolveLlmTestModel(string? selected, string? custom)
    {
        if (selected == "custom") return custom ?? "";
        if (string.IsNullOrWhiteSpace(selected)) return Translate.RemoteDefaults.LlmDefaultModel;
        return selected;
    }

    private async System.Threading.Tasks.Task TestLlmKeyAsync()
    {
        string key = _u.LlmKey.Text ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            Notify("Cole a chave primeiro.");
            return;
        }
        string model = ResolveLlmTestModel(
            _u.LlmModel.SelectedItem as string, _u.LlmCustom.Text);
        var t = new Translate.LlmTranslator(
            () => key, () => model, () => "", () => "default",
            () => 0, () => 0, () => 0, () => "", () => false, () => null);
        try
        {
            var r = await t.TranslateAsync(
                new System.Collections.Generic.List<string> { "Olá" },
                "pt", "pt", System.Threading.CancellationToken.None);
            Notify(r.Error is not null ? r.Error
                : r.Translations.Count > 0 ? "Chave válida. Resposta: " + r.Translations[0]
                : "Resposta vazia do modelo.");
        }
        catch (System.Exception ex) { Notify("Falha ao testar: " + ex.Message); }
    }

    private async void PickFile(TextBox target, string ext)
    {
        try
        {
            var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (sp is null) return;
            var files = await sp.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions { AllowMultiple = false });
            if (files.Count > 0) target.Text = files[0].Path.LocalPath;
        }
        catch (System.Exception ex) { Notify("Falha ao abrir arquivo: " + ex.Message); }
    }

    private void LoadGroupFields()
    {
        var groups = _cfg.Profile.ColorGroups;
        _u.GroupCount.Text = groups.Count == 1 ? "1 grupo" : $"{groups.Count} grupos";
        if (_u.Groups.SelectedIndex < 0 || _u.Groups.SelectedIndex >= groups.Count) return;
        var g = groups[_u.Groups.SelectedIndex];
        _u.R.Text = g.R.ToString(); _u.G.Text = g.G.ToString(); _u.B.Text = g.B.ToString();
        _u.S1.Text = g.S1.ToString(); _u.S2.Text = g.S2.ToString();
        _u.V1.Text = g.V1.ToString(); _u.V2.Text = g.V2.ToString();
    }
}
