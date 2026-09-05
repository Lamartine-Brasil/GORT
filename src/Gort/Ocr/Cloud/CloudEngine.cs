using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Imaging;
using SkiaSharp;

namespace Gort.Ocr.Cloud;

/// <summary>
/// Motor de nuvem (RF-121/documento em imagem): melhor qualidade, com cota;
/// SÓ em modo pontual (RF-122). Falhas viram vazio+mensagem (RF-145).
/// Autenticação por conta de serviço (JWT→OAuth2); contagem local P-29.
/// </summary>
public sealed class CloudEngine : IOcrEngine
{
    public string Id => "cloud";
    public bool ProvidesWordBoxes => true;
    public bool PunctualOnly => true;   // RF-122

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly Func<string> _credFile;
    private readonly Func<int> _limit;
    private readonly CloudQuota _quota = new();
    private string? _token;
    private DateTime _tokenExp;

    public CloudEngine(Func<string> credFile, Func<int> limit)
    {
        _credFile = credFile; _limit = limit;
    }

    public bool IsAvailable => CredPath() is not null;

    public string? UnavailableReason => CredPath() is null
        ? "Selecione o arquivo de credencial da conta de serviço."   // RF-126
        : null;

    private string? CredPath()
    {
        string f = _credFile();
        if (string.IsNullOrWhiteSpace(f)) return null;
        string p = Path.IsPathRooted(f) ? f : Path.Combine(Paths.BaseDir, f);
        return File.Exists(p) ? p : null;
    }

    public IReadOnlyList<string> SupportedOcrLanguages() =>
        new List<string> { "eng", "jpn", "auto" };   // +automático (RF-121)

    /// <summary>RF-127: "usadas / limite" para a credencial vigente.</summary>
    public string UsageText()
    {
        string? c = CredPath();
        if (c is null) return $"0 / {_limit()}";
        var (used, limit) = _quota.Status(c, _limit());
        return $"{used} / {limit}";
    }

