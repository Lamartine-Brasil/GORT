using System;
using System.Collections.Generic;
using System.Diagnostics;
using Gort.Config;
using Gort.Core;
using Gort.Platform;
using Gort.Store;

namespace Gort.Regions;

/// <summary>
/// Gerenciador de regiões (cap. 11): áreas incrementais + decrementais
/// persistidas (RF-066/067), área rápida (RF-069), instantânea (RF-070..072),
/// área que segue o mouse (dados; UI na Etapa 16), edição temporária com
/// aplicar/reverter (RF-061/062), reindexação (RF-064), notificação limitada
/// a 1 a cada P-13 (RF-059) e só fora de carga/aplicação (RF-060).
/// </summary>
public sealed class RegionManager
{
    private readonly ConfigService _cfg;

    /// <summary>Comprometido (espelho do perfil). Edição ocorre em Working.</summary>
    public List<AreaDef> Areas { get; } = new();
    public List<ScreenRect> Exclusions { get; } = new();

    /// <summary>Cópia de trabalho enquanto o gerenciamento está aberto.</summary>
    public List<AreaDef> Working { get; private set; } = new();
    public List<ScreenRect> WorkingExcl { get; private set; } = new();
    public bool Managing { get; private set; }

    public AreaDef? QuickArea { get; private set; }               // RF-069
    public ScreenRect? SnapshotArea { get; private set; }         // RF-070
    public ScreenRect? LastSnapshot { get; private set; }
    public ScreenRect? FollowArea { get; set; }                   // Etapa 16
    public bool FollowActive { get; set; }
    public bool FollowOnly { get; set; } = true;                  // RF-459 padrão

    public bool Initialized { get; set; } = true;
    public bool Applying { get; set; }

    public event Action? Changed;
    private readonly Stopwatch _throttle = Stopwatch.StartNew();
    private bool _notifiedOnce;
    private static readonly TimeSpan Throttle =
        TimeSpan.FromSeconds(Params.P13_DragRecalcSec);           // 🔒 0,3 s

    public RegionManager(ConfigService cfg) => _cfg = cfg;

    // ---- carga/persistência (RF-040, RF-066) ----

    public void LoadFromProfile()
    {
        Areas.Clear();
        foreach (var a in _cfg.Profile.Areas)
        {
            if (a.W <= 0 || a.H <= 0) continue;   // perfil corrompido: pula área vazia
            var def = new AreaDef
            {
                Rect = new ScreenRect(a.X, a.Y, a.W, a.H),
            };
            foreach (var g in a.ColorGroups)
                if (g >= 0 && g < _cfg.Profile.ColorGroups.Count) def.Groups.Add(g);
            if (def.Groups.Count == 0)
                for (int i = 0; i < _cfg.Profile.ColorGroups.Count; i++) def.Groups.Add(i);
            Areas.Add(def);
        }
        Exclusions.Clear();
        foreach (var e in _cfg.Profile.Exclusions)
        {
            if (e.W <= 0 || e.H <= 0) continue;
            Exclusions.Add(new ScreenRect(e.X, e.Y, e.W, e.H));
        }
    }

    public void CommitToProfile()
    {
        _cfg.Profile.Areas.Clear();
        foreach (var a in Areas)
            _cfg.Profile.Areas.Add(new OcrArea
            {
                X = a.Rect.X, Y = a.Rect.Y, W = a.Rect.W, H = a.Rect.H,
                ColorGroups = new List<int>(a.Groups),
            });
        _cfg.Profile.Exclusions.Clear();
        foreach (var e in Exclusions)
            _cfg.Profile.Exclusions.Add(new ExclusionArea
            { X = e.X, Y = e.Y, W = e.W, H = e.H });
        _cfg.SaveProfile();
    }

    // ---- edição temporária (RF-061/062) ----

    public void BeginManage()
    {
        Working = Clone(Areas);
        WorkingExcl = new List<ScreenRect>(Exclusions);
        Managing = true;
    }

    public void ApplyManage()
    {
        ApplyWorking();
        CommitToProfile();
        NotifyChanged(force: true);
    }

