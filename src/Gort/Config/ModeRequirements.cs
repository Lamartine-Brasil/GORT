using System;

namespace Gort.Config;

/// <summary>
/// Pré-requisitos por modo de exibição: ao escolher Escuro, Camada,
/// Sobreposição ou Substituição e aplicar, o estritamente necessário para
/// o modo funcionar é ajustado sozinho — o utilizador não precisa caçar
/// configurações. Escuro e Camada funcionam com qualquer motor;
/// Sobreposição e Substituição exigem motor com posição de palavra
/// utilizável em tradução contínua (o mesmo que a partida verifica).
/// Só o motor é tocado, e só quando incompatível; todo o resto (cores,
/// fontes, fundo, idiomas, áreas) permanece como o utilizador deixou.
/// </summary>
public static class ModeRequirements
{
    /// <summary>
    /// Garante no perfil o pré-requisito do modo vigente. Devolve o aviso
    /// para a interface, ou nulo quando nada precisou mudar.
    /// </summary>
    /// <param name="profile">Perfil a ajustar.</param>
    /// <param name="caps">Capacidades por id de motor; nulo = desconhecido.</param>
    public static string? EnsureForMode(Profile profile,
        Func<string, (bool Available, bool WordBoxes, bool PunctualOnly)?> caps)
    {
        if (profile.WindowMode != "overlay" && profile.WindowMode != "replace")
            return null;   // escuro/camada funcionam com qualquer motor
        if (caps(profile.OcrEngine) is { } cur
            && cur.Available && cur.WordBoxes && !cur.PunctualOnly)
            return null;   // já pronto: não toca em nada pessoal
        // Mesma ordem do assistente inicial: moderno, sistema, clássico.
        foreach (string id in new[] { "modern", "os", "classic" })
        {
            if (caps(id) is { } c && c.Available && c.WordBoxes && !c.PunctualOnly)
            {
                profile.OcrEngine = id;
                string mode = profile.WindowMode == "replace" ? "Substituição" : "Sobreposição";
                return $"A {mode} exige motor com posição de palavra: ajustado para {DisplayOf(id)}.";
            }
        }
        return null;   // sem motor capaz: a partida avisa como antes
    }

    private static string DisplayOf(string id)
    {
        foreach (var c in Catalogs.OcrEngines)
            if (string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                return c.DisplayPtBr;
        return id;
    }
}
