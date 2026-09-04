using System;
using System.Collections.Generic;
using System.IO;
using Gort.Config;
using Gort.Core;

namespace Gort.Translate;

/// <summary>
/// Registro de serviços (Etapa 15 completa): base + presets de API
/// personalizada como entradas próprias (RF-306). Serviço salvo inexistente
/// cai para o banco local (RF-307).
/// </summary>
public static class Services
{
    private static readonly Dictionary<string, ITranslationService> Base = new();
    private static Func<AdvancedOptions>? _adv;

    static Services()
    {
        var web = new WebFreeTranslator();
        Base[web.Id] = web;
        var db = new DbTranslator();
        Base[db.Id] = db;
    }

    public static void Configure(Func<AdvancedOptions> adv) => _adv = adv;

    public static void Configure(Func<AdvancedOptions> adv, Func<Config.Profile> prof)
    {
        _adv = adv;
        _prof = prof;
    }

    private static Func<Config.Profile>? _prof;
    private static readonly Dictionary<string, ITranslationService> Cache = new();
    private static readonly object CacheGate = new();

    private static AdvancedOptions Adv() =>
        _adv?.Invoke() ?? new AdvancedOptions();

    private static Config.Profile Prof() =>
        _prof?.Invoke() ?? new Config.Profile();

    private static string CredSecret(string service)
    {
        try
        {
            foreach (var k in Store.ConfigService.LoadCreds(service))
                if (!string.IsNullOrEmpty(k.Secret)) return k.Secret;
        }
        catch { }
        return "";
    }

    public static ITranslationService? Get(string id)
    {
        if (Base.TryGetValue(id, out var s))
        {
            // Google: qualidade manual vem do perfil vigente (padrão auto).
            if (s is WebFreeTranslator web) web.QualityProvider = () => Prof().WebQuality;
            return s;
        }
        lock (CacheGate)
        {
            if (Cache.TryGetValue(id, out var c)) return c;
            var svc = Create(id);
            if (svc is not null) Cache[id] = svc;
            return svc;
        }
    }

    /// <summary>Descarta instâncias (presets mudaram — RF-302/303).</summary>
    public static void InvalidateCustom()
    {
        lock (CacheGate)
        {
            var drop = new List<string>();
            foreach (var k in Cache.Keys)
                if (k == "custom" || k.StartsWith("custom:")) drop.Add(k);
            foreach (var k in drop)
            {
                if (Cache[k] is IDisposable d)
                    try { d.Dispose(); } catch { }
                Cache.Remove(k);
            }
        }
    }

    /// <summary>Encerra processos auxiliares e navegadores (RF-016).</summary>
    public static void ShutdownAll()
    {
        lock (CacheGate)
        {
            foreach (var svc in Cache.Values)
                if (svc is IDisposable d)
                    try { d.Dispose(); } catch { }
            Cache.Clear();
        }
    }

    private static ITranslationService? Create(string id)
    {
        var adv = Adv();
        if (id == "commercial-kr") return new KoreanTranslator();
        if (id == "web-nokey") return new NoKeyTranslator();
        if (id == "sheets") return new SheetsTranslator(
            () => Prof().SheetsSheetId, () => Adv().Bridge);
        if (id == "embedded-browser") return new BrowserTranslator(
            () => Get("web-free"), () => Adv().FallbackTranslator);
        if (id == "commercial-eu") return new DeepLTranslator(
            () => CredSecret("commercial-eu"),
            () => Prof().DeepLEndpoint);
        if (id == "llm") return new LlmTranslator(
            () => CredSecret("llm"),
            () => Prof().LlmModel,
            () => Adv().LlmCustomModel,
            () => Adv().LlmPreset, () => Adv().LlmTemp, () => Adv().LlmReason,
            () => Adv().LlmMaxOut, () => Adv().LlmInstruction,
            () => Adv().LlmNoDefault, () => Get("web-free"));
        if (id == "local-worker")
        {
            var w = new LocalWorker();
            return LocalWorker.FindLibrary() is null ? null : w;   // RF-574
        }
        if (id == "custom") return new CustomApiService(null,
            () => Adv().CustomUrl);
        foreach (var p in AllPresets())
            if ("custom:" + p.Name == id || p.Name == id)
                return new CustomApiService(p, null);
        return null;
    }

    /// <summary>Presets: arquivos vencem a lista editável (RF-303).</summary>
    public static List<CustomPreset> AllPresets()
    {
        var adv = Adv();
        var files = CustomApiService.LoadPresetFiles(
            Path.Combine(Paths.BaseDir, "custom-presets"));
        var names = new HashSet<string>();
        foreach (var f in files) names.Add(f.Name);
        var list = new List<CustomPreset>(files);
        foreach (var p in adv.CustomPresets)
            if (!names.Contains(p.Name)) list.Add(p);
        return list;
    }

    public static IReadOnlyList<(string Id, string Display)> List()
    {
        var list = new List<(string, string)>
        {
            ("web-free", "Google Tradutor (web gratuito)"),
            ("db", "Banco de dados local"),
            ("web-nokey", "Tradutor web sem chave"),
            ("commercial-kr", "Tradutor comercial por chave (KR)"),
            ("sheets", "Tradutor por planilha em nuvem"),
            ("embedded-browser", "Tradutor por navegador embutido"),
            ("commercial-eu", "Tradutor comercial por chave (EU)"),
            ("llm", "Tradutor por modelo de linguagem"),
        };
        if (LocalWorker.FindLibrary() is not null)
            list.Add(("local-worker", "Tradutor local por processo auxiliar"));
        list.Add(("custom", "API personalizada"));
        foreach (var p in AllPresets())
            list.Add(("custom:" + p.Name, "Custom – " + p.Name));   // RF-306
        return list;
    }

    /// <summary>RF-307: serviço salvo inexistente → banco de dados local.</summary>
    public static string ResolveOrFallback(string id)
    {
        if (Base.ContainsKey(id)) return id;
        foreach (var p in AllPresets())
            if ("custom:" + p.Name == id || p.Name == id || id == "custom") return id;
        foreach (var (known, _) in List())
            if (known == id) return id;
        return "db";
    }

    /// <summary>Recarrega o banco local a partir do perfil (no aplicar).</summary>
    public static void ReloadDb(Config.Profile p) =>
        ((DbTranslator)Base["db"]).Reload(p.DbFile, p.DbIgnoreCase, p.DbPartial);
}
