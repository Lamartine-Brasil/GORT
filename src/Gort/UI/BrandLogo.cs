using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Gort.UI;

/// <summary>
/// Logo do GORT (pastilha índigo→violeta + G + bolha de tradução).
/// Arquivos em Assets/ (logo-16..512.png, app.ico); o exe usa app.ico.
/// Tudo com queda silenciosa: sem logo, a janela abre normal sem ícone.
/// </summary>
public static class BrandLogo
{
    private static string? Find(string name)
    {
        try
        {
            string p = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (File.Exists(p)) return p;
            // Desenvolvimento: roda a partir de src/Gort/bin — mesmo lugar.
            p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", name);
            if (File.Exists(p)) return p;
        }
        catch { }
        return null;
    }

    public static Bitmap? Image(int size = 128)
    {
        string[] cands = [$"logo-{size}.png", "logo-128.png", "logo-64.png", "logo-256.png"];
        foreach (var c in cands)
        {
            string? p = Find(c);
            if (p is not null)
            {
                try { return new Bitmap(p); } catch { }
            }
        }
        return null;
    }

    public static WindowIcon? Icon()
    {
        string? p = Find("logo-64.png") ?? Find("logo-32.png") ?? Find("logo-128.png");
        if (p is null) return null;
        try
        {
            using var fs = File.OpenRead(p);
            return new WindowIcon(fs);
        }
        catch { return null; }
    }

    /// <summary>Ícone da janela (barra de título/ALT+TAB).</summary>
    public static void Apply(Window w)
    {
        try { w.Icon = Icon(); } catch { }
    }

    /// <summary>Controle de imagem pronto p/ splash/sobre (nulo se sem asset).</summary>
    public static Control? Badge(int size = 72)
    {
        var bmp = Image(size <= 64 ? 64 : 128);
        if (bmp is null) return null;
        return new Image
        {
            Source = bmp, Width = size, Height = size,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        };
    }
}
