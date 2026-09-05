using System.Collections.Generic;
using System.IO;
using Gort.Config;
using Gort.Core;
using Gort.Persistence;
using Tomlyn.Model;

namespace Gort.Store;

/// <summary>
/// Carrega/aplica/salva perfil + avançadas + app + atalhos + credenciais
/// (RF-020..RF-046, RF-039 ordem: UI→efetiva→salva).
/// </summary>
public sealed class ConfigService
{
    public Profile Profile { get; private set; } = Profile.Defaults();
    public AdvancedOptions Advanced { get; private set; } = new();
    public AppOptions App { get; private set; } = new();
    public ShortcutFile Shortcuts { get; private set; } = ShortcutFile.Defaults();
    public List<string> Notices { get; } = [];

    private TomlTable _rawProfile = new();
    private TomlTable _rawAdvanced = new();
    private TomlTable _rawApp = new();
    private TomlTable _rawShortcuts = new();

    public void LoadAll()
    {
        Paths.EnsureAll();
        LoadProfile(Paths.ProfileFile, isMain: true);
        LoadAdvanced();
        LoadApp();
        LoadShortcuts();
        CollectNoticesPrune();
    }

    private void CollectNoticesPrune()
    {
        // RF-216: só mantém coletânea que existe no disco.
        Advanced.CollectActive.RemoveAll(f => !File.Exists(Path.Combine(Paths.CollectDir, f)));
    }

