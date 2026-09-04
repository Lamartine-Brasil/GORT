using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gort.Config;
using Gort.Lifecycle;
using Gort.Locale;
using Gort.Translate;

namespace Gort;

public partial class MainWindow
{
    // ================= assistente (RF-515/516) =================

    // Aba Traduzir, topo: as 3 ações de começo num lugar só — áreas,
    // ciclo único e assistente — em vez de espalhadas por abas e janelas.
    // Mais o botão grande de iniciar/parar e o estado das áreas: quem abre
    // pela primeira vez entende em segundos o que falta fazer.
    private Control BuildQuickStart()
    {
        var p = new StackPanel { Margin = new Thickness(12), Spacing = 4 };
        p.Children.Add(UI.GortTheme.SectionTitle("Começar"));
        var areas = new Button { Content = "Gerenciar áreas de OCR…" };
        areas.Click += (_, _) => ((App)Avalonia.Application.Current!).OpenAreas();
        var once = new Button { Content = "Traduzir uma vez" };
        once.Click += OnTranslateOnce;
        var restart = new Button { Content = "Configuração rápida" };
        restart.Click += (_, _) => { _u.WizStep = 0; RenderWizard(); };
        // Reabre o controle remoto se foi escondido pelo × (RF-521).
        var remote = new Button { Content = "Mostrar controle remoto" };
        remote.Click += (_, _) => ((App)Avalonia.Application.Current!).ShowRemote();
        UI.GortTheme.Secondary(areas); UI.GortTheme.Secondary(once); UI.GortTheme.Secondary(restart);
        UI.GortTheme.Secondary(remote);
        p.Children.Add(Wrap(areas, once, restart, remote));
        _u.AreaStatus.FontSize = 12;
        p.Children.Add(_u.AreaStatus);
        _u.StartBtn.MinHeight = 44;
        _u.StartBtn.FontWeight = FontWeight.Bold;
        _u.StartBtn.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        UI.GortTheme.Primary(_u.StartBtn);
        _u.StartBtn.Click += (_, _) =>
            ((App)Avalonia.Application.Current!).ToggleLoopPublic();
        p.Children.Add(_u.StartBtn);
        p.Children.Add(_u.Wizard);
        return p;
    }

