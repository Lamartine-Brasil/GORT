using System;
using System.Collections.Generic;

namespace Gort.Input;

/// <summary>
/// Combinação de atalho (7.7/22): até 3 teclas (RF-442), modificadores
/// normalizados (RF-437), vazia = válida e nunca dispara (RF-446).
/// Formato persistido: "Ctrl+Shift+Z".
/// </summary>
public sealed class KeyCombo
{
    public bool Ctrl, Shift, Alt, Win;
    public int Key;              // código virtual (0 = vazio)
    public bool IsEmpty => Key == 0 && !Ctrl && !Shift && !Alt && !Win;

    public HashSet<int> ToSet()
    {
        var s = new HashSet<int>();
        if (Ctrl) s.Add(VK.CONTROL);
        if (Shift) s.Add(VK.SHIFT);
        if (Alt) s.Add(VK.MENU);
        if (Win) s.Add(VK.LWIN);
        if (Key != 0) s.Add(Key);
        return s;
    }

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        if (Win) parts.Add("Win");
        if (Key != 0) parts.Add(VK.Name(Key));
        return string.Join("+", parts);
    }

    public static KeyCombo Parse(string s)
    {
        var c = new KeyCombo();
        if (string.IsNullOrWhiteSpace(s)) return c;
        int parts = 0;   // RF-442: no máximo três teclas
        foreach (var raw in s.Split('+'))
        {
            string p = raw.Trim();
            if (p.Length == 0) continue;
            if (parts >= 3) break;
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) { c.Ctrl = true; parts++; }
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { c.Shift = true; parts++; }
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) { c.Alt = true; parts++; }
            else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase)) { c.Win = true; parts++; }
            else if (c.Key == 0 && VK.TryParse(p, out int vk)) { c.Key = vk; parts++; }
        }
        return c;
    }

    /// <summary>Códigos virtuais Win32 relevantes.</summary>
    public static class VK
    {
        public const int SHIFT = 0x10, CONTROL = 0x11, MENU = 0x12;
        public const int LWIN = 0x5B, RWIN = 0x5C;
        public const int LSHIFT = 0xA0, RSHIFT = 0xA1;
        public const int LCONTROL = 0xA2, RCONTROL = 0xA3;
        public const int LMENU = 0xA4, RMENU = 0xA5;
        public const int ESCAPE = 0x1B, BACK = 0x08, SPACE = 0x20;

        public static bool TryParse(string name, out int vk)
        {
            vk = 0;
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c >= 'A' && c <= 'Z') { vk = c; return true; }
                if (c >= '0' && c <= '9') { vk = c; return true; }
            }
            if (name.Length is 2 or 3 && (name[0] == 'F' || name[0] == 'f')
                && int.TryParse(name[1..], out int f) && f >= 1 && f <= 24)
            { vk = 0x6F + f; return true; }
            switch (name.ToUpperInvariant())
            {
                case "ESC": case "ESCAPE": vk = ESCAPE; return true;
                case "BACKSPACE": case "BACK": vk = BACK; return true;
                case "SPACE": vk = SPACE; return true;
                case "LEFT": vk = 0x25; return true;
                case "UP": vk = 0x26; return true;
                case "RIGHT": vk = 0x27; return true;
                case "DOWN": vk = 0x28; return true;
                case "INSERT": vk = 0x2D; return true;
                case "DELETE": vk = 0x2E; return true;
                case "HOME": vk = 0x24; return true;
                case "END": vk = 0x23; return true;
                case "PAGEUP": vk = 0x21; return true;
                case "PAGEDOWN": vk = 0x22; return true;
                case "TAB": vk = 0x09; return true;
                case "ENTER": vk = 0x0D; return true;
            }
            return false;
        }

        public static string Name(int vk) => vk switch
        {
            ESCAPE => "Esc",
            BACK => "Back",
            SPACE => "Space",
            _ when vk >= 'A' && vk <= 'Z' => ((char)vk).ToString(),
            _ when vk >= '0' && vk <= '9' => ((char)vk).ToString(),
            _ when vk >= 0x70 && vk <= 0x87 => "F" + (vk - 0x6F),
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            _ => "VK" + vk,
        };
    }
}

/// <summary>
/// Reconhecimento de combinações (RF-438..442): conjunto pressionado contra
/// combinações configuradas, independente de ordem; duplicada resolve pela
/// ordem estável de verificação, silenciosamente (RF-439); repetição
/// ignorada até soltar (RF-440, semântica de conjunto); soltar qualquer
/// tecla limpa tudo (RF-441).
/// </summary>
public sealed class HotkeyMatcher
{
    private readonly List<(string Action, HashSet<int> Keys)> _combos = new();
    private readonly HashSet<int> _pressed = new();

    /// <summary>Ordem estável de verificação = ordem de registro (RF-439).</summary>
    public void Register(string action, KeyCombo combo)
    {
        if (combo.IsEmpty) return;                    // RF-446: nunca dispara
        _combos.Add((action, combo.ToSet()));
    }

    public void Clear() { _combos.Clear(); _pressed.Clear(); }

    /// <returns>Ação disparada, ou nulo.</returns>
    public string? KeyDown(int normalized)
    {
        if (!_pressed.Add(normalized)) return null;   // RF-440: repetição ignora
        foreach (var (action, keys) in _combos)       // RF-439: primeira vence
            if (keys.SetEquals(_pressed)) return action;   // RF-438: exato
        return null;
    }

    public void KeyUp(int normalized)
    {
        _pressed.Clear();                             // RF-441
    }

    public void Reset() => _pressed.Clear();
}
