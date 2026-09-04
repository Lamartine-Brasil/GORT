using System;
using System.Collections.Generic;
using Gort.Platform;

namespace Gort.Regions;

/// <summary>
/// Área em coordenadas físicas de tela = retângulo de CAPTURA (já descontadas
/// borda/barra da moldura, RF-073). Groups = índices em Profile.ColorGroups.
/// O índice da área é sua posição na lista (reindexação automática — RF-064).
/// </summary>
public sealed class AreaDef
{
    public Guid Id { get; } = Guid.NewGuid();
    public ScreenRect Rect;
    public List<int> Groups = new();
}
