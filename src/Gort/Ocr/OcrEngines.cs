using System;
using System.Collections.Generic;
using Gort.Core;
using Gort.Ocr.Classic;
using Gort.Ocr.Cloud;
using Gort.Ocr.Modern;
using Gort.Ocr.Os;
using Gort.Ocr.Venv;

namespace Gort.Ocr;

/// <summary>
/// Registro de motores (RF-120, RF-575): lista exatamente os disponíveis.
/// Clássico, nuvem e venv com instalação sob demanda; SO indisponível
/// sem projeção WinRT.
public static class OcrEngines
{
    private static readonly Dictionary<string, IOcrEngine> All = new();
    private static bool _ready;

    public static IOcrEngine Modern => All["modern"];

    public static void Initialize(Func<bool> verticalOption,
        Func<string> classicDataset, Func<bool> classicFast,
        Func<string> cloudCred, Func<int> cloudLimit)
    {
        if (_ready) return;
        _ready = true;
        string dataDir = System.IO.Path.Combine(Paths.BaseDir, "ocr", "modern");
        string libDir = System.IO.Path.Combine(AppContext.BaseDirectory, "lib");
        All["modern"] = new RapidOcrEngine(dataDir, libDir, verticalOption);
        All["classic"] = new ClassicEngine(classicDataset, classicFast);
        All["os"] = new OsEngine();
        All["venv"] = new VenvEngine();
        All["cloud"] = new CloudEngine(cloudCred, cloudLimit);
    }

    public static void Initialize(Func<bool> verticalOption) =>
        Initialize(verticalOption, () => "eng", () => false, () => "", () => 950);

    public static IReadOnlyList<(string Id, string Display, bool Available, string? Reason)> List()
    {
        var list = new List<(string, string, bool, string?)>();
        if (!_ready) return list;
        list.Add((All["modern"].Id, "Motor de reconhecimento moderno embarcado",
            All["modern"].IsAvailable, All["modern"].UnavailableReason));
        list.Add((All["classic"].Id, "Motor local clássico",
            All["classic"].IsAvailable, All["classic"].UnavailableReason));
        if (All["os"].IsAvailable)
            list.Add((All["os"].Id, "Motor do sistema operacional",
                true, null));
        list.Add((All["venv"].Id, "Motor baseado em ambiente interpretado",
            All["venv"].IsAvailable, All["venv"].UnavailableReason));
        list.Add((All["cloud"].Id, "Motor de nuvem (somente modo pontual)",
            All["cloud"].IsAvailable, All["cloud"].UnavailableReason));
        return list;
    }

    public static IOcrEngine? Get(string id) =>
        _ready && All.TryGetValue(id, out var e) ? e : null;

    /// <summary>RF-016: libera sessões nativas e processos.</summary>
    public static void ShutdownAll()
    {
        foreach (var e in All.Values)
            if (e is IDisposable d)
                try { d.Dispose(); } catch { }
    }
}
