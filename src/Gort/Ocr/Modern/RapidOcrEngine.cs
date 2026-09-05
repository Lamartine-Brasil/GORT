using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace Gort.Ocr.Modern;

/// <summary>
/// Motor de reconhecimento moderno embarcado (RF-121: ONNX Runtime + RapidOCR,
/// Apêndice A). Devolve palavras por linha (ReturnWordBox; CJK por caractere);
/// linhas sem palavras viram uma "palavra" com a caixa da linha (RF-141).
/// Limite de P-30 linhas por imagem (RF-130 🔒).
/// </summary>
public sealed class RapidOcrEngine : IOcrEngine, IDisposable
{
    public string Id => "modern";
    public bool ProvidesWordBoxes => true;   // RF-351: caixas por palavra

    private readonly string _dataDir;
    private readonly string _libDir;
    private readonly Func<bool> _verticalOption;
    private RapidOcr? _ocr;
    private bool _blocked;

    /// <param name="verticalOption">Opção de orientação vertical (RF-140).</param>
    public RapidOcrEngine(string dataDir, string libDir, Func<bool> verticalOption)
    {
        _dataDir = dataDir;
        _libDir = libDir;
        _verticalOption = verticalOption;
    }

    public bool IsAvailable => !_blocked && ResolvePaths() is not null;

    public string? UnavailableReason =>
        _blocked
            ? "O motor moderno falhou ao iniciar. Abra a ajuda e reinicie a tradução para tentar de novo."
            : ResolvePaths() is null ? "Modelos do motor moderno não encontrados." : null;

    /// <summary>
    /// RF-151: interseção entre o que o motor reconhece e {eng, jpn}.
    /// O pacote latino cobre inglês; japonês exige o par rec+dicionário
    /// japonês na pasta de dados (Etapa 14 instala).
    /// </summary>
    public IReadOnlyList<string> SupportedOcrLanguages()
    {
        var langs = new List<string> { "eng" };
        if (FindJapanesePair() is not null) langs.Add("jpn");
        return langs;
    }

    public void Dispose()   // RF-016
    {
        try { _ocr?.Dispose(); } catch { }
        _ocr = null;
    }

    public async Task<OcrResult> RecognizeAsync(ProcessedImage img, string ocrLang,
        CancellationToken ct)
    {
        if (ocrLang != "eng" && ocrLang != "jpn")
            return OcrResult.Fail($"Idioma '{ocrLang}' indisponível no motor moderno.");
        if (ocrLang == "jpn" && !SupportedOcrLanguages().Contains("jpn"))
            return OcrResult.Fail("Modelo de japonês ausente no motor moderno.");
        try
        {
            EnsureInit();
        }
        catch (Exception ex)
        {
            return OcrResult.Fail(ex.Message);   // RF-145: vazio + mensagem
        }

        try
        {
            using var bmp = ToBitmap(img);
            var options = RapidOcrOptions.Default with { ReturnWordBox = true };
            var res = await _ocr!.DetectAsync(bmp, options, ct).ConfigureAwait(false);
            return Map(res, _verticalOption());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return OcrResult.Fail(ex.Message);   // RF-145
        }
    }

    // ---- inicialização (RF-128/129/131) ----

    private void EnsureInit()
    {
        if (_ocr is not null) return;
        if (_blocked)
            throw new InvalidOperationException(UnavailableReason);
        try
        {
            var paths = ResolvePaths()
                ?? throw new InvalidOperationException(
                    "Modelos do motor moderno não encontrados. Reinstale o programa.");
            _ocr = new RapidOcr();
            // Forma 1 (padrão) seria _ocr.InitModels() sem args, mas usa todos
            // os núcleos; com caminhos explícitos dá para limitar as threads.
            TryInitWithPaths(paths);                             // formas 1–3
        }
        catch (Exception)
        {
            _blocked = true;                                        // RF-131
            _ocr?.Dispose();
            _ocr = null;
            throw new InvalidOperationException(
                "Não foi possível iniciar o motor moderno. Abra a página de ajuda. " +
                "A tradução foi parada e não haverá nova tentativa até reiniciar.");
        }
    }

