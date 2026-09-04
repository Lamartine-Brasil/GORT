using System.Collections.Generic;

namespace Gort.Loop;

/// <summary>Bloco para a sobreposição: texto traduzido + geometria de origem.</summary>
public sealed class OverlayBlock
{
    public int AreaIndex;
    public string Text = "";
    public int OX, OY, OW, OH;      // caixa de origem (espaço da imagem)
    public List<(int X, int Y, int W, int H)> LineBoxes { get; } = new();
    public List<(int X, int Y, int W, int H)> WordBoxes { get; } = new();
    public bool Vertical;
    public bool IsTitle;
    public float FontPrefPx;        // tamanho preferido (RF-360, px de desenho)
    public float BodyPx;            // tamanho do corpo da área (RF-360 passo 2)
}

/// <summary>Região para a sobreposição: captura + original + blocos.</summary>
public sealed class OverlayRegion
{
    public int Index;
    public Platform.ScreenRect Rect;   // captura em tela
    public double Zoom = 1;
    public int ClientX, ClientY;       // RF-353: origem limita por baixo (anexada)
    public byte[]? OrigBytes;          // original p/ cor automática (RF-098)
    public int OrigW, OrigH;
    public List<OverlayBlock> Blocks { get; } = new();
}

/// <summary>Quadro completo entregue à sobreposição em um desenho.</summary>
public sealed class OverlayFrame
{
    public List<OverlayRegion> Regions { get; } = new();
}
