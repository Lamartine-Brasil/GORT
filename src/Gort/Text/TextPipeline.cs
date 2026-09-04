using System.Collections.Generic;

namespace Gort.Text;

/// <summary>
/// Tratamento textual e montagem de exibição (15.3): remoção de espaços
/// (RF-180), dicionário (RF-181), junção de quebras (RF-186/187), carga
/// da sobreposição (RF-188), numeração (RF-189), filtros (RF-190/191).
/// </summary>
public static class TextPipeline
{
    /// <summary>
    /// Marcador de "sem resultado" do banco local (RF-190/241): traduções
    /// iguais a ele não são concatenadas. O adaptador do banco (Etapa 15)
    /// usa esta mesma constante.
    /// </summary>
    public const string NoResultMarker = "@@NORESULT@@";

    /// <summary>
    /// Texto que vai ao tradutor (RF-180/181/186/187): remove espaços,
    /// aplica dicionário; fora da sobreposição remove quebras (espaço, ou
    /// nada com remoção ativa — RF-186 🔒).
    /// </summary>
    public static string ForTranslation(Block block, bool removeSpaces,
        DictionaryStore? dict, bool dictActive, bool byWord, int extraPasses,
        bool overlayMode, bool oneLinePerBlock, bool isDbService)
    {
        string t = block.RawText;
        if (removeSpaces)                                            // RF-180
            t = t.Replace(" ", "").Replace("\t", "");
        if (dictActive && dict is not null)                          // RF-181/182
            t = dict.Apply(t, byWord, extraPasses);
        if (!overlayMode && !oneLinePerBlock && !isDbService)        // RF-186 🔒
            t = t.Replace("\n", removeSpaces ? "" : " ");
        return t;
    }

    /// <summary>
    /// Carga da sobreposição (RF-188): por bloco, quebra + token + texto.
    /// Uma única requisição carrega todos os blocos (protocolo 18.1).
    /// </summary>
    public static string OverlayPayload(IReadOnlyList<Block> blocks, string token,
        bool removeSpaces, DictionaryStore? dict, bool dictActive, bool byWord,
        int extraPasses)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var b in blocks)
        {
            sb.Append('\n');
            sb.Append(token);
            sb.Append(ForTranslation(b, removeSpaces, dict, dictActive, byWord,
                extraPasses, overlayMode: true, oneLinePerBlock: false, isDbService: false));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Texto exibido nos modos escuro/camada (RF-189/190/191): prefixo por
    /// área quando há mais de uma; pula traduções-marcador e blocos vazios.
    /// </summary>
    public static string FormatDisplay(
        IReadOnlyList<(int AreaIndex, List<(string Ocr, string Translated)> Items)> regions,
        bool numbering)
    {
        var sb = new System.Text.StringBuilder();
        bool multi = regions.Count > 1;
        foreach (var (idx, items) in regions)
        {
            foreach (var (ocr, tr) in items)
            {
                if (string.IsNullOrEmpty(ocr)) continue;             // RF-191
                if (tr == NoResultMarker) continue;                  // RF-190
                if (multi) sb.Append(numbering ? $"{idx + 1} : " : "- ");  // RF-189
                sb.Append(tr);
                sb.Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n');
    }
}
