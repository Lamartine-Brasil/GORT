namespace Gort.Input;

/// <summary>
/// Condições que deixam os atalhos inertes (RF-443): camada de seleção
/// (via Regions.InputGuard), campo de captura com foco e janela de avançadas.
/// </summary>
public static class HotkeyGuard
{
    public static bool CaptureFieldFocused { get; set; }
    public static bool AdvancedOpen { get; set; }

    public static bool Suspended =>
        Regions.InputGuard.SelectionOpen || CaptureFieldFocused || AdvancedOpen;
}
