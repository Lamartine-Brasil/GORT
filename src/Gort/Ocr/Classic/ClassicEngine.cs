using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Imaging;

namespace Gort.Ocr.Classic;

/// <summary>
/// Motor local clássico (RF-121/Tesseract): sem rede, por palavra. Requer
/// arquivos de dados de idioma; variantes rápidas para eng/jpn (RF-150).
/// tessdata baixado sob demanda para BaseDir/ocr/classic[/fast].
/// </summary>
public sealed class ClassicEngine : IOcrEngine, IDisposable
{
    public string Id => "classic";
    public bool ProvidesWordBoxes => true;
    public bool PunctualOnly => false;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    public static string DataDir =>
        Path.Combine(Paths.BaseDir, "ocr", "classic");

    private readonly Func<string> _dataset;
    private readonly Func<bool> _fast;
    private Tesseract.TesseractEngine? _engine;
    private string? _engineKey;
    private string? _unavailable;
    private bool _nativeMissing;   // biblioteca nativa ausente neste SO

    public ClassicEngine(Func<string> dataset, Func<bool> fast)
    {
        _dataset = dataset; _fast = fast;
    }

    public bool IsAvailable => !_nativeMissing && ResolveDataDir() is not null;

    private string NativeMissingMessage =>
        "Motor clássico indisponível neste sistema (biblioteca nativa " +
        "do Tesseract ausente). Use o motor moderno embarcado.";

    public string? UnavailableReason => _unavailable ?? (IsAvailable
        ? null : "Dados de idioma do motor clássico ausentes (serão baixados no primeiro uso).");

    /// <summary>
    /// RF-150: nome do conjunto + sufixo rápido para eng/jpn.
    /// RF-151: interseção com {eng, jpn} presentes no disco.
    /// </summary>
    public IReadOnlyList<string> SupportedOcrLanguages()
    {
        var list = new List<string>();
        string dir = DataDir;
        if (File.Exists(Path.Combine(dir, "eng.traineddata"))
            || File.Exists(Path.Combine(dir, "fast", "eng.traineddata"))) list.Add("eng");
        if (File.Exists(Path.Combine(dir, "jpn.traineddata"))
            || File.Exists(Path.Combine(dir, "fast", "jpn.traineddata"))) list.Add("jpn");
        return list;
    }

    public void NotifyTranslationRestart() { }

    public void Dispose()   // RF-016
    {
        try { _engine?.Dispose(); } catch { }
        _engine = null;
        _engineKey = null;
    }

    public async Task<OcrResult> RecognizeAsync(ProcessedImage img, string ocrLang,
        CancellationToken ct)
    {
        try
        {
            var engine = await EnsureEngineAsync(ocrLang, ct).ConfigureAwait(false);
            return await Task.Run(() => Recognize(engine, img), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return OcrResult.Fail(ex.Message); }   // RF-145
    }

    private static OcrResult Recognize(Tesseract.TesseractEngine engine, ProcessedImage img)
    {
        // RF-117: entrega BGR de 3 canais (replica o cinza, descarta o alfa).
        byte[] bgr = Preprocess.ConvertChannels(img.Bytes, img.Width, img.Height,
            img.Channels, 3);
        byte[] bmp = Bmp.Encode(bgr, img.Width, img.Height, 24);
        using var pix = Tesseract.Pix.LoadFromMemory(bmp);
        using var page = engine.Process(pix);
        var words = new List<OcrWord>();
        var perLine = new List<int>();
        using var iter = page.GetIterator();
        iter.Begin();
        do
        {
            if (iter.TryGetBoundingBox(
                    Tesseract.PageIteratorLevel.Word, out var rect))
            {
                string text = iter.GetText(Tesseract.PageIteratorLevel.Word) ?? "";
                if (!string.IsNullOrWhiteSpace(text))
                {
                    words.Add(new OcrWord
                    {
                        Text = text.Trim(),
                        X = rect.X1, Y = rect.Y1,
                        W = Math.Max(0, rect.X2 - rect.X1),
                        H = Math.Max(0, rect.Y2 - rect.Y1),
                    });
                }
            }
        } while (iter.Next(Tesseract.PageIteratorLevel.Word));
        // Deriva linhas por agrupamento vertical das palavras (Y).
        var ordered = new List<OcrWord>(words);
        ordered.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));
        var lineList = new List<List<OcrWord>>();
        foreach (var w in ordered)
        {
            bool placed = false;
            foreach (var ln in lineList)
            {
                int cy = ln[0].Y + ln[0].H / 2;
                if (Math.Abs((w.Y + w.H / 2) - cy) <= Math.Max(w.H, ln[0].H))
                { ln.Add(w); placed = true; break; }
            }
            if (!placed) lineList.Add(new List<OcrWord> { w });
        }
        var finalWords = new List<OcrWord>();
        foreach (var ln in lineList)
        {
            ln.Sort((a, b) => a.X.CompareTo(b.X));
            perLine.Add(ln.Count);
            finalWords.AddRange(ln);
        }
        return new OcrResult
        {
            LineCount = lineList.Count, Words = finalWords, WordsPerLine = perLine,
        };
    }

    private async Task<Tesseract.TesseractEngine> EnsureEngineAsync(string lang,
        CancellationToken ct)
    {
        string dataset = _dataset();
        bool fast = _fast() && (dataset == "eng" || dataset == "jpn");  // RF-150
        string key = dataset + (fast ? "+fast" : "");
        if (_engine is not null && _engineKey == key) return _engine;
        string dir = await EnsureDataAsync(dataset, fast, ct).ConfigureAwait(false);
        _engine?.Dispose();
        // RF-143: UTF-8 com alternativa — o wrapper gerencia; força UTF-8.
        try
        {
            _engine = new Tesseract.TesseractEngine(dir, dataset,
                Tesseract.EngineMode.Default);
        }
        catch (Exception ex) when (ex is DllNotFoundException
            || ex is BadImageFormatException || ex is TypeInitializationException)
        {
            // Nativo ausente neste SO (pacote só traz Windows): marca
            // permanente em vez de falhar a cada ciclo.
            _nativeMissing = true;
            _unavailable = NativeMissingMessage;
            throw new InvalidOperationException(_unavailable, ex);
        }
        _engineKey = key;
        _unavailable = null;
        return _engine;
    }

    private string? ResolveDataDir()
    {
        string dataset = _dataset();
        bool fast = _fast() && (dataset == "eng" || dataset == "jpn");
        string dir = fast ? Path.Combine(DataDir, "fast") : DataDir;
        return File.Exists(Path.Combine(dir, dataset + ".traineddata")) ? dir : null;
    }

    internal static string TessUrl(string dataset, bool fast) =>
        fast
            ? $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{dataset}.traineddata"
            : $"https://github.com/tesseract-ocr/tessdata/raw/main/{dataset}.traineddata";

    private async Task<string> EnsureDataAsync(string dataset, bool fast,
        CancellationToken ct)
    {
        string? dir = ResolveDataDir();
        if (dir is not null) return dir;
        dir = fast ? Path.Combine(DataDir, "fast") : DataDir;
        Directory.CreateDirectory(dir);
        string dest = Path.Combine(dir, dataset + ".traineddata");
        try
        {
            using var resp = await Http.GetAsync(TessUrl(dataset, fast), ct)
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(dest);
            await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
        }
        catch
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            _unavailable = $"Não foi possível baixar os dados de idioma '{dataset}'. " +
                "Verifique a rede e tente de novo.";
            throw new InvalidOperationException(_unavailable);
        }
        return dir;
    }
}
