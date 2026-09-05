using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Imaging;
using SkiaSharp;

namespace Gort.Ocr.Venv;

/// <summary>
/// Motor por ambiente interpretado (RF-121): verifica ambiente+pacote antes
/// de traduzir (RF-132), instala sob demanda. Devolve linhas (RF-141 adapta).
/// Sem GPU por padrão; CUDA via instalador (RF-133).
/// </summary>
public sealed class VenvEngine : IOcrEngine, IDisposable
{
    public string Id => "venv";
    public bool ProvidesWordBoxes => false;   // por linha (RF-141)
    public bool PunctualOnly => false;

    public static string EnvDir => Path.Combine(Paths.BaseDir, "venv");
    public static string ScriptPath => Path.Combine(EnvDir, "gort_ocr.py");
    public static string MarkerPath => Path.Combine(EnvDir, "ocr-ready.marker");
    /// <summary>RF-135: ambiente carregado nesta sessão bloqueia reinstalação.</summary>
    public static bool LoadedThisSession { get; private set; }

    private static string Python()
    {
        string win = Path.Combine(EnvDir, "Scripts", "python.exe");
        if (File.Exists(win)) return win;
        return Path.Combine(EnvDir, "bin", "python");
    }

    public bool IsAvailable =>
        File.Exists(Python()) && File.Exists(MarkerPath);

    public string? UnavailableReason => !File.Exists(Python())
        ? "Ambiente interpretado não instalado. Abra o instalador."
        : "Pacote de OCR não instalado no ambiente. Abra o instalador.";

    public IReadOnlyList<string> SupportedOcrLanguages() =>
        IsAvailable ? new List<string> { "eng", "jpn" } : new List<string>();

    public void Dispose()   // RF-016: encerra o ambiente
    {
        try { _proc?.Kill(); } catch { }
        try { _proc?.WaitForExit(2000); } catch { }
        try { _proc?.Dispose(); } catch { }
        _proc = null;
        try { _gate.Dispose(); } catch { }
    }

    private Process? _proc;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _failed;

    public async Task<OcrResult> RecognizeAsync(ProcessedImage img, string ocrLang,
        CancellationToken ct)
    {
        if (!IsAvailable)
            return OcrResult.Fail(UnavailableReason ?? "Motor indisponível.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await EnsureProcAsync(ct).ConfigureAwait(false);
            string b64 = ToPngBase64(img);
            string line = await RoundTripAsync(ocrLang + " " + b64, ct).ConfigureAwait(false);
            return ParseLine(line);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _failed = true;
            try { _proc?.Kill(); } catch { }
            try { _proc?.WaitForExit(2000); } catch { }
            try { _proc?.Dispose(); } catch { }
            _proc = null;
            return OcrResult.Fail(ex.Message);   // RF-145
        }
        finally { _gate.Release(); }
    }

    private static string ToPngBase64(ProcessedImage img)
    {
        byte[] bgra = img.Channels == 4 ? img.Bytes
            : Preprocess.ConvertChannels(img.Bytes, img.Width, img.Height, img.Channels, 4);
        using var bmp = new SKBitmap(img.Width, img.Height,
            SKColorType.Bgra8888, SKAlphaType.Opaque);
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) throw new InvalidOperationException("Falha ao codificar imagem.");
        return Convert.ToBase64String(data.ToArray());
    }

    private async Task EnsureProcAsync(CancellationToken ct)
    {
        if (_proc is not null && !_proc.HasExited && !_failed) return;
        _failed = false;
        EnsureScript();
        _proc = new Process
        {
            StartInfo = new ProcessStartInfo(Python(), "-u \"" + ScriptPath + "\"")
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        if (!_proc.Start())
            throw new InvalidOperationException("Não foi possível iniciar o ambiente.");
        LoadedThisSession = true;
        // Handshake: primeira linha deve ser {"ready":true}.
        string? hello = await _proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
        if (hello is null || !hello.Contains("\"ready\""))
            throw new InvalidOperationException("O ambiente não respondeu.");
    }

    private async Task<string> RoundTripAsync(string b64, CancellationToken ct)
    {
        await _proc!.StandardInput.WriteLineAsync(b64).ConfigureAwait(false);
        await _proc.StandardInput.FlushAsync(ct).ConfigureAwait(false);
        var lines = new List<string>();
        while (true)
        {
            string? line = await _proc.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) throw new InvalidOperationException("Ambiente encerrou.");
            if (line.Contains("\"end\"")) break;
            lines.Add(line);
        }
        return "[" + string.Join(",", lines) + "]";
    }

    /// <summary>Cada linha JSON vira uma "palavra" com a caixa da linha (RF-141).</summary>
    internal static OcrResult ParseLine(string jsonArray)
    {
        var words = new List<OcrWord>();
        var perLine = new List<int>();
        using var doc = JsonDocument.Parse(jsonArray);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.TryGetProperty("error", out var e))
                return OcrResult.Fail(e.GetString() ?? "Erro no ambiente.");
            string text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            int x = 0, y = 0, w = 0, h = 0;
            if (el.TryGetProperty("box", out var box) && box.GetArrayLength() >= 4)
            {
                x = box[0].GetInt32(); y = box[1].GetInt32();
                w = Math.Max(0, box[2].GetInt32() - x);
                h = Math.Max(0, box[3].GetInt32() - y);
            }
            words.Add(new OcrWord { Text = text, X = x, Y = y, W = w, H = h });
            perLine.Add(1);
        }
        return new OcrResult
        {
            LineCount = perLine.Count, Words = words, WordsPerLine = perLine,
        };
    }

    internal static void EnsureScript()
    {
        Directory.CreateDirectory(EnvDir);
        if (!File.Exists(ScriptPath))
            File.WriteAllText(ScriptPath, Script);
    }

    internal const string Script = """
        import sys, json, base64, io
        print(json.dumps({"ready": True}), flush=True)
        try:
        import easyocr
        _READERS = {}
        def reader(lang):
            langs = ["ja"] if lang == "jpn" else ["en"]
            key = ",".join(langs)
            if key not in _READERS:
                _READERS[key] = easyocr.Reader(langs)
            return _READERS[key]
        except Exception as ex:
            print(json.dumps({"error": "not-installed: " + str(ex)}), flush=True)
            print(json.dumps({"end": True}), flush=True)
            sys.exit(0)
        for raw in sys.stdin:
            raw = raw.strip()
            if not raw:
                continue
            try:
                if " " in raw:
                    lang, b64 = raw.split(" ", 1)
                else:
                    lang, b64 = "eng", raw
                data = base64.b64decode(b64)
                try:
                    res = reader(lang).readtext(data)
                except Exception:
                    res = reader("eng").readtext(data)
                for box, text, _ in res:
                    xs = [p[0] for p in box]
                    ys = [p[1] for p in box]
                    print(json.dumps({"text": text,
                        "box": [int(min(xs)), int(min(ys)), int(max(xs)), int(max(ys))]}),
                        flush=True)
            except Exception as ex:
                print(json.dumps({"error": str(ex)}), flush=True)
            print(json.dumps({"end": True}), flush=True)
        """;
}
