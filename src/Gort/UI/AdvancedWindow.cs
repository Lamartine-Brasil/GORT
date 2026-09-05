using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Gort.Locale;
using Gort.Store;

namespace Gort.UI;

/// <summary>
/// Janela de opções avançadas (V.3): hospeda o <see cref="AdvancedPanel"/>
/// com barra de Aplicar/Restaurar (RF-531). Aberta, bloqueia os atalhos
/// globais (RF-443). O mesmo painel vive distribuído na principal.
/// </summary>
public sealed class AdvancedWindow : Window
{
    public AdvancedWindow(ConfigService cfg)
    {
        Title = Strings._("advanced.open");
        Width = 720; Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Input.HotkeyGuard.AdvancedOpen = true;                 // RF-443
        Closed += (_, _) => Input.HotkeyGuard.AdvancedOpen = false;

        var panel = new AdvancedPanel(cfg);
        panel.NeedsRebuild += () => Close();   // restaurou: fecha como antes
        UI.BrandLogo.Apply(this);

        var apply = new Button { Content = Strings._("advanced.apply"), MinWidth = 110 };
        apply.Click += (_, _) => { panel.Apply(); Close(); };
        var restore = new Button { Content = Strings._("advanced.restore") };
        restore.Click += (_, _) => panel.RestoreDefaults();
        var dock = new DockPanel();
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(12),
        };
        bar.Children.Add(apply); bar.Children.Add(restore);
        DockPanel.SetDock(bar, Dock.Bottom);
        dock.Children.Add(bar);
        dock.Children.Add(panel);
        Content = dock;
    }
}
