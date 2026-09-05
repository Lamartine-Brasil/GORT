using System;
using System.Runtime.InteropServices;

namespace Gort.Platform.Windows;

/// <summary>
/// Interceptador global de teclado (C10, RF-436): WH_KEYBOARD_LL, funciona sem
/// foco (RF-436), sem consumir eventos. Normaliza esquerda/direita (RF-437).
/// O callback apenas enfileira; o consumo nunca bloqueia o hook (RF-011).
/// </summary>
public sealed class WinHook : IDisposable
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    [StructLayout(LayoutKind.Sequential)]
    private struct Kbd
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint extra;
    }

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    [DllImport("user32.dll")] private static extern nint SetWindowsHookEx(
        int type, HookProc proc, nint mod, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(
        nint h, int code, nint w, nint l);
    [DllImport("kernel32.dll")] private static extern nint GetModuleHandle(string? name);

    private readonly HookProc _proc;
    private nint _handle;
    private readonly object _gate = new();

    /// <summary>(tecla normalizada, pressionada?) — normalizada: L/R fundidos.</summary>
    public event Action<int, bool>? KeyEvent;
    public bool Installed => _handle != nint.Zero;

    public WinHook() => _proc = Callback;

    public bool Install()
    {
        lock (_gate)
        {
            if (_handle != nint.Zero) return true;
            _handle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            return _handle != nint.Zero;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle != nint.Zero) { UnhookWindowsHookEx(_handle); _handle = nint.Zero; }  // RF-016
        }
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                var k = Marshal.PtrToStructure<Kbd>(lParam);
                int w = (int)wParam;
                bool down = w == WM_KEYDOWN || w == WM_SYSKEYDOWN;
                bool up = w == WM_KEYUP || w == WM_SYSKEYUP;
                if (down || up) KeyEvent?.Invoke(Normalize((int)k.vkCode), down);
            }
            catch { }
        }
        return CallNextHookEx(_handle, code, wParam, lParam);   // nunca consome
    }

    /// <summary>
    /// RF-437: variantes L/R → código único (o LL já entrega fundido).
    /// </summary>
    internal static int Normalize(int vk)
    {
        return vk switch
        {
            0x10 => Input.KeyCombo.VK.SHIFT,                        // Shift L/R
            0x11 => Input.KeyCombo.VK.CONTROL,                      // Ctrl L/R
            0x12 => Input.KeyCombo.VK.MENU,                         // Alt L/R
            0x5B or 0x5C => Input.KeyCombo.VK.LWIN,                 // Win L/R
            _ => vk,
        };
    }

    /// <summary>Códigos de captura de tela do SO (C11): PrintScreen.</summary>
    public const int VK_SNAPSHOT = 0x2C;
}
