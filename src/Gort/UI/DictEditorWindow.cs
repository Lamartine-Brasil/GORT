using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Gort.UI;

/// <summary>
/// Editor rápido de dicionário (RF-537): texto reconhecido pré-carregado,
/// campo de correção, aceitar acrescenta e recarrega. Enquanto aberto, a
/// cópia automática fica suspensa (RF-475) — via App.DictEditorOpen.
/// </summary>
public sealed class DictEditorWindow : Window
{
    public DictEditorWindow(string recognized, string dictPath)
    {
        Title = "Correção de OCR";
        Width = 420; Height = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // Cast defensivo: a janela também é instanciada em testes headless
        // (e por atalhos) sem o App real — nunca deve lançar no construtor.
        var app = Avalonia.Application.Current as App;
        if (app is not null)
        {
            app.DictEditorOpen = true;
            Closed += (_, _) => app.DictEditorOpen = false;
        }

        var src = new TextBox { Text = recognized, Margin = new Thickness(0, 0, 0, 4) };
        var dst = new TextBox { PlaceholderText = "Correção…" };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var ok = new Button { Content = "Aceitar", MinWidth = 90 };
        ok.Click += (_, _) =>
        {
            if (src.Text?.Length > 0 && dst.Text?.Length > 0)   // RF-184
            {
                var store = new Text.DictionaryStore();
                store.Load(dictPath);
                store.AddPair(src.Text, dst.Text, dictPath);
                app?.CurrentLoop?.ReloadDict();
            }
            Close();
        };
        var cancel = new Button { Content = "Cancelar", MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        row.Children.Add(ok); row.Children.Add(cancel);

        Content = new StackPanel
        {
            Margin = new Thickness(12), Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Texto reconhecido:" },
                src,
                new TextBlock { Text = "Correção:" },
                dst,
                row,
            },
        };
    }
}
