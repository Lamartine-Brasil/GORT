using System.Collections.Generic;
using Gort.Core;
using Gort.Ocr;

namespace Gort.Text;

/// <summary>Orientação da linha (7.3): vertical se h &gt; w × P-33.</summary>
public enum LineOrientation { Horizontal, Vertical }

/// <summary>Palavra com caixa expandida para fora (RF-153).</summary>
public readonly record struct PlacedWord(string Text, int X, int Y, int W, int H);

/// <summary>Linha (7.3): palavras, texto com espaço final (RF-152 🔒), caixa união.</summary>
public sealed class Line
{
    public List<PlacedWord> Words { get; } = new();
    public string Text { get; set; } = "";
    public int X, Y, W, H;
    public LineOrientation Orientation;
    /// <summary>Tamanho de fonte estimado (RF-164 🔒).</summary>
    public int FontSize;
}

/// <summary>Bloco de tradução (7.4).</summary>
public sealed class Block
{
    public List<Line> Lines { get; } = new();
    public bool IsTitle;
    public LineOrientation Orientation;
    public int OX, OY, OW, OH;   // origem
    public int VX, VY, VW, VH;   // visualização
    public int CX, CY, CW, CH;   // conteúdo

    /// <summary>Texto bruto: linhas unidas por quebra (RF-187: preservada na sobreposição).</summary>
    public string RawText => string.Join("\n", Lines.ConvertAll(l => l.Text));
}

/// <summary>Resultado de região (7.5, campos textuais; tradução na Etapa 7).</summary>
public sealed class RegionText
{
    public List<Line> Lines { get; } = new();
    public List<Block> Blocks { get; } = new();
}

/// <summary>Construção de linhas (15.1).</summary>
public static class Lines
{
    /// <summary>
    /// Caixa da palavra expandindo para fora (RF-153): piso na origem, teto no
    /// canto oposto; largura/altura negativas viram zero.
    /// </summary>
    public static (int X, int Y, int W, int H) ExpandBox(double x, double y, double w, double h)
    {
        if (w < 0) w = 0;
        if (h < 0) h = 0;
        int x1 = (int)System.Math.Floor(x), y1 = (int)System.Math.Floor(y);
        return (x1, y1,
            (int)System.Math.Ceiling(x + w) - x1,
            (int)System.Math.Ceiling(y + h) - y1);
    }

    /// <summary>Tamanho de fonte: mediana de min(w,h) sobre caixas positivas;
    /// par → média das centrais; piso 1; vazio → P-38 (RF-164 🔒).</summary>
    public static int FontSizeOf(IReadOnlyList<(int W, int H)> boxes)
    {
        var samples = new List<int>();
        foreach (var (w, h) in boxes)
            if (w > 0 && h > 0) samples.Add(System.Math.Min(w, h));
        if (samples.Count == 0) return Params.P38_FontWhenNone;   // 🔒 10
        samples.Sort();
        int n = samples.Count;
        if (n % 2 == 1) return System.Math.Max(1, samples[n / 2]);
        return System.Math.Max(1, (samples[n / 2 - 1] + samples[n / 2]) / 2);
    }

    public static LineOrientation Classify(int w, int h) =>
        h > w * Params.P33_VerticalRatio ? LineOrientation.Vertical : LineOrientation.Horizontal;  // 🔒

    /// <summary>Monta linhas a partir do OCR + caixa de resultado (RF-152..156).</summary>
    public static RegionText Build(OcrResult ocr, int index, bool isSnapshot)
    {
        var region = new RegionText();
        int wi = 0;
        foreach (int count in ocr.WordsPerLine)
        {
            var line = new Line();
            for (int k = 0; k < count && wi < ocr.Words.Count; k++, wi++)
            {
                var w = ocr.Words[wi];
                var (x, y, bw, bh) = ExpandBox(w.X, w.Y, w.W, w.H);
                line.Words.Add(new PlacedWord(w.Text, x, y, bw, bh));
                line.Text += w.Text + " ";                        // RF-152 🔒
            }
            if (line.Words.Count == 0) continue;
            int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
            var dims = new List<(int, int)>();
            foreach (var w in line.Words)
            {
                x1 = System.Math.Min(x1, w.X); y1 = System.Math.Min(y1, w.Y);
                x2 = System.Math.Max(x2, w.X + w.W); y2 = System.Math.Max(y2, w.Y + w.H);
                dims.Add((w.W, w.H));
            }
            line.X = x1; line.Y = y1; line.W = x2 - x1; line.H = y2 - y1;  // RF-154
            line.Orientation = Classify(line.W, line.H);                   // RF-155
            line.FontSize = FontSizeOf(dims);                              // RF-164
            region.Lines.Add(line);
        }
        return region;
    }
}
