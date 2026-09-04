using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gort.Config;

namespace Gort.UI;

/// <summary>
/// Grupos de cor por área (RF-078): caixas de seleção por grupo (índice,
/// R/G/B, faixas S/V), botão marcar todos, aplicar/cancelar.
/// </summary>
public sealed class ColorGroupsWindow : Window
{
    private readonly List<CheckBox> _boxes = new();
    private readonly List<int> _result;
    public bool Applied { get; private set; }

    public ColorGroupsWindow(string areaLabel, List<ColorGroup> all, List<int> current)
    {
        Title = $"Grupos de cor — {areaLabel}";
        Width = 420; Height = 400;
        Topmost = true;   // abre sobre as molduras (Topmost)
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _result = new List<int>(current);

        var panel = new StackPanel { Margin = new Thickness(12), Spacing = 6 };
        if (all.Count == 0)
            panel.Children.Add(new TextBlock
            {
                Text = "Nenhum grupo de cor. Crie grupos na aba Ler (Correção de imagem).",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
        for (int i = 0; i < all.Count; i++)
        {
            var g = all[i];
            var cb = new CheckBox
            {
                Content = $"[{i}] R {g.R} G {g.G} B {g.B}   S {g.S1}–{g.S2}  V {g.V1}–{g.V2}",
                IsChecked = current.Contains(i),
                Tag = i,
            };
            _boxes.Add(cb);
            panel.Children.Add(cb);
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var allBtn = new Button { Content = "Marcar todos" };
        allBtn.Click += (_, _) => { foreach (var b in _boxes) b.IsChecked = true; };
        var ok = new Button { Content = "Aplicar", MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            _result.Clear();
            foreach (var b in _boxes)
                if (b.IsChecked == true) _result.Add((int)b.Tag!);
            Applied = true;
            Close();
        };
        var cancel = new Button { Content = "Cancelar", MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        row.Children.Add(allBtn); row.Children.Add(ok); row.Children.Add(cancel);
        panel.Children.Add(row);
        Content = new ScrollViewer { Content = panel };
    }

    public List<int> Result => _result;
}