    public async Task<OcrResult> RecognizeAsync(ProcessedImage img, string ocrLang,
        CancellationToken ct)
    {
        string? cred = CredPath();
        if (cred is null)
            return OcrResult.Fail("Selecione o arquivo de credencial do motor de nuvem.");
        int limit = _limit();
        if (_quota.Status(cred, limit).Used >= limit)                 // RF-124/125
            return OcrResult.Fail(
                "Cota mensal do motor de nuvem esgotada. (A contagem local pode " +
                "divergir da contagem real do serviço.)");
        try
        {
            string token = await AccessTokenAsync(cred, ct).ConfigureAwait(false);
            var (b64, scale) = ToScaledBase64(img);
            string body = await AnnotateAsync(token, b64, ocrLang, ct).ConfigureAwait(false);
            var res = Parse(body);
            if (res.Error is not null) return res;
            if (scale != 1) res = Rescale(res, scale);
            _quota.TryConsume(cred, limit);   // conta só o sucesso (sem queimar cota à toa)
            return res;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { return OcrResult.Fail(ex.Message); }  // RF-145
    }

    /// <summary>Reduz o lado maior p/ 1600 px (payload) e devolve a escala p/ voltar as caixas.</summary>
    private static (string B64, double Scale) ToScaledBase64(ProcessedImage img)
    {
        const int maxSide = 1600;
        byte[] bgra = img.Channels == 4 ? img.Bytes
            : Preprocess.ConvertChannels(img.Bytes, img.Width, img.Height, img.Channels, 4);
        int w = img.Width, h = img.Height;
        double scale = 1;
        if (System.Math.Max(w, h) > maxSide)
        {
            scale = (double)System.Math.Max(w, h) / maxSide;
            w = (int)(w / scale); h = (int)(h / scale);
        }
        using var src = new SKBitmap(img.Width, img.Height,
            SKColorType.Bgra8888, SKAlphaType.Opaque);
        System.Runtime.InteropServices.Marshal.Copy(bgra, 0, src.GetPixels(), bgra.Length);
        using var resized = (w == img.Width && h == img.Height)
            // High obsoleto = cúbico Mitchell (mesmos pixels, sem o aviso).
            ? null : src.Resize(new SKImageInfo(w, h),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
        using var data = (resized ?? src).Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) throw new InvalidOperationException("Falha ao codificar imagem.");
        return (Convert.ToBase64String(data.ToArray()), scale);
    }

    private static OcrResult Rescale(OcrResult res, double scale)
    {
        var words = new List<OcrWord>();
        foreach (var wd in res.Words)
            words.Add(new OcrWord
            {
                Text = wd.Text,
                X = (int)(wd.X * scale), Y = (int)(wd.Y * scale),
                W = (int)(wd.W * scale), H = (int)(wd.H * scale),
            });
        return new OcrResult
        {
            LineCount = res.LineCount, Words = words, WordsPerLine = res.WordsPerLine,
        };
    }

    internal static string JwtUnsigned(string email)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string header = Base64Url("{\"alg\":\"RS256\",\"typ\":\"JWT\"}");
        string claim = Base64Url(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = email,
            ["scope"] = "https://www.googleapis.com/auth/cloud-vision",
            ["aud"] = "https://oauth2.googleapis.com/token",
            ["exp"] = now + 3600,
            ["iat"] = now,
        }));
        return header + "." + claim;
    }

    internal static string Base64Url(string s) => Base64Url(Encoding.UTF8.GetBytes(s));

    internal static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<string> AccessTokenAsync(string cred, CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow < _tokenExp)
            return _token;
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(cred, ct)
            .ConfigureAwait(false));
        var root = doc.RootElement;
        string email = root.GetProperty("client_email").GetString() ?? "";
        string key = root.GetProperty("private_key").GetString() ?? "";
        string unsigned = JwtUnsigned(email);
        byte[] sig;
        using (var rsa = RSA.Create())
        {
            rsa.ImportFromPem(key);
            sig = rsa.SignData(Encoding.ASCII.GetBytes(unsigned),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
            ["assertion"] = unsigned + "." + Base64Url(sig),
        });
        using var resp = await Http.PostAsync(
            "https://oauth2.googleapis.com/token", form, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var tok = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)
            .ConfigureAwait(false));
        _token = tok.RootElement.GetProperty("access_token").GetString();
        _tokenExp = DateTime.UtcNow.AddMinutes(50);
        return _token ?? throw new InvalidOperationException("Token ausente.");
    }

    private async Task<string> AnnotateAsync(string token, string b64,
        string ocrLang, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://vision.googleapis.com/v1/images:annotate");
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var request = new Dictionary<string, object>
        {
            ["image"] = new Dictionary<string, object> { ["content"] = b64 },
            ["features"] = new object[]
            {
                new Dictionary<string, object> { ["type"] = "DOCUMENT_TEXT_DETECTION" },
            },
        };
        // Dica de idioma: eng/ja melhoram segmentação; demais omitem (auto).
        if (ocrLang == "eng" || ocrLang == "jpn")
            request["imageContext"] = new Dictionary<string, object>
                { ["languageHints"] = new[] { ocrLang == "jpn" ? "ja" : "en" } };
        req.Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["requests"] = new object[] { request },
        }), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Resposta: palavras com caixas dos vértices (RF-142); quebras de linha
    /// comparando a posição acumulada com as quebras do texto completo (RF-144).
    /// </summary>
    internal static OcrResult Parse(string json)
    {
        var words = new List<(string Text, int X, int Y, int W, int H, int Offset)>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("responses", out var responses)
            || responses.GetArrayLength() == 0)
            return OcrResult.Fail("Resposta inesperada do motor de nuvem.");
        var resp0 = responses[0];
        string full = "";
        if (resp0.TryGetProperty("fullTextAnnotation", out var fullAnn)
            && fullAnn.TryGetProperty("text", out var t))
            full = t.GetString() ?? "";
        if (!resp0.TryGetProperty("fullTextAnnotation", out var ann)
            || !ann.TryGetProperty("pages", out var pages))
            return new OcrResult();
        int offset = 0;
        foreach (var page in pages.EnumerateArray())
        {
            if (!page.TryGetProperty("blocks", out var blocks)) continue;
            foreach (var block in blocks.EnumerateArray())
            {
                if (!block.TryGetProperty("paragraphs", out var paras)) continue;
                foreach (var para in paras.EnumerateArray())
                {
                    if (!para.TryGetProperty("words", out var wds)) continue;
                    foreach (var w in wds.EnumerateArray())
                    {
                        var sb = new StringBuilder();
                        int x1 = int.MaxValue, y1 = int.MaxValue;
                        int x2 = int.MinValue, y2 = int.MinValue;
                        if (w.TryGetProperty("symbols", out var syms))
                            foreach (var s in syms.EnumerateArray())
                            {
                                if (s.TryGetProperty("text", out var st))
                                    sb.Append(st.GetString());
                                if (s.TryGetProperty("boundingBox", out var bb))
                                    Accum(bb, ref x1, ref y1, ref x2, ref y2);
                            }
                        if (sb.Length == 0) continue;
                        if (x2 < x1) { x1 = 0; y1 = 0; x2 = 0; y2 = 0; }
                        words.Add((sb.ToString(), x1, y1,
                            Math.Max(0, x2 - x1), Math.Max(0, y2 - y1), offset));
                        offset += sb.Length + 1;
                    }
                }
            }
        }
        // RF-144: linhas pela posição acumulada vs quebras do texto completo.
        var breaks = new List<int>();
        for (int i = 0; i < full.Length; i++)
            if (full[i] == '\n') breaks.Add(i);
        var lines = new List<List<(string, int, int, int, int)>>();
        foreach (var w in words)
        {
            int line = 0;
            while (line < breaks.Count && breaks[line] < w.Offset) line++;
            while (lines.Count <= line) lines.Add(new());
            lines[line].Add((w.Text, w.X, w.Y, w.W, w.H));
        }
        var finalWords = new List<OcrWord>();
        var perLine = new List<int>();
        foreach (var ln in lines)
        {
            ln.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            perLine.Add(ln.Count);
            foreach (var (text, x, y, ww, hh) in ln)
                finalWords.Add(new OcrWord { Text = text, X = x, Y = y, W = ww, H = hh });
        }
        return new OcrResult
        {
            LineCount = lines.Count, Words = finalWords, WordsPerLine = perLine,
        };
    }

    private static void Accum(JsonElement bb, ref int x1, ref int y1, ref int x2, ref int y2)
    {
        if (!bb.TryGetProperty("vertices", out var vs)) return;
        foreach (var v in vs.EnumerateArray())   // RF-142: min/max, nunca direto
        {
            int x = v.TryGetProperty("x", out var xe) ? xe.GetInt32() : 0;
            int y = v.TryGetProperty("y", out var ye) ? ye.GetInt32() : 0;
            x1 = Math.Min(x1, x); y1 = Math.Min(y1, y);
            x2 = Math.Max(x2, x); y2 = Math.Max(y2, y);
        }
    }
}