    private static WrapPanel Wrap(params Control[] cs)
    {
        var w = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 4 };
        foreach (var c in cs) w.Children.Add(c);
        return w;
    }

    private void BuildWizard() => RenderWizard();

    private void RenderWizard()
    {
        _u.Wizard.Children.Clear();
        var next = new Button { MinWidth = 140 };
        UI.GortTheme.Primary(next);
        if (_u.WizStep == 0)
        {
            _u.Wizard.Children.Add(new TextBlock { Text = Strings._("quick.color") });
            var dark = new RadioButton { Content = Strings._("quick.color_dark"), GroupName = "wiz" };
            var light = new RadioButton { Content = Strings._("quick.color_light"), GroupName = "wiz" };
            var unk = new RadioButton { Content = Strings._("quick.color_unknown"), GroupName = "wiz" };
            if (_u.WizColor == "dark") dark.IsChecked = true;
            else if (_u.WizColor == "light") light.IsChecked = true;
            else unk.IsChecked = true;
            dark.IsCheckedChanged += (_, _) => { if (dark.IsChecked == true) _u.WizColor = "dark"; };
            light.IsCheckedChanged += (_, _) => { if (light.IsChecked == true) _u.WizColor = "light"; };
            unk.IsCheckedChanged += (_, _) => { if (unk.IsChecked == true) _u.WizColor = ""; };
            _u.Wizard.Children.Add(dark); _u.Wizard.Children.Add(light); _u.Wizard.Children.Add(unk);
            next.Content = Strings._("quick.next");
            next.Click += (_, _) => { _u.WizStep = 1; RenderWizard(); };
        }
        else if (_u.WizStep == 1)
        {
            _u.Wizard.Children.Add(new TextBlock
            {
                Text = Strings._("quick.area"),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            var sel = new Button { Content = Strings._("quick.select") };
            sel.Click += async (_, _) =>
            {
                var app = (App)Avalonia.Application.Current!;
                var r = await app.SelectAreaAsync();
                if (r.HasValue)
                {
                    _regions.BeginManage();
                    _regions.AddArea(r.Value, exclusion: false);
                    _regions.ApplyWorking();   // vale de imediato, mesmo sem concluir
                    _u.WizStep = 2;
                    RenderWizard();
                }
            };
            _u.Wizard.Children.Add(sel);
            next.Content = "Pular";
            next.Click += (_, _) => { _u.WizStep = 2; RenderWizard(); };
        }
        else if (_u.WizStep == 2)
        {
            // Confirmação: resumo (o gerenciamento abre sob demanda).
            // Sem molduras durante o assistente, a janela fica acessível (RF-515).
            int n = _regions.Managing ? _regions.Working.Count : _regions.Areas.Count;
            _u.Wizard.Children.Add(new TextBlock { Text = $"{n} área(s) de OCR." });
            var manage = new Button { Content = "Gerenciar áreas de OCR…" };
            manage.Click += (_, _) => ((App)Avalonia.Application.Current!).OpenAreas();
            _u.Wizard.Children.Add(manage);
            next.Content = Strings._("quick.next");
            next.Click += (_, _) => { FinishWizard(); _u.WizStep = 3; RenderWizard(); };
        }
        else
        {
            _u.Wizard.Children.Add(new TextBlock { Text = Strings._("quick.finish_links") });
            var docs = new Button { Content = Strings._("help.manual") };
            docs.Click += (_, _) => OpenUrl(Catalogs.Links.Manual);
            _u.Wizard.Children.Add(docs);
            next.Content = Strings._("quick.done");
            next.Click += (_, _) =>
            {
                if (_regions.Managing) _regions.CancelManage();
                _u.WizStep = 0;
                RenderWizard();
            };
        }
        _u.Wizard.Children.Add(next);
    }

    private void FinishWizard()
    {
        var app = (App)Avalonia.Application.Current!;
        app.Controller.RequestStop(Core.Params.P03_LoopWaitMs);   // RF-516: para
        var p = _cfg.Profile;
        string lang = p.OcrLanguage;

        // Motor: moderno se disponível; senão SO com o idioma; senão clássico.
        var modern = Ocr.OcrEngines.Get("modern");
        var os = Ocr.OcrEngines.Get("os");
        var classic = Ocr.OcrEngines.Get("classic");
        if (modern is not null && modern.IsAvailable) p.OcrEngine = "modern";
        else if (os is not null && os.IsAvailable
            && os.SupportedOcrLanguages().Contains(
                Translate.LangCodes.All.First(e => e.Key == lang).Ocr))
            p.OcrEngine = "os";
        else if (classic is not null) p.OcrEngine = "classic";

        // Serviço: inglês → web gratuita; japonês → worker se houver, senão web.
        string key = lang == "ja" || Translate.LangCodes.KeyForOcr(
            Translate.LangCodes.All.First(e => e.Key == lang).Ocr) == "ja" ? "ja" : "en";
        var worker = Translate.Services.Get("local-worker");
        p.TranslationService = key == "ja" && worker is not null ? "local-worker" : "web-free";
        p.OcrLanguage = key == "ja" ? "ja" : "en";
        p.TargetLanguage = Translate.LangCodes.DefaultTarget;     // RF-314

        // Automáticos por propriedade (RF-148) + filtro HSV (RF-119/P-26..28).
        p.Normalize(out _, deriveLangDefaults: true);
        p.ColorGroups.Clear();
        if (_u.WizColor == "dark")
        {
            foreach (var g in Imaging.Preprocess.QuickGroups(darkText: true))
                p.ColorGroups.Add(g);
            p.ColorFilter = "hsv";
        }
        else if (_u.WizColor == "light")
        {
            foreach (var g in Imaging.Preprocess.QuickGroups(darkText: false))
                p.ColorGroups.Add(g);
            p.ColorFilter = "hsv";
        }
        else p.ColorFilter = "none";
        if (p.ColorGroups.Count == 0) p.ColorGroups.Add(new ColorGroup());
        for (int i = 0; i < p.ColorGroups.Count; i++)
            foreach (var a in _regions.Managing ? _regions.Working : _regions.Areas)
                if (!a.Groups.Contains(i)) a.Groups.Add(i);

        p.CaptureActiveWindow = false;
        p.WindowMode = "layer";                                   // RF-516: força camada
        p.Speed = 1;                                              // mais rápida
        if (_regions.Managing) _regions.ApplyManage();
        else _regions.CommitToProfile();
        _cfg.SaveProfile();
        LoadUiFromConfig();
        app.ReloadHotkeys();
    }

    // ================= carga/aplicação =================

    private void SetCombo(ComboBox box, List<string> items, string current)
    {
        box.ItemsSource = items;
        int i = items.IndexOf(current);
        box.SelectedIndex = i >= 0 ? i : (items.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// Combo de idioma por motor: explica para que serve e desabilita quando
    /// só há uma opção (ex.: moderno sem o par japonês mostra só "en").
    /// </summary>
    private void SetLangCombo(ComboBox box, List<string> items, string current,
        string singleTip)
    {
        SetCombo(box, items, current);
        if (items.Count > 1)
        {
            box.IsEnabled = true;
            Avalonia.Controls.ToolTip.SetTip(box,
                "Idioma usado por este motor. Os botões Inglês/Japonês no topo valem para todos.");
        }
        else
        {
            box.IsEnabled = false;
            Avalonia.Controls.ToolTip.SetTip(box, singleTip);
        }
    }

    /// <summary>Seleciona pelo identificador (itens "id — nome").</summary>
    private static void SetComboById(ComboBox box, string id)
    {
        if (box.ItemsSource is System.Collections.IEnumerable items)
        {
            int i = 0;
            foreach (var o in items)
            {
                string s = o as string ?? "";
                if (s == id || s.StartsWith(id + " ")) { box.SelectedIndex = i; return; }
                i++;
            }
        }
        box.SelectedIndex = -1;
    }

    private static string ComboValue(ComboBox box)
    {
        string s = box.SelectedItem as string ?? "";
        int cut = s.IndexOf(' ');
        return cut > 0 ? s[..cut] : s;
    }

    // Primeiro acesso: estado das áreas em linguagem humana + botão
    // grande espelhando a barrinha (mesmos ▶/■ e verde traduzindo).
    private void RefreshAreaStatus()
    {
        int n = _regions.Areas.Count;
        _u.AreaStatus.Text = n == 0
            ? "⚠ Nenhuma área definida — use o assistente ou 'Gerenciar áreas de OCR…' para começar."
            : $"✔ {n} área(s) pronta(s) — pronta para iniciar.";
    }

    private void RefreshStartBtn()
    {
        // Fora do app real (testes headless) não há controlador: mostra ocioso.
        if (Avalonia.Application.Current is not App app)
        {
            _u.StartBtn.Content = "▶ Iniciar tradução";
            _u.StartBtn.Background = new Avalonia.Media.SolidColorBrush(
                UI.GortTheme.Accent);
            return;
        }
        bool idle = app.Controller.State == Lifecycle.LoopState.Idle;
        _u.StartBtn.Content = idle ? "▶ Iniciar tradução" : "■ Parar tradução";
        _u.StartBtn.Background = idle
            ? new Avalonia.Media.SolidColorBrush(UI.GortTheme.Accent)
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(46, 160, 67));
    }

    // Público para a bandeja (RF-017) ressincronizar a UI após toggles.
    public void LoadUiFromConfig()
    {
        var p = _cfg.Profile;
        var a = _cfg.Advanced;
        SetComboById(_u.OcrEngine, p.OcrEngine);
        // Rádios en/ja (fonte única da verdade) + combos por motor acompanham.
        _u.OcrEn.IsChecked = p.OcrLanguage != "ja";
        _u.OcrJa.IsChecked = p.OcrLanguage == "ja";
        _u.ShowOcr.IsChecked = p.ShowOcrText;
        _u.SaveFile.IsChecked = p.SaveResultFile;
        _u.CopyClip.IsChecked = p.CopyToClipboard;
        _u.Dataset.Text = p.ClassicDataset;
        SetLangCombo(_u.OcrLangClassic, ["en", "ja"], p.OcrLanguage,
            "Só há uma opção disponível.");
        _u.Fast.IsChecked = p.ClassicFast;
        SetLangCombo(_u.OsLang, [],
            "",
            "Nenhum idioma de OCR instalado no sistema.");
        SetLangCombo(_u.ModernLang,
            Ocr.OcrEngines.Get("modern")?.SupportedOcrLanguages()
                .Select(LangCodes.KeyForOcr).Where(k => k != "").ToList() ?? ["en"],
            p.OcrLanguage,
            "Só inglês disponível neste motor. O japonês exige o par de modelos japoneses.");
        _u.ModernVertical.IsChecked = p.ModernVertical;
        SetLangCombo(_u.CloudLang, ["auto", "en", "ja"],
            p.OcrLanguage == "ja" ? "ja" : "en",
            "Só há uma opção disponível.");
        _u.CloudCred.Text = p.CloudCredFile;
        var cloud = Ocr.OcrEngines.Get("cloud") as Ocr.Cloud.CloudEngine;
        _u.CloudUsage.Text = $"{Strings._("ocr.cloud_usage")}: {cloud?.UsageText() ?? "—"}";
        _u.CloudPriority.IsChecked = a.CloudPriority;
        SetLangCombo(_u.VenvLang, ["en", "ja"], p.OcrLanguage,
            "Só há uma opção disponível.");

        SetComboById(_u.TrService,
            Translate.Services.ResolveOrFallback(p.TranslationService));
        SetComboById(_u.WebQuality, p.WebQuality);   // auto|high|low (padrão auto)
        _u.DbFile.Text = p.DbFile;
        _u.DbIgnore.IsChecked = p.DbIgnoreCase;
        _u.DbPartial.IsChecked = p.DbPartial;
        _u.WebState.Text = "qualidade normal";
        _u.KrId.Text = ""; _u.KrSecret.Text = "";
        _u.SheetId.Text = p.SheetsSheetId;
        _u.SheetsToken.Text = "token: " +
            (new Translate.SheetsTranslator(() => "", () => false).HasToken() ? "ok" : "ausente");
        _u.BrowserState.Text = "ocioso";
        _u.EuKey.Text = "";
        (_u.EuFree.IsChecked, _u.EuPaid.IsChecked) =
            p.DeepLEndpoint == "paid" ? (false, true) : (true, false);
        _u.LlmKey.Text = "";
        SetCombo(_u.LlmModel,
            Translate.RemoteDefaults.LlmModels.Concat(["custom"]).ToList(),
            p.LlmModel == "" ? Translate.RemoteDefaults.LlmDefaultModel
                : p.LlmModel == "custom" ? "custom" : p.LlmModel);
        _u.LlmCustom.Text = a.LlmCustomModel;

        _u.DictUse.IsChecked = p.UseDict;
        _u.DictFile.Text = p.DictFile;
        _u.DictWord.IsChecked = p.DictByWord;
        _u.FRgb.IsChecked = p.ColorFilter == "rgb";
        _u.FHsv.IsChecked = p.ColorFilter == "hsv";
        _u.FThr.IsChecked = p.ColorFilter == "threshold";
        _u.Threshold.Text = p.Threshold.ToString();
        _u.Erode.IsChecked = p.Erode;
        RefreshGroups();

        _u.FontFam.Text = p.FontFamily;
        _u.FontSize.Text = p.FontSize.ToString();
        _u.CText = (byte[])p.TextColor.Clone();
        _u.CC1 = (byte[])p.Outline1.Clone();
        _u.CC2 = (byte[])p.Outline2.Clone();
        _u.CBg = (byte[])p.BgColor.Clone();
        PaintSwatches();
        _u.Center.IsChecked = p.TextOrder == "center";
        _u.RmSpaces.IsChecked = p.RemoveSpaces;
        _u.UseBg.IsChecked = p.TextBackground;
        _u.AreaNum.IsChecked = p.AreaNumbering;
        _u.Outline.IsChecked = p.OverlayOutline;
        RenderPreview();

        _u.ActiveWin.IsChecked = p.CaptureActiveWindow;
        _u.Zoom.Text = p.Zoom.ToString();
        for (int i = 0; i < 5; i++) _u.Speeds[i].IsChecked = p.Speed == i + 1;
        _u.MDark.IsChecked = p.WindowMode == "dark";
        _u.MLayer.IsChecked = p.WindowMode == "layer";
        _u.MOverlay.IsChecked = p.WindowMode == "overlay";
        _u.Top.IsChecked = _cfg.App.TranslationAlwaysOnTop;
        _u.LayerFit.IsChecked = p.LayerAutoFit;
        _u.LayerMaxW.Text = p.LayerMaxW.ToString();
        _u.LayerMaxH.Text = p.LayerMaxH.ToString();
        _u.CheckUpdate.IsChecked = _cfg.App.CheckUpdate;
        _u.BasicDefault.IsChecked = _cfg.App.BasicTabDefault;

        foreach (var (svc, pair) in _u.LangPairs)
        {
            _cfg.Profile.ServiceSource.TryGetValue(svc, out var src);
            _cfg.Profile.ServiceTarget.TryGetValue(svc, out var dst);
            var names = Translate.LangCodes.All
                .Where(e => e.Svc.TryGetValue(svc, out var c) && c != "")
                .Select(e => e.Key).ToList();
            // Padrão: origem inglês, destino pt-BR (RF-309/RF-314). Lista de
            // destino com pt-BR primeiro (RF-313).
            var dstNames = names.OrderByDescending(k =>
                k == Translate.LangCodes.DefaultTarget).ToList();
            pair.Dst.ItemsSource = dstNames;
            SetCombo(pair.Src, names, string.IsNullOrEmpty(src) ? "en" : src);
            SetCombo(pair.Dst, dstNames, dst ?? Translate.LangCodes.DefaultTarget);
        }
        _u.Tts.IsChecked = p.Tts;
        _u.TtsWait.IsChecked = p.TtsWait;

        foreach (var (act, field) in _u.Hotkeys)
        {
            _cfg.Shortcuts.Map.TryGetValue(act, out var s);
            field.Text = s ?? DefaultFor(act);
        }
        ShowOcrPanel();
        ShowTrPanel();
        RefreshAreaStatus();
        RefreshStartBtn();
    }

    private bool _groupBusy;

    private void RefreshGroups()
    {
        var items = new List<string>();
        for (int i = 0; i < _cfg.Profile.ColorGroups.Count; i++)
        {
            var g = _cfg.Profile.ColorGroups[i];
            items.Add($"[{i}] R {g.R} G {g.G} B {g.B} S {g.S1}–{g.S2} V {g.V1}–{g.V2}");
        }
        // RF-507: adicionar/remover nas duas primeiras posições.
        items.Insert(0, "[remover este grupo]");
        items.Insert(0, "[adicionar grupo]");
        _groupBusy = true;
        _u.Groups.ItemsSource = items;
        _u.Groups.SelectedIndex = _cfg.Profile.ColorGroups.Count > 0 ? 2 : -1;
        _groupBusy = false;
        LoadGroupFields();
    }

    private void GroupComboPicked()
    {
        if (_groupBusy) return;
        int sel = _u.Groups.SelectedIndex;
        if (sel == 0) { _regions.AddColorGroup(); RefreshGroups(); return; }  // RF-507
        if (sel == 1)
        {
            if (_cfg.Profile.ColorGroups.Count > 1)   // remoção ignorada se único
                _regions.RemoveColorGroup(_cfg.Profile.ColorGroups.Count - 1);
            RefreshGroups();
            return;
        }
    }

    private void ApplyFromUi()
    {
        var p = _cfg.Profile;
        var a = _cfg.Advanced;
        string ocrId = OcrId();
        if (ocrId != "") p.OcrEngine = ocrId;              // vazio = mantém
        else if (p.OcrEngine == "") p.OcrEngine = "modern";
        p.ShowOcrText = _u.ShowOcr.IsChecked == true;
        p.SaveResultFile = _u.SaveFile.IsChecked == true;
        p.CopyToClipboard = _u.CopyClip.IsChecked == true;
        p.ClassicDataset = _u.Dataset.Text ?? "eng";
        // Rádios mandam no idioma; combos por motor só valem se o rádio não
        // foi tocado (mantém compatibilidade com perfis antigos).
        if (_u.OcrJa.IsChecked == true) p.OcrLanguage = "ja";
        else if (_u.OcrEn.IsChecked == true) p.OcrLanguage = "en";
        else if (OcrId() == "classic" && ComboValue(_u.OcrLangClassic) is string cl && cl != "")
            p.OcrLanguage = cl;
        p.ClassicFast = _u.Fast.IsChecked == true;
        if (OcrId() == "modern" && ComboValue(_u.ModernLang) is string ml && ml != "")
            p.OcrLanguage = ml;
        p.ModernVertical = _u.ModernVertical.IsChecked == true;
        p.CloudCredFile = _u.CloudCred.Text ?? "";
        a.CloudPriority = _u.CloudPriority.IsChecked == true;
        if (OcrId() == "venv" && ComboValue(_u.VenvLang) is string vl && vl != "")
            p.OcrLanguage = vl;
        if (p.OcrLanguage == "") p.OcrLanguage = "en";          // padrão: inglês
        if (p.TargetLanguage == "")                             // padrão: português BR
            p.TargetLanguage = Translate.LangCodes.DefaultTarget;

        string trId = TrId();
        if (trId != "") p.TranslationService = trId;       // padrão: web gratuita
        else if (p.TranslationService == "") p.TranslationService = "web-free";
        p.WebQuality = ComboValue(_u.WebQuality);          // padrão: automática
        if (p.WebQuality == "") p.WebQuality = "auto";
        p.DbFile = _u.DbFile.Text ?? "empty.txt";
        p.DbIgnoreCase = _u.DbIgnore.IsChecked == true;
        p.DbPartial = _u.DbPartial.IsChecked == true;
        if ((_u.KrId.Text ?? "") != "")
            SaveKey("commercial-kr", _u.KrId.Text!, _u.KrSecret.Text ?? "", "free");
        p.SheetsSheetId = _u.SheetId.Text ?? "";
        if ((_u.SheetsClient.Text ?? "") != "")
            SaveKey("sheets", _u.SheetsClient.Text!, _u.SheetsSecret.Text ?? "", "free");
        if ((_u.EuKey.Text ?? "") != "")
            SaveKey("commercial-eu", "key", _u.EuKey.Text!, "free");
        p.DeepLEndpoint = _u.EuPaid.IsChecked == true ? "paid" : "free";
        if ((_u.LlmKey.Text ?? "") != "")
            SaveKey("llm", "key", _u.LlmKey.Text!, "free");
        string lm = ComboValue(_u.LlmModel);
        p.LlmModel = lm == Translate.RemoteDefaults.LlmDefaultModel ? "" : lm;
        a.LlmCustomModel = _u.LlmCustom.Text ?? a.LlmCustomModel;

        p.UseDict = _u.DictUse.IsChecked == true;
        p.DictFile = _u.DictFile.Text ?? "myDic.txt";
        p.DictByWord = _u.DictWord.IsChecked == true;
        p.ColorFilter = _u.FRgb.IsChecked == true ? "rgb"
            : _u.FHsv.IsChecked == true ? "hsv"
            : _u.FThr.IsChecked == true ? "threshold" : "none";   // RF-104
        p.Threshold = Num(_u.Threshold, 0, 255, 127);
        p.Erode = _u.Erode.IsChecked == true;
        SaveGroupFields();

        p.FontFamily = _u.FontFam.Text ?? "";
        p.FontSize = Math.Clamp(double.TryParse(_u.FontSize.Text, out var fs) ? fs : 15, 8, 72);
        p.TextColor = (byte[])_u.CText.Clone();
        p.Outline1 = (byte[])_u.CC1.Clone();
        p.Outline2 = (byte[])_u.CC2.Clone();
        p.BgColor = (byte[])_u.CBg.Clone();
        p.TextOrder = _u.Center.IsChecked == true ? "center" : "left";
        p.RemoveSpaces = _u.RmSpaces.IsChecked == true;
        p.TextBackground = _u.UseBg.IsChecked == true;
        p.AreaNumbering = _u.AreaNum.IsChecked == true;
        p.OverlayOutline = _u.Outline.IsChecked == true;

        p.CaptureActiveWindow = _u.ActiveWin.IsChecked == true;
        p.Zoom = Imaging.Preprocess.ClampZoomSteps(                 // RF-114
            double.TryParse(_u.Zoom.Text, out var z) ? z : 2.0);
        for (int i = 0; i < 5; i++)
            if (_u.Speeds[i].IsChecked == true) p.Speed = i + 1;
        string mode = _u.MLayer.IsChecked == true ? "layer"
            : _u.MOverlay.IsChecked == true ? "overlay" : "dark";
        if (mode != p.WindowMode) p.WindowMode = mode;             // RF-318 na UI
        _cfg.App.TranslationAlwaysOnTop = _u.Top.IsChecked == true;
        p.LayerAutoFit = _u.LayerFit.IsChecked == true;
        p.LayerMaxW = Num(_u.LayerMaxW, 0, 10000, 0);
        p.LayerMaxH = Num(_u.LayerMaxH, 0, 10000, 0);
        _cfg.App.CheckUpdate = _u.CheckUpdate.IsChecked == true;
        _cfg.App.BasicTabDefault = _u.BasicDefault.IsChecked == true;

        foreach (var (svc, pair) in _u.LangPairs)
        {
            string src = ComboValue(pair.Src), dst = ComboValue(pair.Dst);
            if (src != "") p.ServiceSource[svc] = src;             // RF-512
            if (dst != "") p.ServiceTarget[svc] = dst;
        }
        p.Tts = _u.Tts.IsChecked == true;
        p.TtsWait = _u.TtsWait.IsChecked == true;

        foreach (var (act, field) in _u.Hotkeys)
            _cfg.Shortcuts.Map[act] = field.Text ?? "";            // RF-513 vazio = nunca

        p.Normalize(out _);   // RF-039: normaliza antes de salvar
    }

    private static void SaveKey(string service, string id, string secret, string plan)
    {
        var keys = Store.ConfigService.LoadCreds(service);
        var ex = keys.FirstOrDefault(k => k.Id == id);
        if (ex is null) keys.Add(new CredentialRecord { Id = id, Secret = secret, Plan = plan });
        else ex.Secret = secret;
        Store.ConfigService.SaveCreds(service, keys);
    }

    private void SaveGroupFields()
    {
        int idx = _u.Groups.SelectedIndex - 2;   // desconta adicionar/remover
        var groups = _cfg.Profile.ColorGroups;
        if (idx < 0 || idx >= groups.Count) return;
        var g = groups[idx];
        g.R = Num(_u.R, 0, 255); g.G = Num(_u.G, 0, 255); g.B = Num(_u.B, 0, 255);
        g.S1 = Num(_u.S1, 0, 100); g.S2 = Num(_u.S2, 0, 100);
        g.V1 = Num(_u.V1, 0, 100); g.V2 = Num(_u.V2, 0, 100);
        g.Normalize();   // RF-043
    }
}
