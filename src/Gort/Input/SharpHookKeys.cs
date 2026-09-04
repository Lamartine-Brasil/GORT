using System;

namespace Gort.Input;

/// <summary>
/// Mapeamento SharpHook → códigos VK do matcher (KeyCombo.VK).
/// Por NOME do KeyCode ("VcA", "VcLeftShift", …), não por valor numérico —
/// os valores internos do libuiohook variam por plataforma, os nomes não.
/// Modificadores L/R são normalizados para o código único (RF-437).
/// Função pura e testável sem instalar hook.
/// </summary>
public static class SharpHookKeys
{
    /// <summary>PrintScreen no domínio VK (C11).</summary>
    public const int VK_SNAPSHOT = 0x2C;

    /// <returns>Código VK, ou 0 se a tecla não interessa.</returns>
    public static int Map(string keyCodeName)
    {
        if (keyCodeName.Length == 0) return 0;
        string s = keyCodeName.StartsWith("Vc", StringComparison.Ordinal)
            ? keyCodeName[2..] : keyCodeName;
        switch (s)
        {
            case "LeftShift": case "RightShift": case "Shift":
                return KeyCombo.VK.SHIFT;
            case "LeftControl": case "RightControl": case "Control":
                return KeyCombo.VK.CONTROL;
            case "LeftAlt": case "RightAlt": case "Alt":
                return KeyCombo.VK.MENU;
            case "LeftMeta": case "RightMeta": case "Meta":
                return KeyCombo.VK.LWIN;
            case "PrintScreen": case "Snapshot":
                return VK_SNAPSHOT;
            case "Escape": return KeyCombo.VK.ESCAPE;
            case "Backspace": case "Back": return KeyCombo.VK.BACK;
            case "Space": return KeyCombo.VK.SPACE;
            case "Tab": return 0x09;
            case "Enter": case "Return": return 0x0D;
            case "Left": return 0x25;
            case "Up": return 0x26;
            case "Right": return 0x27;
            case "Down": return 0x28;
            case "Insert": return 0x2D;
            case "Delete": return 0x2E;
            case "Home": return 0x24;
            case "End": return 0x23;
            case "PageUp": return 0x21;
            case "PageDown": return 0x22;
            default:
                // Letras, dígitos e F1–F24 caem no parser VK ("A", "5", "F12").
                if (KeyCombo.VK.TryParse(s, out int vk)) return vk;
                return 0;
        }
    }
}
