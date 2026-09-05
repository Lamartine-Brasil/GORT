using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Gort.Imaging;
using Gort.Locale;
using Gort.Platform;
using Gort.Regions;
using Gort.Store;

namespace Gort.UI;

/// <summary>
/// Gerenciamento de áreas (RF-062, RF-085): adicionar área/exclusão, limpar,
/// aplicar (confirma) ou fechar (reverte — RF-061). As molduras só existem
/// enquanto esta janela está aberta. Inclui "ver resultado da imagem" (RF-083).
/// </summary>
public sealed class AreasWindow : Window
{
    private readonly ConfigService _cfg;
    private readonly RegionManager _mgr;
    private readonly StackPanel _list = new() { Spacing = 4 };
    private readonly Dictionary<Guid, AreaFrameWindow> _frames = new();
    private readonly List<(AreaFrameWindow Frame, int ExclIndex)> _exclFrames = new();

    public AreasWindow(ConfigService cfg, RegionManager mgr)
    {
        _cfg = cfg; _mgr = mgr;
        Title = "Áreas de OCR";
        Width = 460; Height = 520;
        // Sempre no topo JUNTO com as molduras (que são Topmost): sem isso,
        // uma moldura grande cobre esta janela e o usuário perde o acesso a
        // ela — não há como clicar numa janela normal sob uma Topmost.
        // Entre Topmost, a ordem é por ativação: clicar aqui traz p/ frente.
        Topmost = true;

        // Wrap: os 4 botões somem ~470 px e a janela tem 460 — sem quebra o
        // último ("Ver resultado…") era cortado na borda direita.
        var top = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(12, 12, 12, 0) };
        var add = new Button { Content = "Adicionar área" };
        add.Click += (_, _) => AddFlow(exclusion: false);
        var addEx = new Button { Content = "Adicionar área de exclusão" };
        addEx.Click += (_, _) => AddFlow(exclusion: true);
        var clear = new Button { Content = "Limpar tudo" };
        clear.Click += (_, _) => { _mgr.ClearAll(); RebuildFrames(); RefreshList(); };
        var view = new Button { Content = "Ver resultado da imagem" };
        view.Click += (_, _) => OpenDropperFirst();
        top.Children.Add(add); top.Children.Add(addEx); top.Children.Add(clear); top.Children.Add(view);

        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12) };
        var apply = new Button { Content = "Aplicar", MinWidth = 100 };
        apply.Click += (_, _) => { _mgr.ApplyManage(); CloseFrames(); Close(); };
        bottom.Children.Add(apply);

        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top); dock.Children.Add(bottom);
        dock.Children.Add(new ScrollViewer { Content = _list, Margin = new Thickness(12) });
        Content = dock;

        Closed += (_, _) =>
        {
            if (_mgr.Managing) _mgr.CancelManage();   // fechar sem aplicar reverte
            CloseFrames();
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _mgr.BeginManage();
        RebuildFrames();
        RefreshList();
        Activate();   // começa acima das molduras recém-criadas
    }

    private double ScaleOf(AreaFrameWindow f) =>
        Screens.ScreenFromWindow(f)?.Scaling ?? Screens.Primary?.Scaling ?? 1.0;

    private ScreenRect Virtual()
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

    // ---- molduras ----

    private void RebuildFrames()
    {
        CloseFrames();
        int i = 0;
        foreach (var a in _mgr.Working)
        {
            var f = MakeFrame(i, a.Rect, exclusion: false, id: a.Id);
            _frames[a.Id] = f;
            f.Show();
            i++;
        }
        for (int k = 0; k < _mgr.WorkingExcl.Count; k++)
        {
            var f = MakeFrame(k, _mgr.WorkingExcl[k], exclusion: true, id: null);
            _exclFrames.Add((f, k));
            f.Show();
        }
    }

    private AreaFrameWindow MakeFrame(int index, ScreenRect cap, bool exclusion, Guid? id)
    {
        double scale = Screens.Primary?.Scaling ?? 1.0;
        var f = new AreaFrameWindow(index, cap, scale, exclusion, ScaleOf, Virtual);
        f.GeometryChanged += OnFrameGeometry;
        f.CloseRequested += OnFrameClose;
        f.DropperRequested += OnFrameDropper;
        f.GroupsRequested += OnFrameGroups;
        if (id.HasValue) f.Tag = id.Value;
        return f;
    }

    private void OnFrameGeometry(AreaFrameWindow f)
    {
        var cap = f.CurrentCapture();
        if (f.Tag is Guid id && FindWorking(id) is { } def) def.Rect = cap;
        else
        {
            int ei = _exclFrames.FindIndex(t => t.Frame == f);
            if (ei >= 0) _mgr.WorkingExcl[_exclFrames[ei].ExclIndex] = cap;
        }
        _mgr.NotifyChanged();
        RefreshList();
    }

    private AreaDef? FindWorking(Guid id)
    {
        foreach (var a in _mgr.Working) if (a.Id == id) return a;
        return null;
    }

    private void OnFrameClose(AreaFrameWindow f)
    {
        if (f.Tag is Guid id)
        {
            int i = _mgr.Working.FindIndex(a => a.Id == id);
            if (i >= 0) _mgr.RemoveAt(i);                        // RF-064 reindexa
        }
        else
        {
            int ei = _exclFrames.FindIndex(t => t.Frame == f);
            if (ei >= 0) _mgr.RemoveExclusionAt(_exclFrames[ei].ExclIndex);
        }
        RebuildFrames();
        RefreshList();
    }

    private void OnFrameDropper(AreaFrameWindow f)
    {
        string title = "Área de exclusão";
        if (f.Tag is Guid gid && FindWorking(gid) is { } wdef)
            title = $"Área {_mgr.Working.IndexOf(wdef) + 1} — {f.CurrentCapture().W}×{f.CurrentCapture().H}";
        OpenDropper(f.CurrentCapture(), title);
    }

    private void OnFrameGroups(AreaFrameWindow f)
    {
        if (f.Tag is not Guid id || FindWorking(id) is not { } def) return;
        var w = new ColorGroupsWindow($"Área {_mgr.Working.IndexOf(def) + 1}",
            _cfg.Profile.ColorGroups, def.Groups);
        w.Closed += (_, _) =>
        {
            if (w.Applied) { def.Groups.Clear(); def.Groups.AddRange(w.Result); _mgr.NotifyChanged(); }
        };
        w.Show(this);
    }

    private void CloseFrames()
    {
        foreach (var f in _frames.Values) f.Close();
        foreach (var (f, _) in _exclFrames) f.Close();
        _frames.Clear();
        _exclFrames.Clear();
    }

    // ---- fluxos ----

    private SelectionWindow? _selWin;   // uma seleção por vez (sem órfãs)

    private void AddFlow(bool exclusion)
    {
        try { _selWin?.Close(); } catch { }
        var v = Virtual();
        var sel = new SelectionWindow(v, Screens.Primary?.Scaling ?? 1.0,
            _cfg.Advanced.SelectBg, _cfg.Advanced.SelectAccent);
        sel.Selected += rect =>
        {
            _mgr.AddArea(rect, exclusion);
            RebuildFrames();
            RefreshList();
        };
        _selWin = sel;
        sel.Closed += (_, _) => { if (_selWin == sel) _selWin = null; };
        sel.Show(this);
    }

    private void OpenDropperFirst()
    {
        if (_mgr.Working.Count == 0) { Inform(Strings._("no_area.title")); return; }  // RF-084
        var a = _mgr.Working[0];
        OpenDropper(a.Rect, $"Área 1 — {a.Rect.W}×{a.Rect.H}");
    }

    private void OpenDropper(ScreenRect rect, string title)
    {
        var img = PlatformFactory.Current.Capture.CaptureRect(0, rect, false);
        if (img is null) { Inform("A região não produziu imagem."); return; }
        var (mode, groups, thr) = FilterSpec();
        new DropperWindow(title, img, mode, groups, thr).Show(this);
    }

    private (FilterMode, List<(int, int, int, int, int, int, int)>, int) FilterSpec()
    {
        var mode = _cfg.Profile.ColorFilter switch
        {
            "rgb" => FilterMode.Rgb,
            "hsv" => FilterMode.Hsv,
            "threshold" => FilterMode.Threshold,
            _ => FilterMode.None,
        };
        var groups = new List<(int, int, int, int, int, int, int)>();
        foreach (var g in _cfg.Profile.ColorGroups)
            groups.Add((g.R, g.G, g.B, g.S1, g.S2, g.V1, g.V2));
        return (mode, groups, _cfg.Profile.Threshold);
    }

    private async void Inform(string text)
    {
        var d = new Window
        {
            Title = "GORT", Width = 360, Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new TextBlock { Text = text, Margin = new Thickness(16) },
        };
        await d.ShowDialog(this);
    }

    private void RefreshList()
    {
        _list.Children.Clear();
        for (int i = 0; i < _mgr.Working.Count; i++)
        {
            var a = _mgr.Working[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"Área {i + 1} — {a.Rect.W}×{a.Rect.H} @ ({a.Rect.X},{a.Rect.Y}) [grupos {a.Groups.Count}]",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            int idx = i;
            var del = new Button { Content = "Remover" };
            del.Click += (_, _) => { _mgr.RemoveAt(idx); RebuildFrames(); RefreshList(); };
            row.Children.Add(del);
            _list.Children.Add(row);
        }
        for (int i = 0; i < _mgr.WorkingExcl.Count; i++)
        {
            var r = _mgr.WorkingExcl[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock
            {
                Text = $"Área de exclusão {i + 1} — {r.W}×{r.H} @ ({r.X},{r.Y})",
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            });
            int idx = i;
            var del = new Button { Content = "Remover" };
            del.Click += (_, _) => { _mgr.RemoveExclusionAt(idx); RebuildFrames(); RefreshList(); };
            row.Children.Add(del);
            _list.Children.Add(row);
        }
    }
}
