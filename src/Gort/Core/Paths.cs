using System;
using System.IO;

namespace Gort.Core;

/// <summary>
/// Caminhos de dados do usuário (RF-578: convenção de cada SO, formato idêntico).
/// RF-003: a pasta do executável é o diretório de trabalho corrente.
/// </summary>
public static class Paths
{
    public static string BaseDir { get; } = ResolveBase();
    public static string ProfilesDir => Path.Combine(BaseDir, "profiles");
    public static string ProfileFile => Path.Combine(BaseDir, "profile.toml");
    public static string AdvancedFile => Path.Combine(BaseDir, "advanced.toml");
    public static string AppFile => Path.Combine(BaseDir, "app.toml");
    public static string ShortcutsFile => Path.Combine(BaseDir, "shortcuts.toml");
    public static string CredFile(string serviceId)
    {
        // Higieniza: id com separador ou ".." vira "custom" (sem traversal).
        string safe = Path.GetFileName(serviceId);
        if (string.IsNullOrWhiteSpace(safe) || safe != serviceId) safe = "custom";
        return Path.Combine(BaseDir, $"creds-{safe}.toml");
    }
    public static string DictDir => Path.Combine(BaseDir, "dicts");
    public static string CollectDir => Path.Combine(BaseDir, "collect");
    public static string DebugDir => Path.Combine(BaseDir, "debug");

    /// <summary>Arquivo marcador que desativa a instância única (RF-002).</summary>
    public static string MultiInstanceMarker =>
        Path.Combine(BaseDir, "allow-multi-instance.marker");

    private static string ResolveBase()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GORT");
        if (OperatingSystem.IsMacOS())
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "GORT");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "gort");
    }

    public static void EnsureAll()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(ProfilesDir);
        Directory.CreateDirectory(DictDir);
        Directory.CreateDirectory(CollectDir);
        Directory.CreateDirectory(DebugDir);
    }
}
