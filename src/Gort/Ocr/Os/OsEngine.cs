using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gort.Imaging;

namespace Gort.Ocr.Os;

/// <summary>
/// Motor do sistema operacional (RF-121): depende dos pacotes de idioma do
/// SO e da projeção WinRT, indisponível em net9.0 puro (RF-575: nunca
/// apresenta um motor que falhará). Reserva de API para a projeção futura.
/// </summary>
public sealed class OsEngine : IOcrEngine
{
    public string Id => "os";
    public bool ProvidesWordBoxes => true;
    public bool PunctualOnly => false;

    public bool IsAvailable => false;

    public string? UnavailableReason =>
        "Reconhecimento do sistema indisponível nesta compilação " +
        "(requer projeção WinRT).";

    public IReadOnlyList<string> SupportedOcrLanguages() => new List<string>();

    public Task<OcrResult> RecognizeAsync(ProcessedImage img, string ocrLang,
        CancellationToken ct) =>
        Task.FromResult(OcrResult.Fail(UnavailableReason ?? "Indisponível."));
}
