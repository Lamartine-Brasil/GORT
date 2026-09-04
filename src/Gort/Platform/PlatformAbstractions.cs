using System.Collections.Generic;
using Gort.Imaging;

namespace Gort.Platform;

/// <summary>Retângulo em coordenadas globais de pixels físicos da área de trabalho.</summary>
public readonly record struct ScreenRect(int X, int Y, int W, int H)
{
    public bool IsEmpty => W <= 0 || H <= 0;
}

/// <summary>Monitor: retângulo global (pode ter origem negativa, RF-100) + escala.</summary>
public readonly record struct MonitorInfo(ScreenRect Bounds, double Scale);

/// <summary>Janela capturável (C3) / em primeiro plano (C12).</summary>
public readonly record struct WindowRef(nint Handle, string Title);

/// <summary>
/// C1 — Capturar uma região retangular da tela como imagem (RF-088 fonte 1, RF-100).
/// Contrato em pixels físicos globais; retângulo fora de qualquer monitor
/// devolve nulo — não é erro (6.2, Parte VIII).
/// </summary>
public interface IScreenCapture
{
    RegionImage? CaptureRect(int index, ScreenRect rect, bool needOriginal);
    IReadOnlyList<MonitorInfo> GetMonitors();
    ScreenRect VirtualScreen { get; }

    /// <summary>
    /// RF-088 fonte 2 — janela ativa (multiplataforma): captura o cliente
    /// cheio da janela em primeiro plano e recorta a área. Padrão: sem
    /// suporte (nulo) — cada SO implementa com suas APIs.
    /// </summary>
    bool SupportsClientArea => false;
    RegionImage? CaptureClientArea(int index, ScreenRect areaScreen, bool needOriginal) => null;
}

/// <summary>
/// C2/C3/C4/C12 — janelas: fluxo de janela coberta (Etapa 16), seletor,
/// limites estendidos do quadro (RF-092) e janela em primeiro plano.
/// </summary>
public interface IWindowService
{
    IReadOnlyList<WindowRef> ListCapturableWindows();
    WindowRef? ForegroundWindow();
    ScreenRect FrameBounds(WindowRef window);
    ScreenRect ClientOrigin(WindowRef window);
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
}

/// <summary>
/// C5..C9 — superfície de overlay: sempre-no-topo, transparência por pixel,
/// atravessável a cliques, exclusão de captura, sincronia com o compositor.
/// Implementação real nas Etapas 11–12; aqui o contrato (RF-577).
/// </summary>
public interface IOverlaySurface
{
    bool SupportsAlwaysOnTop { get; }
    bool SupportsPerPixelAlpha { get; }
    bool SupportsClickThrough { get; }
    bool SupportsCaptureExclusion { get; }
    bool SupportsVsync { get; }
}

/// <summary>
/// C10 — atalho global de teclado (Etapa 9); C11 — atalho de captura do SO
/// (Etapa 12, RF-347); C15 — síntese de voz (Etapa 16). Contratos (RF-577).
/// </summary>
public interface IGlobalHotkey
{
    bool IsAvailable { get; }
}

public interface IScreenshotWatcher
{
    bool IsAvailable { get; }
}

public interface ISpeechService
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
}

/// <summary>
/// Relatório de capacidades detectadas na inicialização (RF-576): a UI
/// oculta/desabilita o indisponível com explicação, nunca descobre no laço.
/// </summary>
public sealed class CapabilityReport
{
    public required string Platform { get; init; }
    public bool ScreenCapture { get; init; }
    public bool AttachedCapture { get; init; }
    public bool WindowPicker { get; init; }
    public bool FrameBounds { get; init; }
    public bool AlwaysOnTop { get; init; }
    public bool PerPixelAlpha { get; init; }
    public bool ClickThrough { get; init; }
    public bool CaptureExclusion { get; init; }
    public bool Vsync { get; init; }
    public bool GlobalHotkey { get; init; }
    public bool ScreenshotWatcher { get; init; }
    public bool ForegroundInfo { get; init; }
    public bool TrayIcon { get; init; }
    public bool ClipboardWatch { get; init; }
    public bool Speech { get; init; }
    public bool VectorText { get; init; }
    public bool MonitorScales { get; init; }
    public List<string> Notes { get; init; } = [];
}

/// <summary>Camada de plataforma: uma implementação por SO (RF-577).</summary>
public interface IPlatformLayer
{
    IScreenCapture Capture { get; }
    IWindowService Windows { get; }
    IOverlaySurface Overlay { get; }
    IGlobalHotkey Hotkey { get; }
    IScreenshotWatcher Screenshots { get; }
    ISpeechService Speech { get; }
    CapabilityReport Report();
}
