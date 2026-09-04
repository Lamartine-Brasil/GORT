using System.Collections.Generic;

namespace Gort.Ocr;

/// <summary>Palavra (7.2): texto + caixa em pixels da imagem recebida.</summary>
public sealed class OcrWord
{
    public string Text { get; init; } = "";
    public int X { get; init; }
    public int Y { get; init; }
    public int W { get; init; }
    public int H { get; init; }
}

/// <summary>Resultado estruturado do OCR (contrato 6.4).</summary>
public sealed class OcrResult
{
    public int LineCount { get; init; }
    public List<OcrWord> Words { get; init; } = new();
    /// <summary>Quantidade de palavras de cada linha, em ordem de leitura.</summary>
    public List<int> WordsPerLine { get; init; } = new();
    public bool IsEmpty => Words.Count == 0;
    /// <summary>Erro do motor (RF-145): vai no campo de texto, ciclo continua.</summary>
    public string? Error { get; init; }

    public static OcrResult Fail(string message) =>
        new() { Error = message };
}

/// <summary>
/// Conversão de quadrilátero em caixa (RF-142 🔒): mínimo e máximo dos quatro
/// pontos em cada eixo — nunca diferença direta (evita W/H negativos em texto
/// rotacionado).
/// </summary>
public static class QuadBox
{
    public static (int X, int Y, int W, int H) FromPoints(
        (int X, int Y) p0, (int X, int Y) p1, (int X, int Y) p2, (int X, int Y) p3)
    {
        int x1 = System.Math.Min(System.Math.Min(p0.X, p1.X), System.Math.Min(p2.X, p3.X));
        int y1 = System.Math.Min(System.Math.Min(p0.Y, p1.Y), System.Math.Min(p2.Y, p3.Y));
        int x2 = System.Math.Max(System.Math.Max(p0.X, p1.X), System.Math.Max(p2.X, p3.X));
        int y2 = System.Math.Max(System.Math.Max(p0.Y, p1.Y), System.Math.Max(p2.Y, p3.Y));
        return (x1, y1, System.Math.Max(0, x2 - x1), System.Math.Max(0, y2 - y1));
    }
}
