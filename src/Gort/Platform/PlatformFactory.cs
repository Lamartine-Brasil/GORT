using System;
using System.Collections.Generic;
using Gort.Platform.Windows;

namespace Gort.Platform;

/// <summary>Overlay/atalhos/voz: contratos com disponibilidade por SO (RF-576).</summary>
internal sealed class WindowsOverlay : IOverlaySurface
{
    public bool SupportsAlwaysOnTop => true;       // C5
    public bool SupportsPerPixelAlpha => true;     // C6 (Avalonia)
    public bool SupportsClickThrough => true;      // C7
    public bool SupportsCaptureExclusion => true;  // C8 (SetWindowDisplayAffinity)
    public bool SupportsVsync => true;             // C9 (DwmFlush)
}

internal sealed class UnavailableHotkey : IGlobalHotkey
{
    public bool IsAvailable => false;   // Etapa 9
}

internal sealed class UnavailableScreenshots : IScreenshotWatcher
{
    public bool IsAvailable => false;   // Etapa 12 (RF-347)
}

internal sealed class WindowsSpeech : ISpeechService
{
    public bool IsAvailable => OperatingSystem.IsWindows();   // C15: SAPI/WinRT (Etapa 16)
    public string? UnavailableReason => IsAvailable ? null : "Síntese indisponível (RF-573).";
}

internal sealed class WindowsLayer : IPlatformLayer
{
    public IScreenCapture Capture { get; } = new WindowsCapture();
    public IWindowService Windows { get; } = new WindowsWindowService();
    public IOverlaySurface Overlay { get; } = new WindowsOverlay();
    public IGlobalHotkey Hotkey { get; } = new UnavailableHotkey();
    public IScreenshotWatcher Screenshots { get; } = new UnavailableScreenshots();
    public ISpeechService Speech { get; } = new WindowsSpeech();

    public CapabilityReport Report() => new()
    {
        Platform = "windows",
        ScreenCapture = true,
        AttachedCapture = false,     // Etapa 16
        WindowPicker = true,         // C3 (lista); seletor do sistema na Etapa 16
        FrameBounds = true,          // C4
        AlwaysOnTop = Overlay.SupportsAlwaysOnTop,
        PerPixelAlpha = Overlay.SupportsPerPixelAlpha,
        ClickThrough = Overlay.SupportsClickThrough,
        CaptureExclusion = Overlay.SupportsCaptureExclusion,
        Vsync = Overlay.SupportsVsync,
        GlobalHotkey = false,        // Etapa 9
        ScreenshotWatcher = false,   // Etapa 12
        ForegroundInfo = true,       // C12
        TrayIcon = true,             // C13
        ClipboardWatch = true,       // C14 (TextCopy + polling, Etapa 16)
        Speech = Speech.IsAvailable, // C15
        VectorText = PlatformRuntime.VectorText,   // C16/C17 (RF-007)
        MonitorScales = true,        // C18
        Notes =
        [
            "Captura anexada (C2) chega na Etapa 16; até lá o modo fica oculto (RF-576).",
            "Atalhos globais (C10) chegam na Etapa 9; sem eles, usar o controle remoto.",
        ],
    };
}

/// <summary>Camada macOS: screencapture + CGWindowList + objc + say.</summary>
internal sealed class MacLayer : IPlatformLayer
{
    public IScreenCapture Capture { get; } = new Mac.MacCapture();
    public IWindowService Windows { get; } = new Mac.MacWindows();
    public IOverlaySurface Overlay { get; } = new MacOverlay();
    public IGlobalHotkey Hotkey { get; } = new UnavailableHotkey();
    public IScreenshotWatcher Screenshots { get; } = new UnavailableScreenshots();
    public ISpeechService Speech { get; } = new MacSpeech();

