using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gort.Translate;

/// <summary>Resultado de um serviço: traduções na mesma ordem, ou erro, ou cancelado.</summary>
public sealed class ServiceResult
{
    public List<string>? Translations { get; init; }
    public string? Error { get; init; }
    public bool Cancelled { get; init; }
}

/// <summary>Resultado do lote (18.1): por bloco + bruto concatenado (RF-237).</summary>
public sealed class BatchResult
{
    public List<string?> PerText { get; init; } = new();
    /// <summary>Quais vieram de cache (RF-491: marcador ◈).</summary>
    public List<bool> FromCache { get; init; } = new();
    public string Raw { get; init; } = "";
    public string? Error { get; init; }
    public bool Cancelled { get; init; }
}

/// <summary>
/// Serviço de tradução (6.7): lista de textos → lista de traduções, mesma
/// ordem e tamanho, ou mensagem de erro única. Sem inferência local (RF-227).
/// </summary>
public interface ITranslationService
{
    string Id { get; }
    string Display { get; }
    /// <summary>Token separador padrão do serviço (RF-232).</summary>
    string DefaultToken { get; }
    bool SupportsBridge { get; }
    /// <summary>RF-214: banco local e worker não usam memória de resultados.</summary>
    bool UsesResultMemory { get; }
    /// <summary>RF-221: coletânea não é usada quando o serviço é o próprio DB.</summary>
    bool UsesCollectanea { get; }
    Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct);
}

/// <summary>
/// Memória de resultados + coletânea (cap. 17, Etapa 10). Aqui o contrato e
/// o nulo; a implementação em arquivo chega na Etapa 10.
/// </summary>
public interface ITranslationMemory
{
    string? TryGet(string serviceId, string source);
    void Store(string serviceId, string source, string translated);
}

public sealed class NullMemory : ITranslationMemory
{
    public string? TryGet(string serviceId, string source) => null;
    public void Store(string serviceId, string source, string translated) { }
}