    private sealed class ModelPaths
    {
        public string Det = "", Cls = "", Rec = "", Dict = "";
    }

    private static readonly string[] DetNames =
        ["ch_PP-OCRv5_mobile_det.onnx", "ch_PP-OCRv4_det.onnx"];
    private static readonly string[] ClsNames =
        ["ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx", "ch_ppocr_mobile_v2.0_cls_infer.onnx"];
    private static readonly string[] RecNames =
        ["latin_PP-OCRv5_rec_mobile_infer.onnx", "en_PP-OCRv4_rec_infer.onnx"];
    private static readonly string[] DictNames =
        ["ppocrv5_latin_dict.txt", "en_dict.txt"];

    private ModelPaths? ResolvePaths()
    {
        // RF-128: subpasta de bibliotecas do programa, depois dados do usuário.
        string exeDir = AppContext.BaseDirectory;
        string[] libCandidates =
        [
            Path.Combine(_libDir, "ocr", "modern"),
            Path.Combine(exeDir, "lib", "ocr", "modern"),
            Path.Combine(exeDir, "models", "v5"),   // NuGet copia o pacote aqui
            _dataDir,
        ];
        string? det = null, cls = null, rec = null, dict = null, dir = null;
        foreach (var d in libCandidates)
        {
            det = Pick(d, DetNames); cls = Pick(d, ClsNames);
            rec = Pick(d, RecNames); dict = Pick(d, DictNames);
            if (det is not null && cls is not null && rec is not null && dict is not null)
            { dir = d; break; }
            det = cls = rec = dict = null;
        }
        if (dir is null) return null;

        // RF-128: achou fora dos dados → copia para lá.
        if (!SameDir(dir, _dataDir))
        {
            try
            {
                Directory.CreateDirectory(_dataDir);
                foreach (var f in new[] { det!, cls!, rec!, dict! })
                {
                    string dst = Path.Combine(_dataDir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst);
                }
            }
            catch { /* segue com o caminho original */ }
        }

        return new ModelPaths { Det = det!, Cls = cls!, Rec = rec!, Dict = dict! };
    }

    /// <summary>
    /// Teto de threads do ONNX. Com 0 a biblioteca usa TODOS os núcleos —
    /// num PC gamer de 22 núcleos isso trava o jogo. Teto baixo: o OCR
    /// continua rápido e o jogo respira.
    /// </summary>
    private static int OcrThreadCount() =>
        System.Math.Max(1, System.Math.Min(4, System.Environment.ProcessorCount));

    private void TryInitWithPaths(ModelPaths p)
    {
        // Forma 2: caminhos diretos.
        try
        {
            _ocr!.InitModels(p.Det, p.Cls, p.Rec, p.Dict, numThread: OcrThreadCount());
            return;
        }
        catch { }
        // Forma 3 (RF-129 🔒): caminho puramente ASCII — caminhos com
        // caracteres não-ASCII quebram a biblioteca nativa. Recria a
        // instância: a forma 2 pode ter inicializado pela metade.
        try { _ocr?.Dispose(); } catch { }
        _ocr = new RapidOcr();
        string ascii = Path.Combine(Path.GetTempPath(), "gort-ocr-modern");
        Directory.CreateDirectory(ascii);
        string det = CopyAscii(p.Det, ascii), cls = CopyAscii(p.Cls, ascii);
        string rec = CopyAscii(p.Rec, ascii), dict = CopyAscii(p.Dict, ascii);
        _ocr!.InitModels(det, cls, rec, dict, numThread: OcrThreadCount());
    }

