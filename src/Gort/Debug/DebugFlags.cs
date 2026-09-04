namespace Gort.Debug;

/// <summary>Modo de depuração (cap. 27, RF-490/491): ativado por controle
/// escondido (três cliques na versão). Painel completo na Etapa 18.</summary>
public static class DebugFlags
{
    public static bool Enabled { get; private set; }

    public static void Enable()
    {
        Enabled = true;
    }

    public static bool UnlockSpeed { get; set; }
    public static bool ShowCache { get; set; }
    public static bool OneLinePerBlock { get; set; }   // RF-157/491
    public static bool ShowWordAreas { get; set; }
    public static bool SaveAnalysis { get; set; }      // RF-491: retrato
    public static bool NativeShowReplace { get; set; }
    public static bool NativeSaveShot { get; set; }
}