    public CapabilityReport Report()
    {
        bool cap = Mac.MacCapture.IsSupported;
        bool wins = Mac.MacWindows.IsSupported;
        return new()
        {
            Platform = "macos",
            ScreenCapture = cap,
            AttachedCapture = false,
            WindowPicker = wins,
            FrameBounds = wins,
            AlwaysOnTop = true,
            PerPixelAlpha = true,
            ClickThrough = true,     // NSWindow.ignoresMouseEvents
            CaptureExclusion = false,// sem afinidade — concealer do laço cobre
            Vsync = false,
            // A implementação da fábrica é indisponível (o gancho real mora
            // no serviço de atalhos): relatório honesto, sem prometer.
            GlobalHotkey = false,
            ScreenshotWatcher = false,
            ForegroundInfo = wins,
            TrayIcon = true,
            ClipboardWatch = true,
            Speech = Speech.IsAvailable,
            VectorText = PlatformRuntime.VectorText,
            MonitorScales = true,
            Notes =
            [
                "Na primeira captura o sistema pede permissão de “Gravação de Tela”.",
                "Atalhos globais usam o hook do sistema; se negado, use o controle remoto.",
                "Sobreposição: as janelas próprias são ocultadas durante a captura.",
                "Captura anexada (janela coberta) não tem equivalente no macOS.",
            ],
        };
    }

    private sealed class MacOverlay : IOverlaySurface
    {
        public bool SupportsAlwaysOnTop => true;
        public bool SupportsPerPixelAlpha => true;
        public bool SupportsClickThrough => true;
        public bool SupportsCaptureExclusion => false;
        public bool SupportsVsync => false;
    }

    private sealed class MacSpeech : ISpeechService
    {
        public bool IsAvailable => Audio.SpeechService.CliToolAvailable;
        public string? UnavailableReason => IsAvailable ? null : "Síntese indisponível (RF-573).";
    }
}

/// <summary>Camada Linux: grim/maim/import/scrot + wmctrl/xdotool + XShape.</summary>
internal sealed class LinuxLayer : IPlatformLayer
{
    public IScreenCapture Capture { get; } = new Linux.LinuxCapture();
    public IWindowService Windows { get; } = new Linux.LinuxWindows();
    public IOverlaySurface Overlay { get; } = new LinuxOverlay();
    public IGlobalHotkey Hotkey { get; } = new UnavailableHotkey();
    public IScreenshotWatcher Screenshots { get; } = new UnavailableScreenshots();
    public ISpeechService Speech { get; } = new LinuxSpeech();

    public CapabilityReport Report()
    {
        bool cap = Linux.LinuxCapture.IsSupported;
        bool wins = Linux.LinuxWindows.IsSupported;
        bool x11 = Linux.LinuxFx.IsX11;
        var notes = new List<string>();
        if (!cap) notes.Add(Linux.LinuxCapture.UnavailableHint ?? "Sem captura.");
        if (!wins) notes.Add("Sem lista de janelas: " +
            (new Linux.LinuxWindows().UnavailableReason ?? ""));
        if (!x11) notes.Add("Wayland: sem click-through; as janelas de tradução " +
            "ficam normais e são ocultadas durante a captura. Prefira áreas fixas.");
        notes.Add("Captura anexada (janela coberta) não tem equivalente no Linux.");
        return new()
        {
            Platform = "linux",
            ScreenCapture = cap,
            AttachedCapture = false,
            WindowPicker = wins,
            FrameBounds = wins,
            AlwaysOnTop = true,
            PerPixelAlpha = true,
            ClickThrough = x11,      // XShape; Wayland não tem API
            CaptureExclusion = false,// sem afinidade — concealer do laço cobre
            Vsync = false,
            GlobalHotkey = x11,      // SharpHook no X11; Wayland sem API global
            ScreenshotWatcher = x11,
            ForegroundInfo = wins,
            TrayIcon = true,
            ClipboardWatch = true,
            Speech = Speech.IsAvailable,
            VectorText = PlatformRuntime.VectorText,
            MonitorScales = true,
            Notes = notes,
        };
    }

