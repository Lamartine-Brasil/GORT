using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Gort.Platform;

namespace Gort.UI;

/// <summary>
/// Seletor de janela anexada (RF-089): lista as janelas capturáveis do
/// sistema; mostra o estado ("selecionando", "capturando &lt;nome&gt;",
/// "parado") e botão de parar. Sem borda amarela: PrintWindow não desenha
/// nenhuma (RF-091 N/A).
/// </summary>
public sealed class AttachedPickerWindow : Window
{
    private readonly TextBlock _state = new();
    private readonly StackPanel _list = new() { Spacing = 4 };

    public AttachedPickerWindow()
    {
        Title = "Capturar de janela anexada";
        Width = 460; Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Refresh();
        Content = new DockPanel
        {
            Children =
            {
                new StackPanel
                {
                    Margin = new Thickness(12, 12, 12, 0),
                    Spacing = 6,
                    Children = { _state },
                },
                new ScrollViewer { Content = _list, Margin = new Thickness(12) },
            },
        };
        SetDock();
    }

    private void SetDock()
    {
        if (Content is DockPanel dock)
        {
            DockPanel.SetDock(dock.Children[0], Dock.Top);
        }
    }

    private void Refresh()
    {
        _list.Children.Clear();
        if (Platform.Windows.AttachedCapture.IsActive)
        {
            _state.Text = "Capturando " + Platform.Windows.AttachedCapture.Name;  // RF-089
            var stop = new Button { Content = "Parar", MinWidth = 100 };
            stop.Click += (_, _) =>
            {
                Platform.Windows.AttachedCapture.Stop();
                Refresh();
            };
            _list.Children.Add(stop);
            _list.Children.Add(new TextBlock
            {
                Text = "Sem borda amarela: esta captura não desenha borda.",
                Opacity = 0.7,
            });
            return;
        }
        _state.Text = "Selecionando janela…";
        foreach (var w in PlatformFactory.Current.Windows.ListCapturableWindows())
        {
            // Grade: título ocupa o espaço livre com reticências; botão fixo.
            // Antes o StackPanel horizontal estourava a largura e cortava
            // os títulos no meio sem rolagem.
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Margin = new Thickness(0, 0, 0, 4),
            };
            var name = new TextBlock
            {
                Text = w.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            ToolTip.SetTip(name, w.Title);
            var go = new Button { Content = "Anexar" };
            var handle = w.Handle;
            var title = w.Title;
            go.Click += (_, _) =>
            {
                Platform.Windows.AttachedCapture.Start(handle, title);
                Close();
            };
            row.Children.Add(name);
            row.Children.Add(go);
            Grid.SetColumn(go, 1);
            _list.Children.Add(row);
        }
    }
}
