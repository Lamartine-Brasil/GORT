using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Gort.UI;

/// <summary>
/// Renderização de texto com contorno duplo (RF-336/386): contorno externo
/// P-80 na cor 2 + interno P-81 na cor 1 (junção arredondada) + preenchimento.
/// Sem vetor (RF-007) → texto simples sem contorno. Fonte resolvida em tempo
/// de execução (RF-387): interface do SO, depois lista de reserva (dado).
/// </summary>
public static class SkiaText
{
    /// <summary>RF-007: teste de desenho vetorial com latinos + japoneses.</summary>
    public static bool VectorOk { get; private set; } = true;

    public static bool RunVectorCheck()
    {
        try
        {
            using var bmp = new SKBitmap(200, 60);
            using var canvas = new SKCanvas(bmp);
            using var font = new SKFont(ResolveFont(null), 24);
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true,
            };
            canvas.Clear(SKColors.Black);
            canvas.DrawText("Ag 日本語 123", 5, 35, SKTextAlign.Left, font, paint);
            using var path = font.GetTextPath("Ag", new SKPoint(5, 35));
            VectorOk = path is not null && path.PointCount > 0;
        }
        catch { VectorOk = false; }
        return VectorOk;
    }

    /// <summary>
    /// Lista de reserva de famílias (dado, RF-387), por SO: cada plataforma
    /// tem suas fontes CJK (japonês sem tofu) e de interface. A ordem é
    /// interface do SO → CJK do SO → genéricas.
    /// </summary>
    public static string[] FallbackFamilies => _fallback ??= BuildFallback();

    private static string[]? _fallback;

    private static string[] BuildFallback()
    {
        if (OperatingSystem.IsMacOS())
            return ["Helvetica Neue", "Hiragino Sans", "Hiragana Sans",
                "Hiragino Kaku Gothic ProN", "Arial", "sans-serif"];
        if (OperatingSystem.IsLinux())
            return ["Noto Sans", "Noto Sans CJK JP", "Noto Sans CJK",
                "DejaVu Sans", "TakaoGothic", "sans-serif"];
        return ["Segoe UI", "Meiryo", "Yu Gothic", "Arial", "sans-serif"];
    }

    public static SKTypeface ResolveFont(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            try
            {
                var t = SKTypeface.FromFamilyName(requested);
                if (t is not null) return t;
            }
            catch { }
        }
        foreach (var f in FallbackFamilies)
        {
            try
            {
                var t = SKTypeface.FromFamilyName(f);
                if (t is not null) return t;
            }
            catch { }
        }
        return SKTypeface.Default;
    }

    public enum HAlign { Left, Center, Right }

    /// <summary>Quebra em palavras cabendo em maxWidth px (camada).</summary>
    public static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
        {
            string cur = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string trial = cur.Length == 0 ? word : cur + " " + word;
                if (font.MeasureText(trial) <= maxWidth || cur.Length == 0) cur = trial;
                else { lines.Add(cur); cur = word; }
            }
            lines.Add(cur);
        }
        return lines;
    }

    /// <summary>
    /// Desenha linhas com contorno duplo; devolve extensão real (w,h).
    /// advance = altura × P-98 (RF-365).
    /// </summary>
    public static (float W, float H) DrawOutlined(SKCanvas canvas, List<string> lines,
        float x, float y, SKTypeface face, float sizePx,
        SKColor fill, SKColor c1, SKColor c2, HAlign align, float maxWidth)
    {
        float advance = sizePx * (float)Core.Params.P98_LineAdvance;   // 🔒 1,2
        using var font = new SKFont(face, sizePx);
        using var pFill = new SKPaint
        {
            Color = fill,
            IsAntialias = true, StrokeJoin = SKStrokeJoin.Round,
        };
        using var pIn = new SKPaint
        {
            Color = c1, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = (float)Core.Params.P81_OutlineInner,
            StrokeJoin = SKStrokeJoin.Round,
        };
        using var pOut = new SKPaint
        {
            Color = c2, IsAntialias = true,
            Style = SKPaintStyle.Stroke, StrokeWidth = (float)Core.Params.P80_OutlineOuter,
            StrokeJoin = SKStrokeJoin.Round,
        };
        float maxW = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            float wline = font.MeasureText(lines[i]);
            maxW = Math.Max(maxW, wline);
            float lx = align switch
            {
                HAlign.Center => x + (maxWidth - wline) / 2,
                HAlign.Right => x + maxWidth - wline,
                _ => x,
            };
            float ly = y + sizePx + i * advance;
            if (VectorOk)
            {
                canvas.DrawText(lines[i], lx, ly, SKTextAlign.Left, font, pOut);
                canvas.DrawText(lines[i], lx, ly, SKTextAlign.Left, font, pIn);
            }
            canvas.DrawText(lines[i], lx, ly, SKTextAlign.Left, font, pFill);
        }
        return (maxW, lines.Count == 0 ? 0 : sizePx + (lines.Count - 1) * advance);
    }
}