    private sealed class LinuxOverlay : IOverlaySurface
    {
        public bool SupportsAlwaysOnTop => true;
        public bool SupportsPerPixelAlpha => true;
        public bool SupportsClickThrough => Linux.LinuxFx.IsX11;
        public bool SupportsCaptureExclusion => false;
        public bool SupportsVsync => false;
    }

    private sealed class LinuxSpeech : ISpeechService
    {
        public bool IsAvailable => Audio.SpeechService.CliToolAvailable;
        public string? UnavailableReason => IsAvailable ? null
            : "Sem sintetizador. Instale `espeak-ng`/`espeak` ou `speech-dispatcher` (spd-say).";
    }
}

/// <summary>SO desconhecido: nada disponível, degradação honesta (RF-569).</summary>
internal sealed class UnsupportedLayer : IPlatformLayer
{
    public IScreenCapture Capture { get; } = new NullCapture();
    public IWindowService Windows { get; } = new NullWindows();
    public IOverlaySurface Overlay { get; } = new NullOverlay();
    public IGlobalHotkey Hotkey { get; } = new UnavailableHotkey();
    public IScreenshotWatcher Screenshots { get; } = new UnavailableScreenshots();
    public ISpeechService Speech { get; } = new NullSpeech();

    public CapabilityReport Report() => new()
    {
        Platform = "unknown",
        Notes =
        [
            "Plataforma não reconhecida: nenhuma tradução é possível (RF-569).",
        ],
    };

    private sealed class NullCapture : IScreenCapture
    {
        public ScreenRect VirtualScreen => new(0, 0, 0, 0);
        public IReadOnlyList<MonitorInfo> GetMonitors() => [];
        public Imaging.RegionImage? CaptureRect(int i, ScreenRect r, bool o) => null;
    }

    private sealed class NullWindows : IWindowService
    {
        public bool IsAvailable => false;
        public string? UnavailableReason => "Sem permissão de gravação de tela (RF-569).";
        public IReadOnlyList<WindowRef> ListCapturableWindows() => [];
        public WindowRef? ForegroundWindow() => null;
        public ScreenRect FrameBounds(WindowRef w) => new(0, 0, 0, 0);
        public ScreenRect ClientOrigin(WindowRef w) => new(0, 0, 0, 0);
    }

    private sealed class NullOverlay : IOverlaySurface
    {
        public bool SupportsAlwaysOnTop => false;
        public bool SupportsPerPixelAlpha => false;
        public bool SupportsClickThrough => false;
        public bool SupportsCaptureExclusion => false;
        public bool SupportsVsync => false;
    }

    private sealed class NullSpeech : ISpeechService
    {
        public bool IsAvailable => false;
        public string? UnavailableReason => "Síntese indisponível (RF-573).";
    }
}

/// <summary>Fábrica: uma implementação por SO atrás da abstração (RF-577).</summary>
public static class PlatformFactory
{
    public static IPlatformLayer Current { get; } =
        OperatingSystem.IsWindows() ? (IPlatformLayer)new WindowsLayer()
        : OperatingSystem.IsMacOS() ? new MacLayer()
        : OperatingSystem.IsLinux() ? new LinuxLayer()
        : new UnsupportedLayer();

    /// <summary>
    /// Geometria da área de trabalho (pixels físicos) fornecida pela UI
    /// (Avalonia Screens) na inicialização. Usada pelas capturas CLI para
    /// recorte e validação — sem ela, só há o fallback vazio.
    /// </summary>
    public static Func<IReadOnlyList<MonitorInfo>>? DesktopGeometry { get; set; }

    /// <summary>
    /// Ocultação das janelas próprias durante a captura (fornecida pelo App;
    /// Windows mantém o nulo — C8 por afinidade).
    /// </summary>
    public static ICaptureConcealer CaptureConcealer { get; set; } =
        NullConcealer.Instance;
}
