using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gort.Config;
using Gort.Core;
using Gort.Locale;
using Gort.Store;
using Gort.Translate;

namespace Gort.UI;

/// <summary>
/// Painel de opções avançadas (V.3): as 7 abas como controle embutível.
/// Usado na aba Avançado da janela principal e na janela Avançada separada.
/// Edita um clone e grava no Aplicar; restaurar pede confirmação e avisa o
/// hospedeiro para trocar por um painel novo (NeedsRebuild).
/// </summary>
public sealed class AdvancedPanel : UserControl
{
    private readonly ConfigService _cfg;
    private AdvancedOptions _w;
    public event Action? NeedsRebuild;

    // aplicativo
    private CheckBox Tray = new(), Rtl = new(), RemoteTop = new(),
        FollowCompat = new(), FollowOnly = new(), YellowBorder = new();
    private Button SelBg = new(), SelAccent = new();
    private string _selBg = "", _selAccent = "";   // Rebuild preenche antes de qualquer leitura
    // avançados
    private readonly List<(TextBox Keys, TextBox File)> _openProfile = new();
    private TextBox TranspKeys = new();
    private readonly Dictionary<string, TextBox> _svcKeys = new();
    // janela
    private CheckBox OvBgAlpha = new(),
        LayerBottom = new(), LayerRight = new(),
        TopDuring = new(), IgnoreEmpty = new(), HideTranslates = new(),
        DispMem = new();
    private TextBox AutoMin = new(), AutoMax = new(), SnapStay = new(),
        DispN = new(), DispS = new();
    // coletânea
    private readonly List<(string File, CheckBox Box)> _collect = new();
    private TextBlock CollectInfo = new();
    private CheckBox CollectDb = new(), CollectIc = new();
    // tradução
    private CheckBox Bridge = new(), Fallback = new(), CustomSame = new(),
        LlmNoDef = new(), ClipUse = new(), ClipOrig = new(), ClipWork = new();
    private ListBox CustomList = new();
    private TextBox CustomName = new(), CustomUrl = new(), CustomSrc = new(),
        CustomDst = new(), BaseUrl = new();
    private TextBox CustomHeaders = new(), CustomReq = new(), CustomRes = new();
    private RadioButton LlmDef = new(), LlmEco = new(), LlmCustom = new();
    private Slider LlmTemp = new(), LlmReason = new(), LlmMax = new();
    private TextBlock LlmTempLbl = new(), LlmReasonLbl = new(), LlmMaxLbl = new();
    private TextBox LlmInstr = new(), LlmCustomModel = new();
    private ComboBox ClipFormat = new();
    private CustomPreset? _editing;
    // OCR/dicionário
    private CheckBox CloudPriority = new();
    private TextBox DictPasses = new();

    private TextBox DarkFontBox = new();
    private bool _llmWired;
    private bool _appWired, _customWired, _collectWired, _trRadioWired;

    public AdvancedPanel(ConfigService cfg)
    {
        _cfg = cfg;
        _w = Clone(cfg.Advanced);
        Rebuild();
    }

    /// <summary>Dono-janela para diálogos (o painel pode estar em janela ou aba).</summary>
    private Window? DialogOwner => TopLevel.GetTopLevel(this) as Window;

    private readonly List<(TabItem Tab, TextBlock Label)> _subTabs = new();

    private void PaintSubTabs(TabControl tabs)
    {
        foreach (var (tab, label) in _subTabs)
        {
            bool sel = tabs.SelectedItem == tab;
            label.FontSize = 14;
            label.FontWeight = sel ? FontWeight.SemiBold : FontWeight.Regular;
            label.Foreground = new SolidColorBrush(sel
                ? Color.FromRgb(0x1A, 0x1D, 0x26)
                : Color.FromRgb(0x4B, 0x55, 0x63));
        }
    }

