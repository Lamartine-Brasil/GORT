using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Gort.Config;
using Gort.Store;

namespace Gort.UI;

/// <summary>
/// Gerenciamento de chaves (RF-538): lista com identificador, tipo e estado;
/// alterna adicionar/editar pelo identificador; remover; gratuita/paga.
/// </summary>
public sealed class KeyManagerWindow : Window
{
    private readonly string _service;
    private readonly ListBox _list = new();
    private readonly TextBox _id = new();
    private readonly TextBox _secret = new();
    private readonly RadioButton _free = new() { Content = "Gratuita", IsChecked = true };
    private readonly RadioButton _paid = new() { Content = "Paga" };
    private readonly Button _save = new() { Content = "Adicionar", MinWidth = 90 };

    public KeyManagerWindow(string service, string title)
    {
        _service = service;
        Title = title;
        Width = 480; Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _id.TextChanged += (_, _) => RefreshSave();
        var del = new Button { Content = "Remover" };
        del.Click += (_, _) =>
        {
            if (_list.SelectedItem is string sel)
            {
                int at = sel.IndexOf(" [");
                string id = at > 0 ? sel[..at] : sel;
                var keys = ConfigService.LoadCreds(_service);
                keys.RemoveAll(k => k.Id == id);
                ConfigService.SaveCreds(_service, keys);
                Refresh();
            }
        };
        _save.Click += (_, _) =>
        {
            var keys = ConfigService.LoadCreds(_service);
            var ex = keys.FirstOrDefault(k => k.Id == _id.Text);
            if (ex is null)
                keys.Add(new CredentialRecord
                {
                    Id = _id.Text ?? "",
                    Secret = _secret.Text ?? "",
                    Plan = _paid.IsChecked == true ? "paid" : "free",
                });
            else
            {
                ex.Secret = _secret.Text ?? "";
                ex.Plan = _paid.IsChecked == true ? "paid" : "free";
            }
            ConfigService.SaveCreds(_service, keys);
            Refresh();
        };
        Content = new StackPanel
        {
            Margin = new Thickness(12), Spacing = 8,
            Children =
            {
                _list,
                new TextBlock { Text = "Identificador:" }, _id,
                new TextBlock { Text = "Segredo:" }, _secret,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { _free, _paid },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { _save, del },
                },
            },
        };
        Refresh();
    }

    private void Refresh()
    {
        _list.Items.Clear();
        foreach (var k in ConfigService.LoadCreds(_service))
            _list.Items.Add($"{k.Id} [{(k.Plan == "paid" ? "Paga" : "Gratuita")}]");
        RefreshSave();
    }

    private void RefreshSave()
    {
        var keys = ConfigService.LoadCreds(_service);
        _save.Content = keys.Any(k => k.Id == _id.Text) ? "Editar" : "Adicionar";
    }
}