    /// <summary>Promove Working→Areas sem gravar (a gravação é CommitToProfile).</summary>
    public void ApplyWorking()
    {
        Areas.Clear(); Areas.AddRange(Working);
        Exclusions.Clear(); Exclusions.AddRange(WorkingExcl);
        Managing = false;
    }

    public void CancelManage()
    {
        Managing = false;
        Working = Clone(Areas);
        WorkingExcl = new List<ScreenRect>(Exclusions);
    }

    private static List<AreaDef> Clone(List<AreaDef> src)
    {
        var list = new List<AreaDef>(src.Count);
        foreach (var a in src)
            list.Add(new AreaDef { Rect = a.Rect, Groups = new List<int>(a.Groups) });
        return list;
    }

    private List<AreaDef> Target => Managing ? Working : Areas;
    private List<ScreenRect> TargetExcl => Managing ? WorkingExcl : Exclusions;

    public AreaDef? AddArea(ScreenRect rect, bool exclusion)
    {
        // Retângulo vazio nunca entra: captura quebraria à frente.
        if (rect.W <= 0 || rect.H <= 0) return null;
        if (exclusion) { TargetExcl.Add(rect); NotifyChanged(); return null; }
        var def = new AreaDef { Rect = rect };
        for (int i = 0; i < _cfg.Profile.ColorGroups.Count; i++) def.Groups.Add(i);
        Target.Add(def);
        NotifyChanged();
        return def;
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Target.Count) return;   // índice obsoleto: ignora
        Target.RemoveAt(index); NotifyChanged();
    }  // RF-064: lista reindexa
    public void RemoveExclusionAt(int index)
    {
        if (index < 0 || index >= TargetExcl.Count) return;
        TargetExcl.RemoveAt(index); NotifyChanged();
    }

    public void ClearAll()
    {
        Target.Clear(); TargetExcl.Clear();
        NotifyChanged();
    }

    // ---- grupos de cor (RF-078/079) ----

    public void AddColorGroup()
    {
        _cfg.Profile.ColorGroups.Add(new ColorGroup());
        int idx = _cfg.Profile.ColorGroups.Count - 1;
        foreach (var a in Areas) if (!a.Groups.Contains(idx)) a.Groups.Add(idx);
        foreach (var a in Working) if (!a.Groups.Contains(idx)) a.Groups.Add(idx);
    }

    public void RemoveColorGroup(int idx)
    {
        if (_cfg.Profile.ColorGroups.Count <= 1) return;   // RF-507: ignora se único
        if (idx < 0 || idx >= _cfg.Profile.ColorGroups.Count) return;
        _cfg.Profile.ColorGroups.RemoveAt(idx);
        RenumberGroups(Areas, idx);
        RenumberGroups(Working, idx);
    }

    public static void RenumberGroups(List<AreaDef> list, int removed)
    {
        foreach (var a in list)
        {
            a.Groups.RemoveAll(g => g == removed);
            for (int i = 0; i < a.Groups.Count; i++)
                if (a.Groups[i] > removed) a.Groups[i]--;
        }
    }

    // ---- rápidas / instantâneo (RF-069..072) ----

    public void SetQuick(ScreenRect rect)
    {
        if (rect.W <= 0 || rect.H <= 0) return;
        QuickArea = new AreaDef { Rect = rect }; NotifyChanged();
    }

    public void SetSnapshot(ScreenRect rect)
    {
        if (rect.W <= 0 || rect.H <= 0) return;
        SnapshotArea = rect;
        LastSnapshot = rect;                                  // RF-070 memoriza
        NotifyChanged();
    }

    /// <summary>
    /// Fim do instantâneo: a área era para um único ciclo. Sem limpar,
    /// o plano continua retornando só ela e "gruda" (tudo traduz o mesmo
    /// lugar até reiniciar). Chamado no finally do SnapshotAsync.
    /// </summary>
    public void ClearSnapshot() => SnapshotArea = null;
    /// <summary>RF-071: tradução não-instantânea apaga a memória do instantâneo.</summary>
    public void BeginNonSnapshotTranslation() => LastSnapshot = null;

    // ---- montagem da lista final (pseudocódigo do cap. 11) ----

    public sealed class CapturePlan
    {
        public List<ScreenRect> Rects { get; } = new();
        public List<ScreenRect> Exclusions { get; } = new();
        public List<List<int>> GroupsPerRect { get; } = new();
        public bool IsSnapshot { get; set; }
    }

    public CapturePlan BuildPlan()
    {
        // O interruptor "somente mouse" vive no advanced: sincroniza aqui
        // (tela de config edita lá, o laço lê aqui a cada ciclo).
        FollowOnly = _cfg.Advanced.FollowOnly;
        var plan = new CapturePlan();
        bool onlyMouse = FollowActive && FollowOnly;
        bool hasSnap = SnapshotArea.HasValue;

        if (hasSnap && !onlyMouse)
        {
            plan.Rects.Add(Align(SnapshotArea!.Value));
            plan.GroupsPerRect.Add(AllGroups());
            plan.IsSnapshot = true;
        }

        foreach (var a in Areas)
        {
            if (!hasSnap && !onlyMouse)
            {
                plan.Rects.Add(Align(a.Rect));
                plan.GroupsPerRect.Add(new List<int>(a.Groups));
            }
        }

        plan.Exclusions.AddRange(Exclusions);

        if (QuickArea is not null && !hasSnap && !onlyMouse)
        {
            plan.Rects.Add(Align(QuickArea.Rect));
            plan.GroupsPerRect.Add(AllGroups());
        }

        if (FollowActive && FollowArea.HasValue && (!hasSnap || onlyMouse))
        {
            plan.Rects.Add(Align(FollowArea.Value));
            plan.GroupsPerRect.Add(AllGroups());
        }

        return plan;
    }

    private static ScreenRect Align(ScreenRect r) =>
        new(r.X, r.Y, FrameGeometry.AlignWidth(r.W), r.H);     // RF-077 🔒

    private List<int> AllGroups()
    {
        var l = new List<int>();
        for (int i = 0; i < _cfg.Profile.ColorGroups.Count; i++) l.Add(i);
        return l;
    }

    /// <summary>
    /// Retângulos capturados (para posicionar a saída fora deles).
    /// Inclui instantâneo/rápida quando ativos.
    /// </summary>
    public List<ScreenRect> CaptureRects()
    {
        var l = new List<ScreenRect>();
        foreach (var a in Areas) l.Add(a.Rect);
        if (QuickArea is not null) l.Add(QuickArea.Rect);
        if (SnapshotArea.HasValue) l.Add(SnapshotArea.Value);
        return l;
    }

    /// <summary>RF-065: exige ao menos uma área incremental.</summary>
    public bool CanTranslate(out string message)
    {
        FollowOnly = _cfg.Advanced.FollowOnly;
        if (Areas.Count == 0 && QuickArea is null
            && !(FollowActive && FollowOnly && FollowArea.HasValue)
            && !SnapshotArea.HasValue)
        {
            message = "Defina primeiro a área de OCR: abra o gerenciamento de áreas e desenhe um retângulo sobre o texto.";
            return false;
        }
        message = "";
        return true;
    }

    /// <summary>RF-086: quais áreas ficaram fora da área virtual (sem mover — RF-086).</summary>
    public List<int> ValidateAgainst(ScreenRect virtualScreen)
    {
        var bad = new List<int>();
        for (int i = 0; i < Areas.Count; i++)
            if (!Intersects(Areas[i].Rect, virtualScreen)) bad.Add(i);
        return bad;
    }

    private static bool Intersects(ScreenRect a, ScreenRect b) =>
        a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    // ---- notificação (RF-059/060) ----

    public void NotifyChanged(bool force = false)    {
        if (!Initialized || Applying) return;                 // RF-060
        if (!force && _notifiedOnce && _throttle.Elapsed < Throttle) return;   // RF-059 🔒
        _notifiedOnce = true;
        _throttle.Restart();
        Changed?.Invoke();
    }
}