    private void Rebuild()
    {
        _w = Clone(_cfg.Advanced);
        InitFieldTexts();
        _selBg = _w.SelectBg;
        _selAccent = _w.SelectAccent;
        _openProfile.Clear();
        _svcKeys.Clear();
        _collect.Clear();
        _editing = null;

        var tabs = new TabControl();
        _subTabs.Clear();
        foreach (var (key, content) in new (string Key, Control Content)[]
                 {
                     ("adv.app_title", TabApp()),
                     ("adv.shortcuts_title", TabShortcuts()),
                     ("adv.win_title", TabWindow()),
                     ("adv.collect_title", TabCollect()),
                     ("adv.tr_title", TabTranslation()),
                     ("adv.ocr_title", TabOcr()),
                     ("adv.dict_title", TabDict()),
                 })
        {
            var label = new TextBlock { Text = Strings._(key) };
            var tab = new TabItem { Header = label, Content = content };
            _subTabs.Add((tab, label));
            tabs.Items.Add(tab);
        }
        tabs.SelectionChanged += (_, _) => PaintSubTabs(tabs);
        Content = tabs;
        PaintSubTabs(tabs);
        RefreshCollect();
        RefreshCustom();
        RefreshLlm();
    }

    private static AdvancedOptions Clone(AdvancedOptions a)
    {
        var c = new AdvancedOptions();
        c.TrayMode = a.TrayMode; c.RightToLeft = a.RightToLeft;
        c.RemoteAlwaysOnTop = a.RemoteAlwaysOnTop;
        c.FollowCompat = a.FollowCompat; c.FollowOnly = a.FollowOnly;
        c.AttachedYellowBorder = a.AttachedYellowBorder;
        c.SelectBg = a.SelectBg; c.SelectAccent = a.SelectAccent;
        c.ToggleForcedTransparency = a.ToggleForcedTransparency;
        foreach (var o in a.OpenProfile)
            c.OpenProfile.Add(new OpenProfileShortcut { Keys = o.Keys, File = o.File });
        foreach (var kv in a.ServiceSwitch) c.ServiceSwitch[kv.Key] = kv.Value;
        c.OverlayBgAlpha = a.OverlayBgAlpha;
        c.SnapshotStaySec = a.SnapshotStaySec;
        c.DarkFont = a.DarkFont;
        c.LayerBottom = a.LayerBottom; c.LayerRight = a.LayerRight;
        c.TopOnlyDuring = a.TopOnlyDuring; c.IgnoreEmpty = a.IgnoreEmpty;
        c.HideAlsoTranslates = a.HideAlsoTranslates;
        c.DisplayMemory = a.DisplayMemory;
        c.DisplayMemoryN = a.DisplayMemoryN; c.DisplayMemorySec = a.DisplayMemorySec;
        foreach (var f in a.CollectActive) c.CollectActive.Add(f);
        c.CollectAsDb = a.CollectAsDb; c.CollectIgnoreCase = a.CollectIgnoreCase;
        c.Bridge = a.Bridge; c.FallbackTranslator = a.FallbackTranslator;
        foreach (var p in a.CustomPresets)
            c.CustomPresets.Add(new CustomPreset
            {
                Name = p.Name, Url = p.Url,
                Headers = new List<string>(p.Headers),
                ReqTemplate = p.ReqTemplate, ResTemplate = p.ResTemplate,
            });
        c.CustomSameCodes = a.CustomSameCodes;
        c.CustomSource = a.CustomSource; c.CustomTarget = a.CustomTarget;
        c.CustomUrl = a.CustomUrl;
        c.LlmInstruction = a.LlmInstruction; c.LlmCustomModel = a.LlmCustomModel;
        c.LlmNoDefault = a.LlmNoDefault; c.LlmPreset = a.LlmPreset;
        c.LlmTemp = a.LlmTemp; c.LlmReason = a.LlmReason; c.LlmMaxOut = a.LlmMaxOut;
        c.ClipboardTranslate = a.ClipboardTranslate;
        c.ClipboardShowOriginal = a.ClipboardShowOriginal;
        c.ClipboardShowWorking = a.ClipboardShowWorking;
        c.ClipboardCopyFormat = a.ClipboardCopyFormat;
        c.CloudPriority = a.CloudPriority;
        c.DictExtraPasses = a.DictExtraPasses;
        c.ForcedTransparency = a.ForcedTransparency;
        return c;
    }

    private void InitFieldTexts()
    {
        LlmInstr.Text = _w.LlmInstruction;
        LlmCustomModel.Text = _w.LlmCustomModel;
        CustomSrc.Text = _w.CustomSource;
        CustomDst.Text = _w.CustomTarget;
        BaseUrl.Text = _w.CustomUrl;
        DarkFontBox.Text = _w.DarkFont;
        ClipFormat.SelectedItem = _w.ClipboardCopyFormat;
    }

