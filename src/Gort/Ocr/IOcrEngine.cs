using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gort.Imaging;

namespace Gort.Ocr;

/// <summary>
/// Motor de OCR (contrato 6.4): recebe imagem + código de idioma, devolve
/// palavras com caixas em pixels da imagem recebida. Assíncrono para que o
/// laço espere em passos de 50 ms com verificação de parada (Etapa 8).
/// </summary>
public interface IOcrEngine
{
    /// <summary>Identificador textual estável (RF-027): modern|os|classic|venv|cloud.</summary>
    string Id { get; }
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    /// <summary>RF-351: sobreposição exige posição de palavra.</summary>
    bool ProvidesWordBoxes { get; }
    /// <summary>RF-122: nuvem só em modo pontual.</summary>
    bool PunctualOnly => false;
    /// <summary>Códigos de idioma OCR suportados, ex. eng/jpn (RF-151: interseção).</summary>
    IReadOnlyList<string> SupportedOcrLanguages();
    Task<OcrResult> RecognizeAsync(ProcessedImage img, string ocrLang,
        CancellationToken ct);
    /// <summary>RF-131: libera nova tentativa após reinício da tradução.</summary>
    void NotifyTranslationRestart();
}