    private static string CopyAscii(string src, string dir)
    {
        string dst = Path.Combine(dir, Path.GetFileName(src));
        if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length)
            File.Copy(src, dst, overwrite: true);
        return dst;
    }

    private static string? Pick(string dir, string[] names)
    {
        foreach (var n in names)
        {
            string p = Path.Combine(dir, n);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static bool SameDir(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private (string Rec, string Dict)? FindJapanesePair()
    {
        string[] dirs =
        [
            _dataDir,
            Path.Combine(AppContext.BaseDirectory, "lib", "ocr", "modern"),
            Path.Combine(AppContext.BaseDirectory, "models", "v5"),
        ];
        foreach (var d in dirs)
        {
            string rec = Path.Combine(d, "japanese_PP-OCRv5_rec_mobile_infer.onnx");
            string dict = Path.Combine(d, "ppocrv5_japanese_dict.txt");
            if (File.Exists(rec) && File.Exists(dict)) return (rec, dict);
        }
        return null;
    }

    // ---- mapeamento para o contrato 6.4 ----

    internal OcrResult Map(RapidOcrNet.OcrResult res, bool verticalOption)
    {
        var blocks = res.TextBlocks ?? [];
        if (blocks.Length > Params.P30_ModernMaxLines)          // RF-130 🔒
            blocks = blocks[..Params.P30_ModernMaxLines];

        var order = verticalOption ? ReorderVertical(blocks) : blocks.ToList();
        var words = new List<OcrWord>();
        var perLine = new List<int>();
        foreach (var b in order)
        {
            var box = BoxOf(b.BoxPoints);
            int n = 0;
            if (b.WordResults is { Length: > 0 })
            {
                foreach (var w in b.WordResults)
                {
                    if (string.IsNullOrWhiteSpace(w.Text)) continue;
                    var wb = BoxOf(w.BoxPoints);                // RF-142 🔒
                    words.Add(new OcrWord { Text = w.Text, X = wb.X, Y = wb.Y, W = wb.W, H = wb.H });
                    n++;
                }
            }
            if (n == 0 && !string.IsNullOrWhiteSpace(b.Text))
            {
                words.Add(new OcrWord                           // RF-141
                { Text = b.Text, X = box.X, Y = box.Y, W = box.W, H = box.H });
                n = 1;
            }
            perLine.Add(n);
        }
        return new OcrResult { LineCount = order.Count, Words = words, WordsPerLine = perLine };
    }

    internal static (int X, int Y, int W, int H) BoxOf(SkiaSharp.SKPointI[] pts) =>
        pts is { Length: 4 }
            ? QuadBox.FromPoints((pts[0].X, pts[0].Y), (pts[1].X, pts[1].Y),
                (pts[2].X, pts[2].Y), (pts[3].X, pts[3].Y))
            : (0, 0, 0, 0);

    /// <summary>
    /// RF-140 🔒: com a opção ativa, as linhas verticais (h &gt; w × P-32) são
    /// reordenadas por coluna (direita decrescente, topo crescente); as
    /// horizontais mantêm a posição original na lista.
    /// </summary>
    public static List<TextBlock> ReorderVertical(TextBlock[] blocks)
    {
        var verts = new List<(TextBlock B, int X2, int Y)>();
        var isVert = new bool[blocks.Length];
        for (int i = 0; i < blocks.Length; i++)
        {
            var (x, y, w, h) = BoxOf(blocks[i].BoxPoints);
            if (h > w * Params.P32_ModernVerticalRatio)         // 🔒 1,5
            {
                isVert[i] = true;
                verts.Add((blocks[i], x + w, y));
            }
        }
        verts.Sort((a, b) => b.X2 != a.X2 ? b.X2.CompareTo(a.X2) : a.Y.CompareTo(b.Y));
        var outList = new List<TextBlock>(blocks.Length);
        int vi = 0;
        for (int i = 0; i < blocks.Length; i++)
            outList.Add(isVert[i] ? verts[vi++].B : blocks[i]);
        return outList;
    }

    private static SKBitmap ToBitmap(ProcessedImage img)
    {
        var bmp = new SKBitmap(img.Width, img.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        if (img.Channels == 4)
        {
            Marshal.Copy(img.Bytes, 0, bmp.GetPixels(), img.Bytes.Length);
        }
        else
        {
            // RF-117: replica o canal único nos três de cor.
            var bgra = Preprocess.ConvertChannels(img.Bytes, img.Width, img.Height, img.Channels, 4);
            Marshal.Copy(bgra, 0, bmp.GetPixels(), bgra.Length);
        }
        return bmp;
    }
}