    private static StackPanel Sec(params Control[] cs)
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        foreach (var c in cs) p.Children.Add(c);
        return p;
    }

    private static TextBlock H(string t) => GortTheme.SectionTitle(t);

    private static StackPanel Row(params Control[] cs)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var c in cs) p.Children.Add(c);
        return p;
    }

    // ---- aba aplicativo ----

    private Control TabApp()
    {
        Tray.Content = Strings._("adv.tray_mode"); Tray.IsChecked = _w.TrayMode;
        Rtl.Content = Strings._("adv.rtl"); Rtl.IsChecked = _w.RightToLeft;
        RemoteTop.Content = Strings._("adv.remote_top"); RemoteTop.IsChecked = _w.RemoteAlwaysOnTop;
        FollowCompat.Content = Strings._("adv.follow_compat"); FollowCompat.IsChecked = _w.FollowCompat;
        FollowOnly.Content = Strings._("adv.follow_only"); FollowOnly.IsChecked = _w.FollowOnly;
        YellowBorder.Content = Strings._("adv.yellow_border"); YellowBorder.IsChecked = _w.AttachedYellowBorder;
        SelBg.Content = Strings._("adv.sel_bg");
        SelAccent.Content = Strings._("adv.sel_accent");
        if (!_appWired)
        {
            _appWired = true;
            SelBg.Click += (_, _) => PickColor(true, c => { _selBg = c; });
            SelAccent.Click += (_, _) => PickColor(false, c => { _selAccent = c; });
        }
        var preview = new Button { Content = Strings._("adv.sel_preview") };
        preview.Click += (_, _) => PreviewSelection();
        var restore = new Button { Content = Strings._("adv.sel_restore") };
        restore.Click += (_, _) => { _selBg = "#FFFFFFFF"; _selAccent = "#FF000000"; };
        GortTheme.Secondary(SelBg); GortTheme.Secondary(SelAccent);
        GortTheme.Secondary(preview); GortTheme.Secondary(restore);
        return new ScrollViewer
        {
            Content = Sec(Tray, Rtl, RemoteTop, FollowCompat, FollowOnly, YellowBorder,
                H("Cores da seleção"), Row(SelBg, SelAccent, preview, restore)),
        };
    }

    private void PickColor(bool withAlpha, Action<string> onPick)
    {
        var dlg = new Window { Title = "Cor", Width = 340, Height = 420 };
        var picker = new ColorPicker();
        var ok = new Button { Content = "OK", MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            var c = picker.Color;
            onPick(withAlpha ? $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#FF{c.R:X2}{c.G:X2}{c.B:X2}");
            dlg.Close();
        };
        dlg.Content = Sec(picker, ok);
        if (DialogOwner is not null) dlg.ShowDialog(DialogOwner);
        else dlg.Show();
    }

    private void PreviewSelection()
    {
        var app = (App)Application.Current!;
        _ = app.SelectAreaAsync();
    }

    // ---- aba atalhos ----

    private Control TabShortcuts()
    {
        var p = Sec();
        for (int i = 0; i < 4; i++)   // P-119
        {
            while (_w.OpenProfile.Count <= i)
                _w.OpenProfile.Add(new OpenProfileShortcut());
            var keys = new TextBox { Width = 150, Text = _w.OpenProfile[i].Keys };
            var file = new TextBox { Width = 280, Text = _w.OpenProfile[i].File };
            var pick = new Button { Content = Strings._("adv.pick_file") };
            pick.Click += async (_, _) =>
            {
                var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (sp is null) return;
                var fs = await sp.OpenFilePickerAsync(
                    new Avalonia.Platform.Storage.FilePickerOpenOptions());
                if (fs.Count > 0) file.Text = fs[0].Path.LocalPath;
            };
            var clear = new Button { Content = Strings._("hotkeys.clear") };
            clear.Click += (_, _) => { keys.Text = ""; file.Text = ""; };
            GortTheme.Secondary(pick); GortTheme.Secondary(clear);
            _openProfile.Add((keys, file));
            p.Children.Add(Row(new TextBlock
            {
                Text = $"{Strings._("adv.open_profile")} {i + 1}",
                Width = 110,
            }, keys, file, pick, clear));
        }
        TranspKeys.Width = 150;
        TranspKeys.Text = _w.ToggleForcedTransparency;
        p.Children.Add(Row(new TextBlock
        {
            Text = Strings._("adv.forced_transparency"), Width = 220,
        }, TranspKeys));
        string[] svcs = ["db", "sheets", "web-free", "commercial-kr",
            "local-worker", "embedded-browser", "web-nokey"];
        foreach (var s in svcs)
        {
            var keys = new TextBox { Width = 150 };
            _w.ServiceSwitch.TryGetValue(s, out var v);
            keys.Text = v ?? "";
            _svcKeys[s] = keys;
            p.Children.Add(Row(new TextBlock
            {
                Text = $"{Strings._("adv.switch_service")}: {s}",
                Width = 220,
            }, keys));
        }
        return new ScrollViewer { Content = p };
    }

    // ---- aba janela ----

    private static TextBox NumBox(string v, int w = 60)
    {
        var t = new TextBox { Text = v, Width = w };
        t.LostFocus += (_, _) =>
        {
            string d = new string(t.Text?.Where(char.IsDigit).ToArray() ?? []);
            t.Text = d.Length == 0 ? "0" : d;
        };
        return t;
    }

    private Control TabWindow()
    {
        OvBgAlpha.Content = Strings._("adv.use_bg_alpha"); OvBgAlpha.IsChecked = _w.OverlayBgAlpha;
        AutoMin = NumBox(_cfg.Profile.AutoMinPt.ToString());
        AutoMax = NumBox(_cfg.Profile.AutoMaxPt.ToString());
        SnapStay = NumBox(_w.SnapshotStaySec.ToString());
        var darkFont = new Button { Content = Strings._("adv.dark_font") };
        GortTheme.Secondary(darkFont);
        darkFont.Click += (_, _) =>
        {
            try
            {
                var fams = new List<string>();
                foreach (var f in SkiaSharp.SKFontManager.Default.GetFontFamilies())
                    fams.Add(f);
                fams.Sort();
                var box = new Window { Title = "Fonte", Width = 300, Height = 380 };
                var list = new ListBox { ItemsSource = fams };
                list.SelectionChanged += (_, _) =>
                {
                    if (list.SelectedItem is string f) DarkFontBox.Text = f;
                };
                box.Content = list;
                if (DialogOwner is not null) box.ShowDialog(DialogOwner);
                else box.Show();
            }
            catch { }
        };
        LayerBottom.Content = Strings._("adv.layer_bottom"); LayerBottom.IsChecked = _w.LayerBottom;
        LayerRight.Content = Strings._("adv.layer_right"); LayerRight.IsChecked = _w.LayerRight;
        TopDuring.Content = Strings._("adv.top_during"); TopDuring.IsChecked = _w.TopOnlyDuring;
        IgnoreEmpty.Content = Strings._("adv.ignore_empty"); IgnoreEmpty.IsChecked = _w.IgnoreEmpty;
        HideTranslates.Content = Strings._("adv.hide_translates");
        HideTranslates.IsChecked = _w.HideAlsoTranslates;
        DispMem.Content = Strings._("adv.disp_mem"); DispMem.IsChecked = _w.DisplayMemory;
        DispN = NumBox(_w.DisplayMemoryN.ToString());
        DispS = NumBox(_w.DisplayMemorySec.ToString());
        return new ScrollViewer
        {
            Content = Sec(
                H("Sobreposição"), OvBgAlpha,
                Row(new TextBlock { Text = Strings._("adv.min_size") }, AutoMin,
                    new TextBlock { Text = Strings._("adv.max_size") }, AutoMax),
                Row(new TextBlock { Text = Strings._("adv.snapshot_stay") }, SnapStay),
                H("Escuro"), Row(darkFont, DarkFontBox),
                H("Camada"), LayerBottom, LayerRight,
                H("Geral"), TopDuring, IgnoreEmpty, HideTranslates,
                H(Strings._("adv.disp_mem")), DispMem,
                Row(new TextBlock { Text = Strings._("adv.disp_n") }, DispN,
                    new TextBlock { Text = Strings._("adv.disp_s") }, DispS)),
        };
    }

    // ---- aba coletânea ----

    private Control TabCollect()
    {
        var mark = new Button { Content = Strings._("adv.select_all") };
        mark.Click += (_, _) => { foreach (var (_, b) in _collect) b.IsChecked = true; };
        var unmark = new Button { Content = Strings._("adv.unselect_all") };
        unmark.Click += (_, _) => { foreach (var (_, b) in _collect) b.IsChecked = false; };
        CollectDb.Content = Strings._("adv.as_db");
        CollectDb.IsChecked = _w.CollectAsDb;
        if (!_collectWired)
        {
            _collectWired = true;
            CollectDb.IsCheckedChanged += (_, _) => SyncCollectGroup();
        }
        CollectIc.Content = Strings._("adv.db_ignore_case");
        CollectIc.IsChecked = _w.CollectIgnoreCase;
        var list = new StackPanel { Spacing = 4 };
        var panel = Sec(Row(mark, unmark), list,
            new TextBlock { Text = "Informação:" }, CollectInfo,
            CollectDb, CollectIc);
        panel.Tag = list;
        _collectPanel = panel;
        SyncCollectGroup();
        return new ScrollViewer { Content = panel };
    }

    private StackPanel? _collectPanel;

    private void RefreshCollect()
    {
        _collect.Clear();
        if (_collectPanel is null) return;
        if (_collectPanel.Children.Count < 2
            || _collectPanel.Children[1] is not StackPanel list) return;
        list.Children.Clear();
        try
        {
            foreach (var f in Directory.GetFiles(Core.Paths.CollectDir, "*.txt"))
            {
                string name = Path.GetFileName(f);
                var cb = new CheckBox
                {
                    Content = name,
                    IsChecked = _w.CollectActive.Contains(name),
                };
                cb.IsCheckedChanged += (_, _) =>
                    CollectInfo.Text = Collectanea.InfoOf(Path.Combine(Core.Paths.CollectDir, name));
                _collect.Add((name, cb));
                list.Children.Add(cb);
            }
        }
        catch { }
    }

    private void SyncCollectGroup() =>
        CollectIc.IsEnabled = CollectDb.IsChecked == true;

    // ---- aba tradução ----

    private Control TabTranslation()
    {
        Bridge.Content = Strings._("adv.bridge"); Bridge.IsChecked = _w.Bridge;
        Fallback.Content = Strings._("adv.fallback_alt"); Fallback.IsChecked = _w.FallbackTranslator;
        var add = new Button { Content = Strings._("adv.add") };
        add.Click += (_, _) => SaveEditingPreset(selectNew: true);
        var del = new Button { Content = Strings._("adv.remove") };
        GortTheme.Secondary(add); GortTheme.Secondary(del);
        del.Click += (_, _) =>
        {
            if (_editing is not null && !_editing.FromFile)   // RF-528
                _w.CustomPresets.Remove(_editing);
            _editing = null;
            RefreshCustom();
        };
        if (!_customWired)
        {
            _customWired = true;
            CustomList.SelectionChanged += (_, _) =>
            {
                // Trava de reentrância: RefreshCustom troca a lista e
                // redispararia este handler até estourar a pilha (crash).
                if (_refreshingCustom) return;
                SaveEditingPreset(selectNew: false);              // RF-527
                if (CustomList.SelectedItem is string name)
                    _editing = FindPreset(name);
                LoadEditingPreset();
            };
        }
        CustomSame.Content = Strings._("adv.custom_same_codes");
        CustomSame.IsChecked = _w.CustomSameCodes;
        LlmDef.Content = Strings._("adv.llm_preset_default");
        LlmEco.Content = Strings._("adv.llm_preset_eco");
        LlmCustom.Content = Strings._("adv.llm_preset_custom");
        LlmDef.GroupName = LlmEco.GroupName = LlmCustom.GroupName = "llm";
        LlmDef.IsChecked = _w.LlmPreset != "eco" && _w.LlmPreset != "custom";
        LlmEco.IsChecked = _w.LlmPreset == "eco";
        LlmCustom.IsChecked = _w.LlmPreset == "custom";
        if (!_trRadioWired)
        {
            _trRadioWired = true;
            LlmDef.IsCheckedChanged += (_, _) => RefreshLlm();    // RF-525
            LlmEco.IsCheckedChanged += (_, _) => RefreshLlm();
            LlmCustom.IsCheckedChanged += (_, _) => RefreshLlm();
        }
        // (assinatura única: Rebuild reutiliza os controles; RefreshLlm é idempotente)
        LlmTemp.Minimum = 0; LlmTemp.Maximum = 100;
        LlmReason.Minimum = 0; LlmReason.Maximum = 3;
        LlmMax.Minimum = 500; LlmMax.Maximum = 10000;
        LlmNoDef.Content = Strings._("adv.llm_no_default");
        LlmNoDef.IsChecked = _w.LlmNoDefault;
        ClipUse.Content = Strings._("adv.clip_use");
        ClipUse.IsChecked = _w.ClipboardTranslate;
        ClipOrig.Content = Strings._("adv.clip_orig");
        ClipOrig.IsChecked = _w.ClipboardShowOriginal;
        ClipWork.Content = Strings._("adv.clip_working");
        ClipWork.IsChecked = _w.ClipboardShowWorking;
        ClipFormat.ItemsSource = new List<string> { "ocr-only", "translation-only", "both" };
        return new ScrollViewer
        {
            Content = Sec(Bridge, Fallback,
                H("API personalizada"),
                Row(CustomList, new StackPanel
                {
                    Spacing = 4,
                    Children = { add, del },
                }),
                Row(new TextBlock { Text = "Nome", Width = 150 }, CustomName),
                Row(new TextBlock { Text = Strings._("adv.custom_url"), Width = 150 }, CustomUrl),
                Row(new TextBlock { Text = Strings._("adv.custom_headers"), Width = 150 }, CustomHeaders),
                Row(new TextBlock { Text = Strings._("adv.custom_req"), Width = 150 }, CustomReq),
                Row(new TextBlock { Text = Strings._("adv.custom_res"), Width = 150 }, CustomRes),
                CustomSame,
                Row(new TextBlock { Text = "Origem", Width = 150 }, CustomSrc,
                    new TextBlock { Text = "Destino" }, CustomDst),
                Row(new TextBlock { Text = "URL base", Width = 150 }, BaseUrl),
                H("Modelo de linguagem"),
                Row(new TextBlock { Text = Strings._("adv.llm_instruction"), Width = 150 }, LlmInstr),
                Row(new TextBlock { Text = Strings._("adv.llm_custom_model"), Width = 150 }, LlmCustomModel),
                LlmNoDef,
                Row(LlmDef, LlmEco, LlmCustom),
                Row(new TextBlock { Text = Strings._("adv.llm_temp"), Width = 150 }, LlmTemp, LlmTempLbl),
                Row(new TextBlock { Text = Strings._("adv.llm_reason"), Width = 150 }, LlmReason, LlmReasonLbl),
                Row(new TextBlock { Text = Strings._("adv.llm_maxout"), Width = 150 }, LlmMax, LlmMaxLbl),
                H("Área de transferência"),
                ClipUse, ClipOrig, ClipWork,
                Row(new TextBlock { Text = Strings._("adv.clip_format"), Width = 150 }, ClipFormat)),
        };
    }

    private CustomPreset? FindPreset(string display)
    {
        string name = display.StartsWith("[arq] ") ? display[6..] : display;
        return _w.CustomPresets.FirstOrDefault(p => p.Name == name);
    }

    private void SaveEditingPreset(bool selectNew)
    {
        if (_editing is null && selectNew)
        {
            string name = CustomApiService.UniqueName(_w.CustomPresets,   // RF-529
                CustomName.Text?.Length > 0 ? CustomName.Text : "preset");
            _editing = new CustomPreset { Name = name };
            _w.CustomPresets.Add(_editing);
        }
        if (_editing is not null && !_editing.FromFile)       // RF-528: arquivo é só-leitura
        {
            _editing.Url = CustomUrl.Text ?? "";
            _editing.Headers = (CustomHeaders.Text ?? "")
                .Split('\n').Select(s => s.Trim())
                .Where(s => s.Length > 0).ToList();
            _editing.ReqTemplate = CustomReq.Text ?? "";
            _editing.ResTemplate = CustomRes.Text ?? "";
        }
        RefreshCustom();
    }

    private void LoadEditingPreset()
    {
        if (_editing is null) return;
        CustomName.Text = _editing.Name;
        CustomName.IsReadOnly = _editing.FromFile;            // RF-528
        CustomUrl.Text = _editing.Url;
        CustomHeaders.Text = string.Join("\n", _editing.Headers);
        CustomReq.Text = _editing.ReqTemplate;
        CustomRes.Text = _editing.ResTemplate;
    }

    private bool _refreshingCustom;

    private void RefreshCustom()
    {
        var items = new List<string>();
        foreach (var f in CustomApiService.LoadPresetFiles(
                     System.IO.Path.Combine(Core.Paths.BaseDir, "custom-presets")))
            if (!_w.CustomPresets.Any(p => p.Name == f.Name))
                _w.CustomPresets.Add(f);
        foreach (var p in _w.CustomPresets)
            items.Add(p.FromFile ? "[arq] " + p.Name : p.Name);   // RF-528
        _refreshingCustom = true;
        try { CustomList.ItemsSource = items; }
        finally { _refreshingCustom = false; }
    }

    private void RefreshLlm()
    {
        bool custom = LlmCustom.IsChecked == true;
        string preset = LlmEco.IsChecked == true ? "eco" : custom ? "custom" : "default";
        (int t, int r, int m) = preset switch
        {
            "eco" => (Core.Params.P67_LlmTempEco, Core.Params.P68_LlmReasonEco,
                Core.Params.P69_LlmMaxOutEco),
            "custom" => ((int)LlmTemp.Value, (int)LlmReason.Value, (int)LlmMax.Value),
            _ => (Core.Params.P64_LlmTempDefault, Core.Params.P65_LlmReasonDefault,
                Core.Params.P66_LlmMaxOutDefault),
        };
        if (!custom)   // RF-525: mostra o preset e desabilita
        {
            LlmTemp.Value = t; LlmReason.Value = r; LlmMax.Value = m;
        }
        LlmTemp.IsEnabled = LlmReason.IsEnabled = LlmMax.IsEnabled = custom;
        LlmTempLbl.Text = (LlmTemp.Value / 100).ToString("F2");   // RF-526
        LlmReasonLbl.Text = ((int)LlmReason.Value) switch
        {
            0 => "mínimo", 1 => "baixo", 2 => "médio", _ => "alto",
        };
        LlmMaxLbl.Text = ((int)LlmMax.Value).ToString();
        if (!_llmWired)
        {
            _llmWired = true;
            LlmTemp.PropertyChanged += (_, _) => LlmTempLbl.Text = (LlmTemp.Value / 100).ToString("F2");
        }
    }

    // ---- abas OCR/dicionário ----

    private Control TabOcr()
    {
        CloudPriority.Content = Strings._("ocr.cloud_priority");
        CloudPriority.IsChecked = _w.CloudPriority;
        return Sec(CloudPriority);
    }

    private Control TabDict()
    {
        DictPasses.Width = 60;
        DictPasses.Text = _w.DictExtraPasses.ToString();
        return Sec(Row(new TextBlock { Text = Strings._("dict.passes") }, DictPasses));
    }

    // ---- aplicar/restaurar ----

    /// <summary>Grava o clone no perfil (o hospedeiro decide fechar ou não).</summary>
    public void Apply()
    {
        SaveEditingPreset(selectNew: false);
        var a = _cfg.Advanced;
        a.TrayMode = Tray.IsChecked == true;
        a.RightToLeft = Rtl.IsChecked == true;
        a.RemoteAlwaysOnTop = RemoteTop.IsChecked == true;
        a.FollowCompat = FollowCompat.IsChecked == true;
        a.FollowOnly = FollowOnly.IsChecked == true;
        a.AttachedYellowBorder = YellowBorder.IsChecked == true;
        a.SelectBg = _selBg; a.SelectAccent = _selAccent;
        for (int i = 0; i < 4 && i < _openProfile.Count; i++)
        {
            while (a.OpenProfile.Count <= i)
                a.OpenProfile.Add(new OpenProfileShortcut());
            a.OpenProfile[i].Keys = _openProfile[i].Keys.Text ?? "";
            a.OpenProfile[i].File = _openProfile[i].File.Text ?? "";
        }
        a.ToggleForcedTransparency = TranspKeys.Text ?? "";
        a.ServiceSwitch.Clear();
        foreach (var (svc, box) in _svcKeys)
            if ((box.Text ?? "") != "") a.ServiceSwitch[svc] = box.Text!;
        a.OverlayBgAlpha = OvBgAlpha.IsChecked == true;
        if (double.TryParse(AutoMin.Text, out var mn)
            && double.TryParse(AutoMax.Text, out var mx))   // RF-524
        {
            if (mn > mx) mx = mn;
            if (mx < mn) mn = mx;
            // Tamanho mín/máx mora no perfil (o overlay lê de lá).
            var p = _cfg.Profile;
            p.AutoMinPt = mn; p.AutoMaxPt = mx;
        }
        if (int.TryParse(SnapStay.Text, out var ss)) a.SnapshotStaySec = Math.Max(0, ss);
        a.DarkFont = DarkFontBox.Text ?? "";
        a.LayerBottom = LayerBottom.IsChecked == true;
        a.LayerRight = LayerRight.IsChecked == true;
        a.TopOnlyDuring = TopDuring.IsChecked == true;
        a.IgnoreEmpty = IgnoreEmpty.IsChecked == true;
        a.HideAlsoTranslates = HideTranslates.IsChecked == true;
        a.DisplayMemory = DispMem.IsChecked == true;
        if (int.TryParse(DispN.Text, out var dn)) a.DisplayMemoryN = Math.Clamp(dn, 1, 10);
        if (int.TryParse(DispS.Text, out var ds)) a.DisplayMemorySec = Math.Clamp(ds, 1, 200);
        a.CollectActive.Clear();
        foreach (var (f, b) in _collect)
            if (b.IsChecked == true) a.CollectActive.Add(f);
        a.CollectAsDb = CollectDb.IsChecked == true;
        a.CollectIgnoreCase = CollectIc.IsChecked == true;
        a.Bridge = Bridge.IsChecked == true;
        a.FallbackTranslator = Fallback.IsChecked == true;
        a.CustomPresets.Clear();
        foreach (var p in _w.CustomPresets) a.CustomPresets.Add(p);
        a.CustomSameCodes = CustomSame.IsChecked == true;
        a.CustomSource = CustomSrc.Text ?? a.CustomSource;
        a.CustomTarget = CustomDst.Text ?? a.CustomTarget;
        a.CustomUrl = BaseUrl.Text ?? a.CustomUrl;
        a.LlmInstruction = LlmInstr.Text ?? "";
        a.LlmCustomModel = LlmCustomModel.Text ?? a.LlmCustomModel;
        a.LlmNoDefault = LlmNoDef.IsChecked == true;
        a.LlmPreset = LlmEco.IsChecked == true ? "eco"
            : LlmCustom.IsChecked == true ? "custom" : "default";
        a.LlmTemp = (int)LlmTemp.Value;
        a.LlmReason = (int)LlmReason.Value;
        a.LlmMaxOut = (int)LlmMax.Value;
        a.ClipboardTranslate = ClipUse.IsChecked == true;
        a.ClipboardShowOriginal = ClipOrig.IsChecked == true;
        a.ClipboardShowWorking = ClipWork.IsChecked == true;
        a.ClipboardCopyFormat = (ClipFormat.SelectedItem as string) ?? "ocr-only";
        a.CloudPriority = CloudPriority.IsChecked == true;
        if (int.TryParse(DictPasses.Text, out var dp)) a.DictExtraPasses = Math.Clamp(dp, 0, 3);
        _cfg.SaveAdvanced();
        _cfg.SaveProfile();   // min/max moram no perfil desde a unificação
        var app = (App)Application.Current!;
        app.CurrentLoop?.ReloadDict();   // RF-531: dicionário sem reiniciar
        if (app.Remote is not null)
            app.Remote.Topmost = a.RemoteAlwaysOnTop;
    }

    /// <summary>Restaura os padrões com confirmação; depois reconstrói.</summary>
    public async void RestoreDefaults()
    {
        var box = new Window
        {
            Title = "GORT", Width = 360, Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16), Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "Restaurar os padrões das opções avançadas?" },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children =
                        {
                            new Button { Content = "Sim", MinWidth = 80 },
                            new Button { Content = "Não", MinWidth = 80 },
                        },
                    },
                },
            },
        };
        bool yes = false;
        var row = (StackPanel)((StackPanel)box.Content!).Children[1];
        ((Button)row.Children[0]).Click += (_, _) => { yes = true; box.Close(); };
        ((Button)row.Children[1]).Click += (_, _) => box.Close();
        if (DialogOwner is not null) await box.ShowDialog(DialogOwner);
        else { box.Show(); return; }
        if (!yes) return;   // RF-530: com confirmação
        var fresh = new AdvancedOptions();
        // RF-532: direção derivada da propriedade do idioma de destino.
        fresh.RightToLeft = Translate.LangCodes.IsRtl(_cfg.Profile.TargetLanguage);
        _cfg.Advanced.TrayMode = fresh.TrayMode;
        _cfg.Advanced.RightToLeft = fresh.RightToLeft;
        _cfg.SaveAdvanced();
        Rebuild();   // controles voltam aos padrões; o hospedeiro se ajusta
        NeedsRebuild?.Invoke();
    }
}
