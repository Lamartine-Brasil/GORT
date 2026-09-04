using System;
using System.Collections.Concurrent;
using System.Threading;
using Gort.Config;

namespace Gort.Input;

/// <summary>
/// Serviço de atalhos (cap. 22): hook global → matcher → fila → worker.
/// O worker executa as ações fora do hook para nunca prendê-lo (RF-011).
/// Ordem estável de verificação = ordem de registro (RF-439).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly HotkeyMatcher _matcher = new();
    private readonly BlockingCollection<(int Key, bool Down)> _queue = new();
    private readonly Thread _worker;
    private bool _running = true;
    private Platform.Windows.WinHook? _hook;
    private SharpHookHotkey? _sharpHook;

    public event Action<string>? ActionFired;
    /// <summary>C11: tecla de captura de tela do SO (Etapa 12 consome).</summary>
    public event Action? ScreenshotKey;

    public HotkeyService()
    {
        _worker = new Thread(Work) { IsBackground = true, Name = "GORT-hotkey" };
        _worker.Start();
    }

    /// <summary>Carrega as 7 ações + avançados (RF-444/447) dos arquivos.</summary>
    public void Reload(ShortcutFile shortcuts, AdvancedOptions adv)
    {
        _matcher.Clear();
        foreach (var (action, def) in ShortcutActions.Defaults)
        {
            shortcuts.Map.TryGetValue(action, out var s);
            _matcher.Register(action, KeyCombo.Parse(s ?? def));
        }
        foreach (var op in adv.OpenProfile)
            if (!string.IsNullOrWhiteSpace(op.Keys) && !string.IsNullOrWhiteSpace(op.File))
                _matcher.Register("open-profile\t" + op.File, KeyCombo.Parse(op.Keys));
        if (!string.IsNullOrWhiteSpace(adv.ToggleForcedTransparency))
            _matcher.Register("forced-transparency",
                KeyCombo.Parse(adv.ToggleForcedTransparency));
        foreach (var kv in adv.ServiceSwitch)
            _matcher.Register("service\t" + kv.Key, KeyCombo.Parse(kv.Value));
    }

    public bool InstallHook()
    {
        if (OperatingSystem.IsWindows())
        {
            _hook = new Platform.Windows.WinHook();
            _hook.KeyEvent += OnKey;
            return _hook.Install();
        }
        // Demais SOs: SharpHook/libuiohook (X11/macOS/Windows). No Wayland
        // não há API global — devolve false e vale o controle remoto (RF-569).
        _sharpHook = new SharpHookHotkey();
        _sharpHook.KeyEvent += OnKey;
        return _sharpHook.Install();
    }

    public bool HookInstalled => (_hook?.Installed ?? false) || (_sharpHook?.Installed ?? false);

    private void OnKey(int key, bool down)
    {
        if (key == Platform.Windows.WinHook.VK_SNAPSHOT && down)
            ScreenshotKey?.Invoke();                       // C11 (Etapa 12)
        _queue.Add((key, down));
    }

    private void Work()
    {
        foreach (var (key, down) in _queue.GetConsumingEnumerable())
        {
            if (!_running) return;
            try
            {
                if (HotkeyGuard.Suspended)                  // RF-443
                {
                    if (!down) _matcher.KeyUp(key);
                    continue;
                }
                string? action = down ? _matcher.KeyDown(key) : null;
                if (!down) _matcher.KeyUp(key);
                if (action is not null) ActionFired?.Invoke(action);
            }
            catch { }
        }
    }

    public void Dispose()
    {
        _running = false;
        _queue.CompleteAdding();
        _hook?.Dispose();                                  // RF-016
        _sharpHook?.Dispose();
    }
}
