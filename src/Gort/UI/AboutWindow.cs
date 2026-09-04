using Avalonia;
using Avalonia.Controls;

namespace Gort.UI;

/// <summary>Sobre (RF-543): versão, data, dicionários, links; logo reabre splash.</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow(string version, string date, string dicts)
    {
        Title = "Sobre o GORT";
        BrandLogo.Apply(this);
        Width = 420; Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var links = new StackPanel { Spacing = 4 };
        foreach (var (label, url) in new[]
                 {
                     ("Repositório", Config.Catalogs.Links.Repo),
                     ("Página do projeto", Config.Catalogs.Links.ProjectPage),
                     ("Comunidade", Config.Catalogs.Links.Community),
                 })
        {
            var b = new Button { Content = label };
            b.Click += (_, _) => App.OpenUrl(url);
            links.Children.Add(b);
        }
        var top = new StackPanel { Spacing = 8 };
        var badge = BrandLogo.Badge(64);
        if (badge is not null) top.Children.Add(badge);
        top.Children.Add(new TextBlock { Text = "GORT", FontSize = 28, FontWeight = Avalonia.Media.FontWeight.Bold });
        top.Children.Add(new TextBlock { Text = "por Lamartine Barbosa" });
        top.Children.Add(new TextBlock { Text = $"Versão {version} — compilada em {date}" });
        top.Children.Add(new TextBlock { Text = "Dicionários: " + dicts });
        top.Children.Add(links);
        Content = new StackPanel
        {
            Margin = new Thickness(16), Spacing = 8,
            Children = { top },
        };
    }
}
