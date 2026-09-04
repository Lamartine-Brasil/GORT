namespace Gort.Imaging;

/// <summary>
/// Imagem de região (7.1): largura, altura, canais e bytes linha a linha,
/// sem preenchimento. Opcionalmente a variante original sem tratamento
/// (só solicitada em sobreposição + cor automática, RF-098; liberada em RF-099).
/// </summary>
public sealed class RegionImage
{
    /// <summary>Índice da área de OCR de origem (base 0). Ausente da lista = sem imagem (6.2).</summary>
    public int Index { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>1 (cinza), 3 (BGR) ou 4 (BGRA). A captura entrega 4.</summary>
    public int Channels { get; init; }

    /// <summary>Pixels linha a linha, sem preenchimento. BGRA quando Channels == 4.</summary>
    public required byte[] Bytes { get; init; }

    public int OrigWidth { get; init; }
    public int OrigHeight { get; init; }
    public byte[]? OrigBytes { get; set; }

    public bool IsEmpty => Width <= 0 || Height <= 0 || Bytes.Length == 0;
}