    public void LoadProfile(string path, bool isMain)
    {
        var (raw, fresh, corrupt) = TomlFile.LoadEx(path);
        if (isMain && corrupt) BackupCorrupt(path);   // defeito vira .bak, não perda total
        _rawProfile = raw;        var p = new Profile();
        if (!fresh)
        {
            p.LoadedSchema = TomlFile.GetSchema(raw, Profile.SchemaVersion);
            // Migrações futuras entram aqui (RF-038). v1 = atual.
            p.WindowMode = TomlFile.GetString(raw, "window_mode", p.WindowMode);
            p.TranslationService = TomlFile.GetString(raw, "translation_service", p.TranslationService);
            p.CustomPresetSubkey = TomlFile.GetString(raw, "custom_preset", p.CustomPresetSubkey);
            p.OcrEngine = TomlFile.GetString(raw, "ocr_engine", p.OcrEngine);
            p.OcrLanguage = TomlFile.GetString(raw, "ocr_lang", p.OcrLanguage);
            p.TargetLanguage = TomlFile.GetString(raw, "target_lang", p.TargetLanguage);
            p.Speed = TomlFile.GetInt(raw, "speed", p.Speed);
            p.Zoom = TomlFile.GetDouble(raw, "zoom", p.Zoom);
            p.Threshold = TomlFile.GetInt(raw, "threshold", p.Threshold);
            p.ColorFilter = TomlFile.GetString(raw, "color_filter", p.ColorFilter);
            p.UseDict = TomlFile.GetBool(raw, "use_dict", p.UseDict);
            p.DictByWord = TomlFile.GetBool(raw, "dict_by_word", p.DictByWord);
            p.DictFile = TomlFile.GetString(raw, "dict_file", p.DictFile);
            p.DbFile = TomlFile.GetString(raw, "db_file", p.DbFile);
            p.DbIgnoreCase = TomlFile.GetBool(raw, "db_ignore_case", p.DbIgnoreCase);
            p.DbPartial = TomlFile.GetBool(raw, "db_partial", p.DbPartial);
            p.ShowOcrText = TomlFile.GetBool(raw, "show_ocr", p.ShowOcrText);
            p.RemoveSpaces = TomlFile.GetBool(raw, "remove_spaces", p.RemoveSpaces);
            p.FontSize = TomlFile.GetDouble(raw, "font_size", p.FontSize);
            p.AutoMinPt = TomlFile.GetDouble(raw, "auto_min_pt", p.AutoMinPt);
            p.AutoMaxPt = TomlFile.GetDouble(raw, "auto_max_pt", p.AutoMaxPt);
            p.FontFamily = TomlFile.GetString(raw, "font_family", p.FontFamily);
            p.CloudMonthlyLimit = TomlFile.GetInt(raw, "cloud_limit", p.CloudMonthlyLimit);
            p.CloudCredFile = TomlFile.GetString(raw, "cloud_cred", p.CloudCredFile);
            p.DeepLEndpoint = TomlFile.GetString(raw, "deepl_endpoint", p.DeepLEndpoint);
            p.WebQuality = TomlFile.GetString(raw, "web_quality", p.WebQuality);
            p.SheetsSheetId = TomlFile.GetString(raw, "sheets_id", p.SheetsSheetId);
            p.LlmModel = TomlFile.GetString(raw, "llm_model", p.LlmModel);
            p.ModernVertical = TomlFile.GetBool(raw, "modern_vertical", p.ModernVertical);
            p.PreprocessOff = TomlFile.GetBool(raw, "preprocess_off", p.PreprocessOff);
            p.LayerAutoFit = TomlFile.GetBool(raw, "layer_autofit", p.LayerAutoFit);
            p.LayerMaxW = TomlFile.GetInt(raw, "layer_max_w", p.LayerMaxW);
            p.LayerMaxH = TomlFile.GetInt(raw, "layer_max_h", p.LayerMaxH);
            p.OverlayOutline = TomlFile.GetBool(raw, "overlay_outline", p.OverlayOutline);
            p.AutoFontSize = TomlFile.GetBool(raw, "overlay_autofont", p.AutoFontSize);
            p.MergeLinesOverlay = TomlFile.GetBool(raw, "overlay_merge", p.MergeLinesOverlay);
            p.KeepDirection = TomlFile.GetBool(raw, "overlay_keepdir", p.KeepDirection);
            p.AutoColorMaster = TomlFile.GetBool(raw, "overlay_autocolor", p.AutoColorMaster);
            p.AutoColorFg = TomlFile.GetBool(raw, "overlay_autofg", p.AutoColorFg);
            p.AutoColorBg = TomlFile.GetBool(raw, "overlay_autobg", p.AutoColorBg);
            p.ClassicDataset = TomlFile.GetString(raw, "classic_dataset", p.ClassicDataset);
            p.ClassicFast = TomlFile.GetBool(raw, "classic_fast", p.ClassicFast);
            p.SaveResultFile = TomlFile.GetBool(raw, "save_result", p.SaveResultFile);
            p.CopyToClipboard = TomlFile.GetBool(raw, "copy_clipboard", p.CopyToClipboard);
            p.CopyFormat = TomlFile.GetString(raw, "copy_format", p.CopyFormat);
            p.Erode = TomlFile.GetBool(raw, "erode", p.Erode);
            p.TextOrder = TomlFile.GetString(raw, "text_order", p.TextOrder);
            p.TextBackground = TomlFile.GetBool(raw, "text_background", p.TextBackground);
            p.AreaNumbering = TomlFile.GetBool(raw, "area_number", p.AreaNumbering);
            p.CaptureActiveWindow = TomlFile.GetBool(raw, "capture_active", p.CaptureActiveWindow);
            p.Tts = TomlFile.GetBool(raw, "tts", p.Tts);
            p.TtsWait = TomlFile.GetBool(raw, "tts_wait", p.TtsWait);
            p.LayerX = TomlFile.GetInt(raw, "layer_x", p.LayerX);
            p.LayerY = TomlFile.GetInt(raw, "layer_y", p.LayerY);
            p.LayerW = TomlFile.GetInt(raw, "layer_w", p.LayerW);
            p.LayerH = TomlFile.GetInt(raw, "layer_h", p.LayerH);
            p.TextColor = GetBytes(raw, "text_color", p.TextColor, 3);
            p.Outline1 = GetBytes(raw, "outline1", p.Outline1, 3);
            p.Outline2 = GetBytes(raw, "outline2", p.Outline2, 3);
            p.BgColor = GetBytes(raw, "bgcolor", p.BgColor, 4);
            p.ServiceSource = GetMap(raw, "service_source");
            p.ServiceTarget = GetMap(raw, "service_target");
            p.BgTransparency = TomlFile.GetBool(raw, "bg_transparency", p.BgTransparency);
            LoadColorGroups(raw, p);
            LoadAreas(raw, p);
        }
        // RF-044: deriva do idioma só quando o arquivo não traz valor explícito
        // (perfil novo/parcial). RF-148 age no evento de troca de idioma, na UI.
        bool explicitDict = !fresh &&
            (raw.ContainsKey("dict_by_word") || raw.ContainsKey("remove_spaces"));
        p.Normalize(out var notices, deriveLangDefaults: !explicitDict);
        Notices.AddRange(notices);
        Profile = p;
        if (isMain && fresh) SaveProfile();   // RF-024: ausente → cria vazio/padrões
    }

