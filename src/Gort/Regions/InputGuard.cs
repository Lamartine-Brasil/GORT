namespace Gort.Regions;

/// <summary>
/// Guarda de entrada: enquanto a camada de seleção está aberta, os atalhos
/// globais ficam inertes (RF-053; consumido pelo interceptador na Etapa 9).
/// </summary>
public static class InputGuard
{
    public static bool SelectionOpen { get; set; }
}
