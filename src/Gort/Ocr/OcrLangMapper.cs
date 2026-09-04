using System.Collections.Generic;

namespace Gort.Ocr;

/// <summary>
/// Mapeamento de idiomas na troca de motor/serviço (RF-147/149/151/315).
/// Regras por propriedade do idioma, nunca por identificador (RF-567).
/// </summary>
public static class OcrLangMapper
{
    /// <summary>
    /// RF-149: preserva o idioma ao trocar de motor quando possível.
    /// </summary>
    public static string Preserve(string oldOcrCode, IReadOnlyList<string> newLangs)
    {
        foreach (var l in newLangs)
            if (l == oldOcrCode) return l;
        foreach (var l in newLangs)
            if (l == "eng") return l;
        return newLangs.Count > 0 ? newLangs[0] : "eng";
    }

    /// <summary>RF-151: interseção motor ∩ {eng, jpn}.</summary>
    public static List<string> IntersectScope(IReadOnlyList<string> engineLangs)
    {
        var list = new List<string>();
        foreach (var l in engineLangs)
            if (l == "eng" || l == "jpn") list.Add(l);
        return list;
    }
}
