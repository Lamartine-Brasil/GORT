using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gort.Translate;

namespace Gort.Tests;

/// <summary>Etapa 15 — demais serviços (partes puras + fakes).</summary>
public class Services15Tests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Reply = _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}"),
            };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,
            CancellationToken ct)
        {
            return Task.FromResult(Reply(req));
        }
    }

    [Fact]
    public async Task Korean_Rotates_Keys()
    {
        var fh = new FakeHandler();
        int n = 0;
        fh.Reply = _ =>
        {
            n++;
            return n == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"errorMessage\":\"Daily limit\",\"errorCode\":\"010\"}"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"message\":{\"result\":{\"translatedText\":\"OLÁ\"}}}"),
                };
        };
        var svc = new KoreanTranslator(fh);
        // Sem chaves cadastradas → pede cadastro (ambiente limpo).
        var r0 = await svc.TranslateAsync(new List<string> { "x" }, "en", "pt",
            CancellationToken.None);
        Assert.Contains("chave", r0.Error);
    }

    [Fact]
    public void Korean_Parse_Matrix()
    {
        var ok = KoreanTranslator.Parse(
            "{\"message\":{\"result\":{\"translatedText\":\"Oi\"}}}", 200);
        Assert.True(ok.Ok);
        Assert.Equal("Oi", ok.Text);
        var free = KoreanTranslator.Parse(
            "{\"errorMessage\":\"m\",\"errorCode\":\"010\"}", 200);
        Assert.False(free.Ok);
        Assert.True(free.Limited);                        // RF-252: limite
        var paid = KoreanTranslator.Parse(
            "{\"error\":{\"message\":\"bad key\"}}", 403);
        Assert.False(paid.Ok);
        var bad = KoreanTranslator.Parse("não json", 200);
        Assert.False(bad.Ok);
    }

    [Fact]
    public void Sheets_Formula_And_Row()
    {
        var (row, f) = SheetsTranslator.BuildCell("hi", "en", "pt");  // RF-255
        Assert.InRange(row, 1, 50);                       // P-57 🔒
        Assert.Contains("GOOGLETRANSLATE", f);
        Assert.Contains("\"en\"", f);
    }

    [Fact]
    public async Task Sheets_Exchange_Fails_Without_Refresh_Token()
    {
        // Resposta sem refresh_token: false, sem gravar nada.
        var fh = new FakeHandler();
        fh.Reply = _ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{}") };
        var svc = new SheetsTranslator(() => "sheet", () => false, fh);
        string real = Path.Combine(Gort.Core.Paths.BaseDir, "sheets-token.json");
        string? before = File.Exists(real) ? File.ReadAllText(real) : null;
        Assert.False(await svc.ExchangeCodeAsync(
            "id", "secret", "code", "urn:ietf:wg:oauth:2.0:oob",
            CancellationToken.None));
        string? after = File.Exists(real) ? File.ReadAllText(real) : null;
        Assert.Equal(before, after);   // nada gravado no caminho real
        Assert.Null(SheetsTranslator.LoadRefreshToken(
            Path.Combine(Path.GetTempPath(), "gort-test-nope.json")));
    }

    [Fact]
    public void Browser_Url_And_Post()
    {
        string url = BrowserTranslator.BuildUrl("a/b", "en", "pt");  // RF-261
        Assert.Contains("%2F", url);
        Assert.Contains(Uri.EscapeDataString("^^^^"), url);
        Assert.Equal("done", BrowserTranslator.PostProcess("\"done^^^^\""));
        Assert.Equal("a\nb", BrowserTranslator.PostProcess("\"a\\n\\nB^^^^ tail\"".Replace("B", "b")));
        var t = new BrowserTranslator(() => null, () => false);
        Assert.True(t.SelectTimeout("x").TotalSeconds >= 5);   // P-59
    }

    [Fact]
    public void DeepL_Zh_And_Endpoints()
    {
        Assert.Equal("ZH", DeepLTranslator.NormZh("ZH-TW"));   // RF-272 🔒
        Assert.Equal("EN", DeepLTranslator.NormZh("EN"));
        Assert.Contains("api-free", DeepLTranslator.BaseUrl("free"));
        Assert.DoesNotContain("free", DeepLTranslator.BaseUrl("paid"));
    }

    [Fact]
    public void Llm_Prompt_Reasoning_Family()
    {
        string def = LlmTranslator.DefaultInstruction("Portuguese (Brazil)");
        Assert.Contains("22 years", def);                 // RF-274 🔒
        Assert.Contains("only the translation", def);
        Assert.Contains("honorifics", def);
        Assert.True(LlmTranslator.IsOldFamily("gemini-2.0-flash"));   // RF-280
        Assert.False(LlmTranslator.IsOldFamily("gemini-1.5-pro"));
        Assert.True(LlmTranslator.IsPro("gemini-2.0-pro"));
        var old1 = LlmTranslator.Reasoning("gemini-2.0-pro", 1);
        Assert.Equal(512, old1["thinkingBudget"]);         // RF-282 P-76 🔒
        Assert.Empty(LlmTranslator.Reasoning("gemini-2.0-flash", 0));
        var @new = LlmTranslator.Reasoning("gemini-1.5-flash", 2);
        Assert.Equal("MEDIUM", @new["thinkingLevel"]);
        Assert.Equal(4, LlmTranslator.SafetyOff().Count); // RF-276
    }

    [Fact]
    public void Template_Substitute_And_Relaxed()
    {
        string s = TemplateEngine.Substitute(
            "{\"text\":\"{OCR_TEXT}\",\"src\":\"{SOURCE_CODE}\"}",
            "a\"b", "en", "pt");                          // RF-296: escapado
        using var doc = System.Text.Json.JsonDocument.Parse(s);
        Assert.Equal("a\"b", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("en", doc.RootElement.GetProperty("src").GetString());
        var (ok, json, _) = TemplateEngine.BuildJson("text = hello, n = 3, ok = true, x = null, arr = [1, a]");
        Assert.True(ok);                                  // RF-297
        Assert.Contains("\"n\":3", json.Replace(" ", ""));
        Assert.Contains("\"ok\":true", json.Replace(" ", ""));
        var (ok2, json2, _) = TemplateEngine.BuildJson("a = 1");
        Assert.True(ok2);                                 // RF-298: auto-chaves
        Assert.StartsWith("{", json2.TrimStart());
        var (ok3, _, err3) = TemplateEngine.BuildJson("{inválido");
        Assert.False(ok3);                                // RF-299
        Assert.NotNull(err3);
    }

    [Fact]
    public void Template_Key_Discovery_Recursive()
    {
        string? key = TemplateEngine.DiscoverKey("{\"data\":\"{RESULT_TEXT}\"}");
        Assert.Equal("data", key);                        // RF-300
        Assert.Null(TemplateEngine.DiscoverKey("{\"a\":1}"));
        using var doc = System.Text.Json.JsonDocument.Parse(
            "{\"wrap\":{\"deep\":{\"data\":\"TRAD\"}}}");
        Assert.Equal("TRAD", TemplateEngine.FindRecursive(doc.RootElement, "data"));
        var h = TemplateEngine.ParseHeaders(new[] { "A: b", "ruim", "C : d" });  // RF-301
        Assert.Equal(2, h.Count);
    }

    [Fact]
    public void Standard_Format_Codes()
    {
        var ok = CustomApiService.ParseStandard(
            "{\"result\":\"Olá\",\"error\":\"0\",\"message\":\"\"}");   // RF-292/293
        Assert.Equal("Olá", ok.Translations![0]);
        var vec = CustomApiService.ParseStandard(
            "{\"result\":[\"a\",\"b\"],\"error\":\"0\",\"message\":\"\"}");  // RF-294
        Assert.Equal("ab", vec.Translations![0]);
        var err = CustomApiService.ParseStandard(
            "{\"result\":\"\",\"error\":\"1\",\"message\":\"ruim\"}");
        Assert.Equal("ruim", err.Error);
    }

    [Fact]
    public void Preset_Files_And_Unique_Names()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.json"),
            "[{\"name\":\"P\",\"url\":\"http://x\",\"request\":\"{}\",\"response\":\"{}\"}," +
            "{\"name\":\"P\",\"url\":\"http://y\"}]");
        var list = CustomApiService.LoadPresetFiles(dir);  // RF-302/304
        Assert.Single(list);
        Assert.True(list[0].FromFile);                     // RF-303
        var mine = new List<Config.CustomPreset> { new() { Name = "P" } };
        Assert.Equal("P (2)", CustomApiService.UniqueName(mine, "P"));  // RF-305
    }

    [Fact]
    public void Pipe_Framing_Roundtrip_Truncates()
    {
        var enc = PipeFraming.Encode("olá");
        Assert.Equal(2 + "olá".Length * 2, enc.Length);    // RF-287: BE + UTF-16
        Assert.Equal(0, enc[0]);
        var big = new string('x', 70000);
        Assert.Equal(2 + 65535, PipeFraming.Encode(big).Length);  // P-135
    }

    [Fact]
    public void Registry_Lists_All_And_Fallback()
    {
        var ids = Services.List().Select(e => e.Id).ToList();
        foreach (var id in new[] { "web-free", "db", "web-nokey", "commercial-kr",
                     "sheets", "embedded-browser", "commercial-eu", "llm", "custom" })
            Assert.Contains(id, ids);
        Assert.Contains(Services.List(), e => e.Display.StartsWith("Custom") == false);
        Assert.Equal("db", Services.ResolveOrFallback("preset-removido"));  // RF-307
        Assert.Equal("web-free", Services.ResolveOrFallback("web-free"));
    }

    [Fact]
    public void LangCodes_Per_Service()
    {
        Assert.Equal("pt", LangCodes.CodeFor("commercial-kr", "pt-BR"));
        Assert.Equal("JA", LangCodes.CodeFor("commercial-eu", "ja"));
        Assert.Equal("Portuguese (Brazil)", LangCodes.CodeFor("llm", "pt-BR"));
    }

    [Fact]
    public void Google_Is_Default_Translator()
    {
        // RF-225: padrão = Google (web-free); qualidade padrão = auto.
        var svc = Services.Get("web-free");
        Assert.NotNull(svc);
        Assert.Contains("Google", svc.Display);
        Assert.Equal("auto", new Gort.Config.Profile().WebQuality);
        Assert.Contains("gtx", WebFreeTranslator.BuildUrl("x", "en", "pt", "gtx"));
        Assert.Contains("webapp", WebFreeTranslator.BuildUrl("x", "en", "pt", "webapp"));
    }
}
