namespace Gort.Config;

/// <summary>Opções do aplicativo (RF-034): UI, update, aba inicial, sempre-no-topo.</summary>
public sealed class AppOptions
{
    public const int SchemaVersion = 1;
    public string UiLanguage { get; set; } = "pt-BR";  // RF-487
    public bool CheckUpdate { get; set; } = true;
    public bool BasicTabDefault { get; set; } = false; // true = recomeça no assistente (RF-501)
    public bool TranslationAlwaysOnTop { get; set; } = true;
}

/// <summary>Registro de credencial (RF-036): identificador, segredo, plano. Texto puro (RF-035).</summary>
public sealed class CredentialRecord
{
    public string Id { get; set; } = "";
    public string Secret { get; set; } = "";
    public string Plan { get; set; } = "free";          // free|paid
}

/// <summary>Ação de atalho (RF-444) + avançados (RF-447).</summary>
public static class ShortcutActions
{
    public const string ToggleLoop = "toggle-loop";           // Ctrl+Shift+Z
    public const string Once = "translate-once";              // Ctrl+Shift+C
    public const string Snapshot = "snapshot";                // Ctrl+Shift+A
    public const string Quick = "quick-area";                 // Ctrl+Shift+X
    public const string DictEditor = "dict-editor";           // Ctrl+Shift+S
    public const string HideWindow = "hide-window";           // Ctrl+Shift+D
    public const string FollowMouse = "follow-mouse";         // Ctrl+Shift+F

    public static readonly (string Action, string Default)[] Defaults =
    [
        (ToggleLoop, "Ctrl+Shift+Z"),
        (Once, "Ctrl+Shift+C"),
        (Snapshot, "Ctrl+Shift+A"),
        (Quick, "Ctrl+Shift+X"),
        (DictEditor, "Ctrl+Shift+S"),
        (HideWindow, "Ctrl+Shift+D"),
        (FollowMouse, "Ctrl+Shift+F"),
    ];
}

public sealed class ShortcutFile
{
    public const int SchemaVersion = 1;
    public System.Collections.Generic.Dictionary<string, string> Map { get; set; } = new();

    public static ShortcutFile Defaults()
    {
        var s = new ShortcutFile();
        foreach (var (a, d) in ShortcutActions.Defaults) s.Map[a] = d;
        return s;
    }
}