    /// <summary>
    /// Atalho abrir-perfil: carrega o arquivo como perfil principal vigente
    /// (normaliza e salva — RF-039).
    /// </summary>
    public void LoadProfileIntoMain(string path)
    {
        // Preserva o raw principal: sem isso as chaves desconhecidas do
        // importado contaminavam o profile.toml e as dele se perdiam.
        var keep = _rawProfile;
        LoadProfile(path, isMain: false);
        _rawProfile = keep;
        SaveProfile();
    }

    /// <summary>Cópia de segurança do arquivo defeituoso antes de regravar padrões.</summary>
    private static void BackupCorrupt(string path)
    {
        try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
    }

    /// <summary>Bytes de cor (array TOML de 0–255); fora disso volta o padrão.</summary>
    private static byte[] GetBytes(TomlTable t, string key, byte[] dflt, int n)
    {
        if (t.TryGetValue(key, out var w) && w is TomlArray arr && arr.Count == n)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++)
            {
                long x = arr[i] is long l ? l : arr[i] is int ii ? ii : -1;
                if (x < 0 || x > 255) return dflt;
                b[i] = (byte)x;
            }
            return b;
        }
        return dflt;
    }

    /// <summary>Mapa string→string (pares por serviço).</summary>
    private static Dictionary<string, string> GetMap(TomlTable t, string key)
    {
        var d = new Dictionary<string, string>();
        if (t.TryGetValue(key, out var v) && v is TomlTable m)
            foreach (var kv in m)
                if (kv.Value is string s) d[kv.Key] = s;
        return d;
    }

    private static void LoadColorGroups(TomlTable raw, Profile p)
    {
        if (raw.TryGetValue("color_groups", out var v) && v is TomlTableArray arr)
        {
            var list = new List<ColorGroup>();
            foreach (var t in arr)
            {
                var g = new ColorGroup
                {
                    R = TomlFile.GetInt(t, "r", 0),
                    G = TomlFile.GetInt(t, "g", 0),
                    B = TomlFile.GetInt(t, "b", 0),
                    S1 = TomlFile.GetInt(t, "s1", 0),
                    S2 = TomlFile.GetInt(t, "s2", 0),
                    V1 = TomlFile.GetInt(t, "v1", 0),
                    V2 = TomlFile.GetInt(t, "v2", 0),
                };
                g.Normalize();
                list.Add(g);
            }
            if (list.Count > 0) p.ColorGroups = list;
        }
    }

    private static void LoadAreas(TomlTable raw, Profile p)
    {
        if (raw.TryGetValue("areas", out var v) && v is TomlTableArray arr)
        {
            p.Areas.Clear();
            foreach (var t in arr)
            {
                var area = new OcrArea
                {
                    X = TomlFile.GetInt(t, "x", 0),
                    Y = TomlFile.GetInt(t, "y", 0),
                    W = TomlFile.GetInt(t, "w", 50),
                    H = TomlFile.GetInt(t, "h", 50),
                };
                if (t.TryGetValue("groups", out var gv) && gv is TomlArray gl)
                    foreach (var gi in gl)
                        if (gi is long l) area.ColorGroups.Add((int)l);
                p.Areas.Add(area);
            }
        }
        if (raw.TryGetValue("exclusions", out var e) && e is TomlTableArray earr)
        {
            p.Exclusions.Clear();
            foreach (var t in earr)
                p.Exclusions.Add(new ExclusionArea
                {
                    X = TomlFile.GetInt(t, "x", 0),
                    Y = TomlFile.GetInt(t, "y", 0),
                    W = TomlFile.GetInt(t, "w", 50),
                    H = TomlFile.GetInt(t, "h", 50),
                });
        }
    }

    private void LoadAdvanced() => LoadAdvancedFrom(Paths.AdvancedFile);

    public void LoadAdvancedFrom(string path)
    {
        var (raw, fresh, corrupt) = TomlFile.LoadEx(path);
        // Backup só do canônico: temp/custom corrompido não encosta nele.
        if (corrupt && path == Paths.AdvancedFile) BackupCorrupt(path);
        _rawAdvanced = raw;
        var a = new AdvancedOptions();
        if (!fresh)
        {
            a.LoadedSchema = TomlFile.GetSchema(raw, AdvancedOptions.SchemaVersion);
            a.TrayMode = TomlFile.GetBool(raw, "tray_mode", a.TrayMode);
            a.RightToLeft = TomlFile.GetBool(raw, "right_to_left", a.RightToLeft);
            a.RemoteAlwaysOnTop = TomlFile.GetBool(raw, "remote_top", a.RemoteAlwaysOnTop);
            a.FollowCompat = TomlFile.GetBool(raw, "follow_compat", a.FollowCompat);
            a.FollowOnly = TomlFile.GetBool(raw, "follow_only", a.FollowOnly);
            a.AttachedYellowBorder = TomlFile.GetBool(raw, "yellow_border", a.AttachedYellowBorder);
            a.SelectBg = TomlFile.GetString(raw, "select_bg", a.SelectBg);
            a.SelectAccent = TomlFile.GetString(raw, "select_accent", a.SelectAccent);
            LoadOpenProfiles(raw, a);
            a.ToggleForcedTransparency = TomlFile.GetString(raw, "forced_transparency_keys", a.ToggleForcedTransparency);
            a.ServiceSwitch = GetMap(raw, "service_switch");
            a.OverlayBgAlpha = TomlFile.GetBool(raw, "overlay_bg_alpha", a.OverlayBgAlpha);
            a.DarkFont = TomlFile.GetString(raw, "dark_font", a.DarkFont);
            a.LayerBottom = TomlFile.GetBool(raw, "layer_bottom", a.LayerBottom);
            a.LayerRight = TomlFile.GetBool(raw, "layer_right", a.LayerRight);
            a.DisplayMemory = TomlFile.GetBool(raw, "display_memory", a.DisplayMemory);
            a.DisplayMemoryN = TomlFile.GetInt(raw, "display_memory_n", a.DisplayMemoryN);
            a.DisplayMemorySec = TomlFile.GetInt(raw, "display_memory_s", a.DisplayMemorySec);
            a.DictExtraPasses = TomlFile.GetInt(raw, "dict_passes", a.DictExtraPasses);
            a.SnapshotStaySec = TomlFile.GetInt(raw, "snapshot_stay", a.SnapshotStaySec);
            a.Bridge = TomlFile.GetBool(raw, "bridge", a.Bridge);
            a.FallbackTranslator = TomlFile.GetBool(raw, "fallback", a.FallbackTranslator);
            a.IgnoreEmpty = TomlFile.GetBool(raw, "ignore_empty", a.IgnoreEmpty);
            a.TopOnlyDuring = TomlFile.GetBool(raw, "top_only_during", a.TopOnlyDuring);
            a.HideAlsoTranslates = TomlFile.GetBool(raw, "hide_also_translates", a.HideAlsoTranslates);
            a.ForcedTransparency = TomlFile.GetBool(raw, "forced_transparency", a.ForcedTransparency);
            LoadCollect(raw, a);
            LoadCustomPresets(raw, a);
            a.CustomSameCodes = TomlFile.GetBool(raw, "custom_same_codes", a.CustomSameCodes);
            a.CustomSource = TomlFile.GetString(raw, "custom_source", a.CustomSource);
            a.CustomTarget = TomlFile.GetString(raw, "custom_target", a.CustomTarget);
            a.CustomUrl = TomlFile.GetString(raw, "custom_url", a.CustomUrl);
            a.LlmInstruction = TomlFile.GetString(raw, "llm_instruction", a.LlmInstruction);
            a.LlmCustomModel = TomlFile.GetString(raw, "llm_custom_model", a.LlmCustomModel);
            a.LlmNoDefault = TomlFile.GetBool(raw, "llm_no_default", a.LlmNoDefault);
            a.LlmPreset = TomlFile.GetString(raw, "llm_preset", a.LlmPreset);
            a.LlmTemp = TomlFile.GetInt(raw, "llm_temp", a.LlmTemp);
            a.LlmReason = TomlFile.GetInt(raw, "llm_reason", a.LlmReason);
            a.LlmMaxOut = TomlFile.GetInt(raw, "llm_maxout", a.LlmMaxOut);
            a.ClipboardTranslate = TomlFile.GetBool(raw, "clip_translate", a.ClipboardTranslate);
            a.ClipboardShowOriginal = TomlFile.GetBool(raw, "clip_original", a.ClipboardShowOriginal);
            a.ClipboardShowWorking = TomlFile.GetBool(raw, "clip_working", a.ClipboardShowWorking);
            a.ClipboardCopyFormat = TomlFile.GetString(raw, "clip_format", a.ClipboardCopyFormat);
            a.CloudPriority = TomlFile.GetBool(raw, "cloud_priority", a.CloudPriority);
        }
        a.Normalize();
        Advanced = a;
        if (fresh) SaveAdvancedTo(path);   // RF-033
    }

    private static void LoadOpenProfiles(TomlTable raw, AdvancedOptions a)
    {
        if (raw.TryGetValue("open_profiles", out var v) && v is TomlTableArray arr)
        {
            a.OpenProfile.Clear();
            foreach (var t in arr)
            {
                if (t is TomlTable m)
                    a.OpenProfile.Add(new OpenProfileShortcut
                    {
                        Keys = TomlFile.GetString(m, "keys", ""),
                        File = TomlFile.GetString(m, "file", ""),
                    });
                if (a.OpenProfile.Count >= 4) break;
            }
        }
    }

    private static void LoadCollect(TomlTable raw, AdvancedOptions a)
    {
        if (raw.TryGetValue("collect_active", out var v) && v is TomlArray arr)
        {
            a.CollectActive.Clear();
            foreach (var e in arr)
                if (e is string s && s.Length > 0) a.CollectActive.Add(s);
        }
        a.CollectAsDb = TomlFile.GetBool(raw, "collect_as_db", a.CollectAsDb);
        a.CollectIgnoreCase = TomlFile.GetBool(raw, "collect_ignore_case", a.CollectIgnoreCase);
    }

    private static void LoadCustomPresets(TomlTable raw, AdvancedOptions a)
    {
        if (raw.TryGetValue("custom_presets", out var v) && v is TomlTableArray arr)
        {
            a.CustomPresets.Clear();
            foreach (var t in arr)
            {
                if (t is not TomlTable m) continue;
                var p = new CustomPreset
                {
                    Name = TomlFile.GetString(m, "name", ""),
                    Url = TomlFile.GetString(m, "url", ""),
                    ReqTemplate = TomlFile.GetString(m, "req", ""),
                    ResTemplate = TomlFile.GetString(m, "res", ""),
                };
                if (p.Name.Length == 0) continue;
                if (m.TryGetValue("headers", out var h) && h is TomlArray ha)
                    foreach (var e in ha)
                        if (e is string s) p.Headers.Add(s);
                a.CustomPresets.Add(p);
            }
        }
    }

    private void LoadApp()
    {
        var (raw, fresh) = TomlFile.Load(Paths.AppFile);
        _rawApp = raw;
        var a = new AppOptions
        {
            UiLanguage = TomlFile.GetString(raw, "ui_lang", "pt-BR"),
            CheckUpdate = TomlFile.GetBool(raw, "check_update", true),
            BasicTabDefault = TomlFile.GetBool(raw, "basic_default", false),
            TranslationAlwaysOnTop = TomlFile.GetBool(raw, "always_on_top", true),
        };
        if (a.UiLanguage != "" && a.UiLanguage != "pt-BR")
        {
            Notices.Add($"ui_lang desconhecido '{a.UiLanguage}'; padrão pt-BR.");
            a.UiLanguage = "pt-BR";
        }
        App = a;
        if (fresh) SaveApp();
    }

    private void LoadShortcuts()
    {
        var (raw, fresh) = TomlFile.Load(Paths.ShortcutsFile);
        _rawShortcuts = raw;
        var s = ShortcutFile.Defaults();
        if (!fresh && raw.TryGetValue("keys", out var v) && v is TomlTable t)
            foreach (var kv in t)
                if (kv.Value is string str) s.Map[kv.Key] = str;
        Shortcuts = s;
    }

    public void SaveShortcuts()
    {
        var keys = new TomlTable();
        foreach (var kv in Shortcuts.Map) keys[kv.Key] = kv.Value;
        TomlFile.Save(Paths.ShortcutsFile, _rawShortcuts,
            new Dictionary<string, object?> { ["keys"] = keys },
            ShortcutFile.SchemaVersion);
    }

    public void SaveProfile() => SaveProfileTo(Paths.ProfileFile);

    public void SaveProfileTo(string path)
    {
        var groups = new TomlTableArray();
        foreach (var g in Profile.ColorGroups)
        {
            var t = new TomlTable
            {
                ["r"] = (long)g.R, ["g"] = (long)g.G, ["b"] = (long)g.B,
                ["s1"] = (long)g.S1, ["s2"] = (long)g.S2,
                ["v1"] = (long)g.V1, ["v2"] = (long)g.V2,
            };
            groups.Add(t);
        }
        var areas = new TomlTableArray();
        foreach (var a in Profile.Areas)
        {
            var at = new TomlTable
            {
                ["x"] = (long)a.X, ["y"] = (long)a.Y,
                ["w"] = (long)a.W, ["h"] = (long)a.H,
            };
            var gl = new TomlArray();
            foreach (var g in a.ColorGroups) gl.Add((long)g);
            at["groups"] = gl;
            areas.Add(at);
        }
        var excls = new TomlTableArray();
        foreach (var e in Profile.Exclusions)
            excls.Add(new TomlTable
            {
                ["x"] = (long)e.X, ["y"] = (long)e.Y,
                ["w"] = (long)e.W, ["h"] = (long)e.H,
            });
        var known = new Dictionary<string, object?>
        {
            ["window_mode"] = Profile.WindowMode,
            ["translation_service"] = Profile.TranslationService,
            ["custom_preset"] = Profile.CustomPresetSubkey,
            ["ocr_engine"] = Profile.OcrEngine,
            ["ocr_lang"] = Profile.OcrLanguage,
            ["target_lang"] = Profile.TargetLanguage,
            ["speed"] = (long)Profile.Speed,
            ["speed_ms"] = (long)Core.Params.SpeedToInterval(Profile.Speed),
            ["zoom"] = Profile.Zoom,
            ["threshold"] = (long)Profile.Threshold,
            ["color_filter"] = Profile.ColorFilter,
            ["use_dict"] = Profile.UseDict,
            ["dict_by_word"] = Profile.DictByWord,
            ["dict_file"] = Profile.DictFile,
            ["db_file"] = Profile.DbFile,
            ["db_ignore_case"] = Profile.DbIgnoreCase,
            ["db_partial"] = Profile.DbPartial,
            ["show_ocr"] = Profile.ShowOcrText,
            ["remove_spaces"] = Profile.RemoveSpaces,
            ["font_size"] = Profile.FontSize,
            ["auto_min_pt"] = Profile.AutoMinPt,
            ["auto_max_pt"] = Profile.AutoMaxPt,
            ["font_family"] = Profile.FontFamily,
            ["cloud_limit"] = (long)Profile.CloudMonthlyLimit,
            ["cloud_cred"] = Profile.CloudCredFile,
            ["deepl_endpoint"] = Profile.DeepLEndpoint,
            ["web_quality"] = Profile.WebQuality,
            ["sheets_id"] = Profile.SheetsSheetId,
            ["llm_model"] = Profile.LlmModel,
            ["modern_vertical"] = Profile.ModernVertical,
            ["preprocess_off"] = Profile.PreprocessOff,
            ["layer_autofit"] = Profile.LayerAutoFit,
            ["layer_max_w"] = (long)Profile.LayerMaxW,
            ["layer_max_h"] = (long)Profile.LayerMaxH,
            ["overlay_outline"] = Profile.OverlayOutline,
            ["overlay_autofont"] = Profile.AutoFontSize,
            ["overlay_merge"] = Profile.MergeLinesOverlay,
            ["overlay_keepdir"] = Profile.KeepDirection,
            ["overlay_autocolor"] = Profile.AutoColorMaster,
            ["overlay_autofg"] = Profile.AutoColorFg,
            ["overlay_autobg"] = Profile.AutoColorBg,
            ["classic_dataset"] = Profile.ClassicDataset,
            ["classic_fast"] = Profile.ClassicFast,
            ["save_result"] = Profile.SaveResultFile,
            ["copy_clipboard"] = Profile.CopyToClipboard,
            ["copy_format"] = Profile.CopyFormat,
            ["erode"] = Profile.Erode,
            ["text_order"] = Profile.TextOrder,
            ["text_background"] = Profile.TextBackground,
            ["area_number"] = Profile.AreaNumbering,
            ["capture_active"] = Profile.CaptureActiveWindow,
            ["tts"] = Profile.Tts,
            ["tts_wait"] = Profile.TtsWait,
            ["layer_x"] = (long)Profile.LayerX,
            ["layer_y"] = (long)Profile.LayerY,
            ["layer_w"] = (long)Profile.LayerW,
            ["layer_h"] = (long)Profile.LayerH,
            ["text_color"] = ByteArray(Profile.TextColor),
            ["outline1"] = ByteArray(Profile.Outline1),
            ["outline2"] = ByteArray(Profile.Outline2),
            ["bgcolor"] = ByteArray(Profile.BgColor),
            ["service_source"] = StringMap(Profile.ServiceSource),
            ["service_target"] = StringMap(Profile.ServiceTarget),
            ["bg_transparency"] = Profile.BgTransparency,
            ["color_groups"] = groups,
            ["areas"] = areas,
            ["exclusions"] = excls,
        };
        // Preserva schema futuro (nunca rebaixa ao salvar).
        int schema = Profile.LoadedSchema > Profile.SchemaVersion
            ? Profile.LoadedSchema : Profile.SchemaVersion;
        TomlFile.Save(path, _rawProfile, known, schema);
    }

    private static TomlArray ByteArray(byte[] b)
    {
        var arr = new TomlArray();
        foreach (byte x in b) arr.Add((long)x);
        return arr;
    }

    private static TomlTable StringMap(Dictionary<string, string> d)
    {
        var t = new TomlTable();
        foreach (var kv in d) t[kv.Key] = kv.Value;
        return t;
    }

    public void SaveAdvanced() => SaveAdvancedTo(Paths.AdvancedFile);

    public void SaveAdvancedTo(string path)
    {
        var open = new TomlTableArray();
        foreach (var o in Advanced.OpenProfile)
            open.Add(new TomlTable { ["keys"] = o.Keys, ["file"] = o.File });
        var collect = new TomlArray();
        foreach (var f in Advanced.CollectActive) collect.Add(f);
        var presets = new TomlTableArray();
        foreach (var p in Advanced.CustomPresets)
        {
            if (p.FromFile) continue;   // arquivo é fonte (só-leitura)
            var headers = new TomlArray();
            foreach (var h in p.Headers) headers.Add(h);
            presets.Add(new TomlTable
            {
                ["name"] = p.Name, ["url"] = p.Url, ["headers"] = headers,
                ["req"] = p.ReqTemplate, ["res"] = p.ResTemplate,
            });
        }
        var switches = new TomlTable();
        foreach (var kv in Advanced.ServiceSwitch) switches[kv.Key] = kv.Value;
        TomlFile.Save(path, _rawAdvanced, new()
        {
            ["tray_mode"] = Advanced.TrayMode,
            ["right_to_left"] = Advanced.RightToLeft,
            ["remote_top"] = Advanced.RemoteAlwaysOnTop,
            ["follow_compat"] = Advanced.FollowCompat,
            ["follow_only"] = Advanced.FollowOnly,
            ["yellow_border"] = Advanced.AttachedYellowBorder,
            ["select_bg"] = Advanced.SelectBg,
            ["select_accent"] = Advanced.SelectAccent,
            ["open_profiles"] = open,
            ["forced_transparency_keys"] = Advanced.ToggleForcedTransparency,
            ["service_switch"] = switches,
            ["overlay_bg_alpha"] = Advanced.OverlayBgAlpha,
            ["dark_font"] = Advanced.DarkFont,
            ["layer_bottom"] = Advanced.LayerBottom,
            ["layer_right"] = Advanced.LayerRight,
            ["display_memory"] = Advanced.DisplayMemory,
            ["display_memory_n"] = (long)Advanced.DisplayMemoryN,
            ["display_memory_s"] = (long)Advanced.DisplayMemorySec,
            ["dict_passes"] = (long)Advanced.DictExtraPasses,
            ["snapshot_stay"] = (long)Advanced.SnapshotStaySec,
            ["bridge"] = Advanced.Bridge,
            ["fallback"] = Advanced.FallbackTranslator,
            ["ignore_empty"] = Advanced.IgnoreEmpty,
            ["top_only_during"] = Advanced.TopOnlyDuring,
            ["hide_also_translates"] = Advanced.HideAlsoTranslates,
            ["forced_transparency"] = Advanced.ForcedTransparency,
            ["collect_active"] = collect,
            ["collect_as_db"] = Advanced.CollectAsDb,
            ["collect_ignore_case"] = Advanced.CollectIgnoreCase,
            ["custom_presets"] = presets,
            ["custom_same_codes"] = Advanced.CustomSameCodes,
            ["custom_source"] = Advanced.CustomSource,
            ["custom_target"] = Advanced.CustomTarget,
            ["custom_url"] = Advanced.CustomUrl,
            ["llm_instruction"] = Advanced.LlmInstruction,
            ["llm_custom_model"] = Advanced.LlmCustomModel,
            ["llm_no_default"] = Advanced.LlmNoDefault,
            ["llm_preset"] = Advanced.LlmPreset,
            ["llm_temp"] = (long)Advanced.LlmTemp,
            ["llm_reason"] = (long)Advanced.LlmReason,
            ["llm_maxout"] = (long)Advanced.LlmMaxOut,
            ["clip_translate"] = Advanced.ClipboardTranslate,
            ["clip_original"] = Advanced.ClipboardShowOriginal,
            ["clip_working"] = Advanced.ClipboardShowWorking,
            ["clip_format"] = Advanced.ClipboardCopyFormat,
            ["cloud_priority"] = Advanced.CloudPriority,
        }, Advanced.LoadedSchema > AdvancedOptions.SchemaVersion
            ? Advanced.LoadedSchema : AdvancedOptions.SchemaVersion);
    }

    public void SaveApp()
    {
        TomlFile.Save(Paths.AppFile, _rawApp, new()
        {
            ["ui_lang"] = App.UiLanguage,
            ["check_update"] = App.CheckUpdate,
            ["basic_default"] = App.BasicTabDefault,
            ["always_on_top"] = App.TranslationAlwaysOnTop,
        }, AppOptions.SchemaVersion);
    }

    public void RestoreDefaults()
    {
        Profile = Profile.Defaults();
        Profile.Normalize(out _);
        SaveProfile();
    }

    /// <summary>RF-046: texto da configuração atual para compartilhar na comunidade.</summary>
    public string BuildExportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# GORT — configuração exportada (cole na página da comunidade)");
        sb.AppendLine($"# window_mode = {Profile.WindowMode}");
        sb.AppendLine($"# ocr = {Profile.OcrEngine}/{Profile.OcrLanguage} x{Profile.Zoom}");
        sb.AppendLine($"# translator = {Profile.TranslationService} ({Profile.OcrLanguage}->{Profile.TargetLanguage})");
        sb.AppendLine($"# speed_ms = {Core.Params.SpeedToInterval(Profile.Speed)}");
        sb.AppendLine($"# filter = {Profile.ColorFilter}, threshold = {Profile.Threshold}");
        foreach (var a in Profile.Areas)
            sb.AppendLine($"# area = {a.X},{a.Y} {a.W}x{a.H}");
        sb.Append(File.Exists(Paths.ProfileFile) ? File.ReadAllText(Paths.ProfileFile) : "");
        return sb.ToString();
    }

    /// <summary>Credenciais em texto puro, um arquivo por serviço (RF-035).</summary>
    public static List<CredentialRecord> LoadCreds(string serviceId)
    {
        var (raw, fresh) = TomlFile.Load(Paths.CredFile(serviceId));
        var list = new List<CredentialRecord>();
        if (!fresh && raw.TryGetValue("keys", out var v) && v is TomlTableArray arr)
            foreach (var t in arr)
                list.Add(new CredentialRecord
                {
                    Id = TomlFile.GetString(t, "id", ""),
                    Secret = TomlFile.GetString(t, "secret", ""),
                    Plan = TomlFile.GetString(t, "plan", "free"),
                });
        return list;
    }

    public static void SaveCreds(string serviceId, List<CredentialRecord> keys)
    {
        var (raw, _) = TomlFile.Load(Paths.CredFile(serviceId));
        var arr = new TomlTableArray();
        foreach (var k in keys)
            arr.Add(new TomlTable
            {
                ["id"] = k.Id, ["secret"] = k.Secret, ["plan"] = k.Plan,
            });
        TomlFile.Save(Paths.CredFile(serviceId), raw,
            new() { ["keys"] = arr }, 1);
    }
}
