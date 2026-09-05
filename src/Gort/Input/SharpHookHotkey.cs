using System;
using SharpHook;
using SharpHook.Native;

namespace Gort.Input;

/// <summary>
/// Hook global de teclado multiplataforma via SharpHook/libuiohook
/// (Windows, macOS, Linux/X11). Entrega (tecla normalizada, pressionada?)
/// no mesmo domínio VK do WinHook — o matcher e a fila do HotkeyService
/// são reaproveitados sem mudança. Nunca consome eventos; falha de
/// instalação (Wayland, permissão negada) → false, com fallback para o
/// controle remoto (RF-569). Descarte no Dispose (RF-016).
/// </summary>
public sealed class SharpHookHotkey : IDisposable
{
    private TaskPoolGlobalHook? _hook;

    /// <summary>(tecla normalizada, pressionada?) — normalizada: L/R fundidos.</summary>
    public event Action<int, bool>? KeyEvent;
    public bool Installed => _hook?.IsRunning ?? false;

    public bool Install()
    {
        try
        {
            if (_hook is not null) return _hook.IsRunning;
            var hook = new TaskPoolGlobalHook();
            hook.KeyPressed += (_, e) => Forward(e, true);
            hook.KeyReleased += (_, e) => Forward(e, false);
            // Roda o loop nativo em background; confirma que subiu (falha
            // assíncrona — Wayland/sem permissão — virava "ok" e o aviso de
            // fallback para o remoto (RF-569) nunca aparecia).
            _ = hook.RunAsync();
            for (int i = 0; i < 50 && !hook.IsRunning; i++)
                System.Threading.Thread.Sleep(20);
            if (!hook.IsRunning) { try { hook.Dispose(); } catch { } return false; }
            _hook = hook;
            return true;
        }
        catch { return false; }
    }

    private void Forward(KeyboardHookEventArgs e, bool down)
    {
        try
        {
            int vk = SharpHookKeys.Map(e.Data.KeyCode.ToString());
            if (vk != 0) KeyEvent?.Invoke(vk, down);
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            var h = _hook;
            _hook = null;
            h?.Dispose();
        }
        catch { }
    }
}
