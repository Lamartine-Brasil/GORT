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
        var (raw, fresh) = TomlFile.Load(path);
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
        LoadProfile(path, isMain: false);
        SaveProfile();
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

    private void LoadAdvanced()
    {
        var (raw, fresh) = TomlFile.Load(Paths.AdvancedFile);
        _rawAdvanced = raw;
        var a = new AdvancedOptions();
        if (!fresh)
        {
            a.TrayMode = TomlFile.GetBool(raw, "tray_mode", a.TrayMode);
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
        }
        a.Normalize();
        Advanced = a;
        if (fresh) SaveAdvanced();   // RF-033
    }

    private void LoadApp()
    {
        var (raw, _) = TomlFile.Load(Paths.AppFile);
        _rawApp = raw;
        var a = new AppOptions
        {
            UiLanguage = TomlFile.GetString(raw, "ui_lang", "pt-BR"),
            CheckUpdate = TomlFile.GetBool(raw, "check_update", true),
            BasicTabDefault = TomlFile.GetBool(raw, "basic_default", false),
            TranslationAlwaysOnTop = TomlFile.GetBool(raw, "always_on_top", true),
        };
        App = a;
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
            ["color_groups"] = groups,
            ["areas"] = areas,
            ["exclusions"] = excls,
        };
        TomlFile.Save(path, _rawProfile, known, Profile.SchemaVersion);
    }

    public void SaveAdvanced()
    {
        TomlFile.Save(Paths.AdvancedFile, _rawAdvanced, new()
        {
            ["tray_mode"] = Advanced.TrayMode,
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
        }, AdvancedOptions.SchemaVersion);
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
