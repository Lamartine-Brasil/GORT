using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Gort.UI;

/// <summary>
/// Design-system do GORT (tokens únicos): 7 cores, ritmo base 8, raios
/// 10/6/6. Tudo via code-behind — sem XAML estilizado, sem nova fonte.
/// </summary>
public static class GortTheme
{
    public static readonly Color Accent = Color.FromRgb(0x35, 0x50, 0xF2);
    public static readonly Color Text = Color.FromRgb(0x1A, 0x1D, 0x26);
    public static readonly Color Muted = Color.FromRgb(0x6B, 0x72, 0x80);
    public static readonly Color Border = Color.FromRgb(0xE2, 0xE4, 0xEB);
    public static readonly Color Bg = Color.FromRgb(0xF7, 0xF8, 0xFB);
    public static readonly Color Surface = Colors.White;
    public static readonly Color DarkPreview = Color.FromRgb(0x1A, 0x1D, 0x26);

    /// <summary>Cartão de seção: fundo branco, borda fina, raio 10.</summary>
    public static Border Card(Control body)
    {
        return new Border
        {
            Background = new SolidColorBrush(Surface),
            BorderBrush = new SolidColorBrush(Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 12),
            Child = body,
        };
    }

    /// <summary>Título de seção: 12px, negrito, cinza-escuro legível.</summary>
    public static TextBlock SectionTitle(string t) =>
        new()
        {
            Text = StripEmoji(t).ToUpperInvariant(),
            FontSize = 12,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
            Margin = new Thickness(0, 8, 0, 2),
        };

    // Títulos de seção são texto puro: emoji de decoração vira "??" em
    // fontes sem o glifo e polui o layout (as abas já têm ícone).
    private static string StripEmoji(string t)
    {
        var sb = new System.Text.StringBuilder(t.Length);
        foreach (char c in t)
        {
            if (char.IsSurrogate(c)) continue;
            var cat = char.GetUnicodeCategory(c);
            if (cat == System.Globalization.UnicodeCategory.OtherSymbol) continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Texto auxiliar: 12px atenuado, com quebra.</summary>
    public static TextBlock Help(string t) =>
        new()
        {
            Text = t,
            FontSize = 12,
            Foreground = new SolidColorBrush(Muted),
            TextWrapping = TextWrapping.Wrap,
        };

    /// <summary>Botão primário: índigo, texto branco.</summary>
    public static void Primary(Button b)
    {
        b.Background = new SolidColorBrush(Accent);
        b.Foreground = Brushes.White;
        b.FontWeight = FontWeight.SemiBold;
        b.MinHeight = 32;
        b.CornerRadius = new CornerRadius(6);
    }

    /// <summary>Botão secundário: branco com borda visível.</summary>
    public static void Secondary(Button b)
    {
        b.Background = new SolidColorBrush(Surface);
        b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        b.BorderThickness = new Thickness(1);
        b.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37));
        b.FontWeight = FontWeight.Medium;
        b.MinHeight = 32;
        b.CornerRadius = new CornerRadius(6);
    }

    /// <summary>Botão terciário: só texto índigo, sem borda nem fundo.</summary>
    public static void Link(Button b)
    {
        b.Background = Brushes.Transparent;
        b.BorderThickness = new Thickness(0);
        b.Foreground = new SolidColorBrush(Accent);
        b.MinHeight = 32;
    }

    /// <summary>Linha de formulário: label com largura fixa e altura de input.</summary>
    public static TextBlock FieldLabel(string t, double width = 140) =>
        new()
        {
            Text = t,
            Width = width,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Text),
        };
}
