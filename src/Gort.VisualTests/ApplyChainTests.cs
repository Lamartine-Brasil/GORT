using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Gort.Lifecycle;
using Gort.Store;

namespace Gort.Tests;

// xUnit1031 suprimido (intencional, como em VisualRenderTests): Dispatch
// headless precisa concluir e o xUnit não tem SynchronizationContext.
#pragma warning disable xUnit1031 // blocking GetResult em teste headless

/// <summary>
/// Prova funcional da cadeia aplicar: dirige os controles de verdade numa
/// MainWindow headless, chama ApplyFromUi e confere o perfil — sem disco
/// (Save* nunca roda aqui; round-trip tem teste próprio).
/// </summary>
[Collection("visual")]
public class ApplyChainTests
{
    private static void Drive(int tab, Action<Gort.MainWindow> act)
    {
        HeadlessSetup.Session.Dispatch(() =>
        {
            // Tema do app real (como em Shot): sem ele os controles
            // renderizam sem template e o conteúdo das abas não materializa.
            var app = Avalonia.Application.Current!;
            if (!app.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().Any())
                app.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            var cfg = new ConfigService();
            var w = new Gort.MainWindow(cfg, new TranslationController(), null);
            try
            {
                w.FindControl<TabControl>("Tabs")!.SelectedIndex = tab;
                w.Show();
                // Conteúdo da aba anexa após passes de layout (1 tick não basta).
                for (int i = 0; i < 5; i++)
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
                act(w);
            }
            finally { if (w.IsVisible) w.Close(); }
        }, default).GetAwaiter().GetResult();
    }

    private static ConfigService ServiceOf(Gort.MainWindow w) =>
        (ConfigService)w.GetType().GetField("_cfg",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(w)!;

    [Fact]
    public void TrocaServico_ChegaAoPerfil()
    {
        Drive(2, w =>
        {
            var combo = w.GetVisualDescendants().OfType<ComboBox>()
                .First(c => c.ItemsSource is System.Collections.IEnumerable items
                    && items.Cast<object>().Any(o => (o as string ?? "").StartsWith("db ")));
            combo.SelectedItem = combo.ItemsSource!.Cast<object>()
                .First(o => ((o as string) ?? "").StartsWith("db "));
            w.ApplyFromUi();
            Assert.Equal("db", ServiceOf(w).Profile.TranslationService);
        });
    }

    [Fact]
    public void ModoCamada_ChegaAoPerfil()
    {
        Drive(3, w =>
        {
            var radio = w.GetVisualDescendants().OfType<RadioButton>()
                .First(r => (r.Content as string) == "Camada");
            radio.IsChecked = true;
            w.ApplyFromUi();
            Assert.Equal("layer", ServiceOf(w).Profile.WindowMode);
        });
    }

    [Fact]
    public void PosicaoCamada_ChegaAoPerfil()
    {
        Drive(3, w =>
        {
            var radio = w.GetVisualDescendants().OfType<RadioButton>()
                .First(r => (r.Content as string) == "Em cima, dentro da captura");
            radio.IsChecked = true;
            w.ApplyFromUi();
            Assert.Equal("top", ServiceOf(w).Profile.LayerPlace);
        });
    }

    [Fact]
    public void Velocidade5_ChegaAoPerfil()
    {
        Drive(1, w =>
        {
            var speeds = w.GetVisualDescendants().OfType<RadioButton>()
                .Where(r => r.GroupName == "speed").ToList();
            Assert.Equal(5, speeds.Count);
            speeds[4].IsChecked = true;
            w.ApplyFromUi();
            Assert.Equal(5, ServiceOf(w).Profile.Speed);
        });
    }

    [Fact]
    public void DesmarcarDicionario_ChegaAoPerfil()
    {
        Drive(2, w =>
        {
            var chk = w.GetVisualDescendants().OfType<CheckBox>()
                .First(c => (c.Content as string) == "Usar dicionário");
            chk.IsChecked = false;
            w.ApplyFromUi();
            Assert.False(ServiceOf(w).Profile.UseDict);
        });
    }
}
