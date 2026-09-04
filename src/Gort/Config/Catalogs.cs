using System.Collections.Generic;

namespace Gort.Config;

/// <summary>
/// Catálogos de conjuntos fechados como DADOS (RF-029, RF-566, RF-567).
/// Persistidos por identificador textual (RF-026), estáveis, minúsculos (RF-027).
/// </summary>
public sealed class CatalogItem
{
    public required string Id { get; init; }
    public required string DisplayPtBr { get; init; }
}

public static class Catalogs
{
    // Ordem é apresentação; nunca índice persistido (RF-026).
    public static readonly List<CatalogItem> OcrEngines =
    [
        new() { Id = "modern", DisplayPtBr = "Motor de reconhecimento moderno embarcado" },
        new() { Id = "os", DisplayPtBr = "Motor do sistema operacional" },
        new() { Id = "classic", DisplayPtBr = "Motor local clássico" },
        new() { Id = "venv", DisplayPtBr = "Motor baseado em ambiente interpretado" },
        new() { Id = "cloud", DisplayPtBr = "Motor de nuvem (somente modo pontual)" },
    ];

    public static readonly List<CatalogItem> TranslationServices =
    [
        new() { Id = "web-free", DisplayPtBr = "Google Tradutor (web gratuito)" },
        new() { Id = "db", DisplayPtBr = "Banco de dados local" },
        new() { Id = "web-nokey", DisplayPtBr = "Tradutor web sem chave" },
        new() { Id = "commercial-kr", DisplayPtBr = "Tradutor comercial por chave (KR)" },
        new() { Id = "sheets", DisplayPtBr = "Tradutor por planilha em nuvem" },
        new() { Id = "embedded-browser", DisplayPtBr = "Tradutor por navegador embutido" },
        new() { Id = "commercial-eu", DisplayPtBr = "Tradutor comercial por chave (EU)" },
        new() { Id = "llm", DisplayPtBr = "Gemini (modelo de linguagem)" },
        new() { Id = "local-worker", DisplayPtBr = "Tradutor local por processo auxiliar" },
        new() { Id = "custom", DisplayPtBr = "API personalizada" },
    ];

    public static readonly List<CatalogItem> WindowModes =
    [
        new() { Id = "overlay", DisplayPtBr = "Sobreposição" },
        new() { Id = "layer", DisplayPtBr = "Camada" },
        new() { Id = "dark", DisplayPtBr = "Escuro" },
        // Fase 2 do roadmap: substitui o original sob a tradução (modo novo,
        // não troca o padrão camada).
        new() { Id = "replace", DisplayPtBr = "Substituição" },
    ];

    public static bool KnownId(List<CatalogItem> catalog, string id)
    {
        foreach (var c in catalog) if (c.Id == id) return true;
        return false;
    }

    // URLs e textos externos são dados, não literais (RF-544, aba Outros).
    // Dono: Lamartine Barbosa — tudo aponta para o GitHub dele por enquanto
    // (inclusive doações; ele ajusta depois).
    public static class Links
    {
        private const string GitHub = "https://github.com/Lamartine-Brasil/GORT";
        public const string Donate = GitHub;
        public const string Repo = GitHub;
        public const string ProjectPage = GitHub;
        public const string Community = GitHub;
        public const string Manual = GitHub;
        public const string KnownErrors = GitHub;
    }
}
