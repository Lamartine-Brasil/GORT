using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Gort.UI;

/// <summary>
/// Navegador de configurações da comunidade (RF-541 + VI.10): busca com
/// filtro ao vivo; título original e traduzido; painel com título, links e
/// descrição clicável; aplicar baixa perfil+banco (ou vira link de download).
/// </summary>
public sealed class CommunityWindow : Window
{
    private readonly ListBox _list = new();
    private readonly TextBox _search = new() { PlaceholderText = "Buscar…" };
    private readonly TextBlock _title = new() { Text = "Carregando…" };
    private readonly TextBlock _desc = new() { Text = "" };
    private readonly Button _apply = new() { Content = "Aplicar" };
    private List<Entry> _entries = new();
    private Entry? _sel;

    internal sealed class Entry
    {
        public string Path = "";
        public string Title = "";
        public string TitleTr = "";
        public string InfoTitle = "";
        public string Links = "";
        public string Desc = "";
        public string Profile = "";
        public string Db = "";
    }

    public CommunityWindow()
    {
        Title = "Configurações da comunidade";
        Width = 700; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _search.TextChanged += (_, _) => Filter();
        _list.SelectionChanged += (_, _) => Select();
        _apply.Click += (_, _) => _ = ApplyAsync();
        var left = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        left.Children.Add(_search);
        left.Children.Add(_list);
        var right = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        right.Children.Add(_title);
        right.Children.Add(_desc);
        right.Children.Add(_apply);
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
        };
        grid.Children.Add(left);
        grid.Children.Add(right);
        Grid.SetColumn(right, 1);
        Content = grid;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            string index = await http.GetStringAsync(Update.Dist.CommunityIndexUrl);
            var list = new List<Entry>();
            foreach (var line in index.Split('\n'))
            {
                var f = line.Split('\t');                          // VI.10: tabulado
                if (f.Length < 3) continue;
                list.Add(new Entry { Path = f[0].Trim(), Title = f[1].Trim(), TitleTr = f[2].Trim() });
            }
            _entries = list;
            Filter();
        }
        catch { _title.Text = "Sem rede para carregar a lista."; }  // VI.10: ignora
    }

    private void Filter()
    {
        string q = _search.Text ?? "";
        var items = new List<string>();
        foreach (var e in _entries)
            if (q == "" || e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.TitleTr.Contains(q, StringComparison.OrdinalIgnoreCase))
                items.Add(e.Title + "  /  " + e.TitleTr);
        _list.ItemsSource = items;
    }

    private int _selGen;   // trocas rápidas: só a última seleção pinta a tela

    private async void Select()
    {
        if (_list.SelectedIndex < 0) return;
        int gen = ++_selGen;
        string shown = (string)_list.SelectedItem!;
        _sel = _entries.FirstOrDefault(e => (e.Title + "  /  " + e.TitleTr) == shown);
        if (_sel is null) return;
        var sel = _sel;
        _title.Text = sel.Title;
        _desc.Text = "Carregando…";
        _apply.Content = "Aplicar";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            string info = await http.GetStringAsync(
                Update.Dist.CommunityBase + sel.Path + "/info.txt");
            if (gen != _selGen) return;
            ParseInfo(sel, info);
            _title.Text = sel.InfoTitle != "" ? sel.InfoTitle : sel.Title;
            _desc.Text = sel.Desc + "\n" + sel.Links;
            if (sel.Profile == "" && sel.Db == "")
                _apply.Content = "Ir para a página de download";   // RF-541
        }
        catch { if (gen == _selGen) _desc.Text = "Falha ao carregar."; }
    }

    internal static void ParseInfo(Entry e, string info)
    {
        foreach (var line in info.Split('\n'))
        {
            string t = line.Trim();
            if (t.StartsWith("title:")) e.InfoTitle = t["title:".Length..].Trim();
            else if (t.StartsWith("links:")) e.Links = t["links:".Length..].Trim();
            else if (t.StartsWith("profile:")) e.Profile = t["profile:".Length..].Trim();
            else if (t.StartsWith("db:")) e.Db = t["db:".Length..].Trim();
            else if (t.StartsWith("desc:")) e.Desc = t["desc:".Length..].Trim();
        }
    }

    private async Task ApplyAsync()
    {
        var sel = _sel;
        if (sel is null) return;
        var app = (App)Avalonia.Application.Current!;
        if (sel.Profile == "" && sel.Db == "")
        {
            App.OpenUrl(Update.Dist.CommunityBase + sel.Path);
            return;
        }
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            if (sel.Profile != "")
            {
                string p = await http.GetStringAsync(
                    Update.Dist.CommunityBase + sel.Path + "/" + sel.Profile);
                string dest = System.IO.Path.Combine(Core.Paths.ProfilesDir, sel.Profile);
                string tmp = dest + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmp, p);
                System.IO.File.Move(tmp, dest, overwrite: true);
                app.Config.LoadProfileIntoMain(dest);
                app.Regions.LoadFromProfile();
            }
            if (sel.Db != "")
            {
                string d = await http.GetStringAsync(
                    Update.Dist.CommunityBase + sel.Path + "/" + sel.Db);
                string dest = System.IO.Path.Combine(Core.Paths.BaseDir, sel.Db);
                string tmp = dest + ".tmp";
                await System.IO.File.WriteAllTextAsync(tmp, d);
                System.IO.File.Move(tmp, dest, overwrite: true);
            }
            Close();
        }
        catch { _desc.Text = "Falha ao baixar."; }
    }
}
