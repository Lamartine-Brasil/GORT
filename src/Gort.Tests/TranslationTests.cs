using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gort.Translate;

namespace Gort.Tests;

/// <summary>Etapa 7 — protocolo 18.1 e tradutor web (partes puras).</summary>
public class TranslationTests
{
    private sealed class FakeService : ITranslationService
    {
        public string Id => "fake";
        public string Display => "Fake";
        public string DefaultToken => "//////";
        public bool SupportsBridge => false;
        public bool UsesResultMemory => true;
        public bool UsesCollectanea => true;
        public List<string> Bodies { get; } = new();
        public Func<string, string> Reply = _ => "";
        public int DelayMs;
        public string? FailWith;

        public async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
            string src, string dst, CancellationToken ct)
        {
            Bodies.Add(texts[0]);
            if (DelayMs > 0) await Task.Delay(DelayMs, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (FailWith is not null) return new ServiceResult { Error = FailWith };
            return new ServiceResult { Translations = new List<string> { Reply(texts[0]) } };
        }
    }

    private sealed class FakeMemory : ITranslationMemory
    {
        private readonly Dictionary<string, string> _d = new();
        public string? TryGet(string serviceId, string source) =>
            _d.TryGetValue(serviceId + "\n" + source, out var t) ? t : null;
        public void Store(string serviceId, string source, string translated) =>
            _d[serviceId + "\n" + source] = translated;
        public bool Has(string serviceId, string source) =>
            _d.ContainsKey(serviceId + "\n" + source);
    }

    private static TranslationPipeline Pipe(FakeService svc, FakeMemory mem) =>
        new(id => id == "fake" ? svc : Services.Get(id), mem);

    [Fact]
    public void ResolvePair_Default_En_PtBr_And_Ja_En_Override()
    {
        var p = new Gort.Config.Profile();   // padrão: en→pt-BR
        var (src, dst) = Gort.Loop.TranslationLoop.ResolvePair(p, "web-free");
        Assert.Equal("en", src);
        Assert.Equal("pt", dst);
        // Par por serviço: japonês→inglês sem mudar o padrão global.
        p.OcrLanguage = "ja";
        p.ServiceSource["web-free"] = "ja";
        p.ServiceTarget["web-free"] = "en";
        var (src2, dst2) = Gort.Loop.TranslationLoop.ResolvePair(p, "web-free");
        Assert.Equal("ja", src2);
        Assert.Equal("en", dst2);
        Assert.Equal("pt-BR", p.TargetLanguage);   // global intacto
    }

    [Fact]
    public async Task Empty_Returns_Empty_Without_Call()
    {
        var svc = new FakeService();
        var pipe = Pipe(svc, new FakeMemory());
        var r = await pipe.TranslateBatchAsync("fake", new List<string> { "", "" },
            "en", "pt", false, CancellationToken.None);
        Assert.Empty(svc.Bodies);                                  // RF-228
        Assert.Equal(2, r.PerText.Count);
    }

    [Fact]
    public async Task Single_Request_Format_And_Raw()
    {
        var svc = new FakeService { Reply = _ => "//////OLÁ" };
        var pipe = Pipe(svc, new FakeMemory());
        var r = await pipe.TranslateBatchAsync("fake", new List<string> { "HELLO" },
            "en", "pt", false, CancellationToken.None);
        Assert.Single(svc.Bodies);
        Assert.Equal("//////HELLO\n", svc.Bodies[0]);              // RF-231
        Assert.Equal(new List<string?> { "OLÁ" }, r.PerText);
        Assert.Equal("//////OLÁ\n", r.Raw);                        // RF-237
    }

    [Fact]
    public async Task MultiBlock_Splits_In_Order_Short_Rests_Empty()
    {
        var svc = new FakeService { Reply = _ => "//////A//////B" };
        var pipe = Pipe(svc, new FakeMemory());
        var r = await pipe.TranslateBatchAsync("fake",
            new List<string> { "a", "b", "c" }, "en", "pt", false, CancellationToken.None);
        Assert.Equal(new List<string?> { "A", "B", "" }, r.PerText);  // RF-233
    }

    [Fact]
    public async Task Cache_Skips_Network_And_Keeps_Order()
    {
        var svc = new FakeService { Reply = _ => "//////A2//////C2" };
        var mem = new FakeMemory();
        mem.Store("fake", "b", "B-cached");
        var pipe = Pipe(svc, mem);
        var r = await pipe.TranslateBatchAsync("fake",
            new List<string> { "a", "b", "c" }, "en", "pt", false, CancellationToken.None);
        Assert.Single(svc.Bodies);
        Assert.DoesNotContain("//////b", svc.Bodies[0]);           // RF-230: só faltantes
        Assert.Equal("B-cached", r.PerText[1]);                    // ordem com cache no meio
        Assert.True(mem.Has("fake", "a"));                         // RF-235: grava já
    }

    [Fact]
    public async Task Error_Returns_Message_Cycle_Continues()
    {
        var svc = new FakeService { FailWith = "HTTP 500" };
        var pipe = Pipe(svc, new FakeMemory());
        var r = await pipe.TranslateBatchAsync("fake", new List<string> { "a" },
            "en", "pt", false, CancellationToken.None);
        Assert.Equal("HTTP 500", r.Error);                         // RF-236
    }

    [Fact]
    public async Task New_Request_Cancels_Previous()
    {
        var svc = new FakeService { DelayMs = 5000, Reply = _ => "//////X" };
        var pipe = Pipe(svc, new FakeMemory());
        var t1 = pipe.TranslateBatchAsync("fake", new List<string> { "a" },
            "en", "pt", false, CancellationToken.None);
        await Task.Delay(100);
        svc.DelayMs = 0;
        var r2 = await pipe.TranslateBatchAsync("fake", new List<string> { "b" },
            "en", "pt", false, CancellationToken.None);
        var r1 = await t1;
        Assert.True(r1.Cancelled);                                 // RF-229/238
        Assert.False(r2.Cancelled);
        Assert.Equal("X", r2.PerText[0]);
    }

    [Fact]
    public async Task Unknown_Service_Errors()
    {
        var pipe = new TranslationPipeline(_ => null, new FakeMemory());
        var r = await pipe.TranslateBatchAsync("nope", new List<string> { "a" },
            "en", "pt", false, CancellationToken.None);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void Advanced_Token_Shorten_And_Clean()
    {
        Assert.Equal("////", TokenHelper.Shorten("//////", true));  // RF-234: 6→−2
        Assert.Equal("@@@@", TokenHelper.Shorten("@@@@@@@", true));  // 7→−3
        Assert.Equal("@@@@", TokenHelper.Shorten("@@@@@@", true));
        Assert.Equal("//////", TokenHelper.Shorten("//////", false));
        var parts = TokenHelper.CleanParts(new[] { "//A//", "////", "B" }, "//////", true);
        Assert.Equal(new[] { "A", "B" }, parts);   // pontas limpas, vazia descartada
        var plain = TokenHelper.CleanParts(new[] { "A", "" }, "//////", false);
        Assert.Equal(new[] { "A", "" }, plain);
    }

    [Fact]
    public void LangCodes_Table()
    {
        Assert.Equal("en", LangCodes.CodeFor("web-free", "en"));
        Assert.Equal("ja", LangCodes.CodeFor("web-free", "ja"));
        Assert.Equal("pt", LangCodes.CodeFor("web-free", "pt-BR"));  // RF-314
        Assert.Equal("", LangCodes.CodeFor("web-free", "xx"));
        Assert.True(LangCodes.SameCode("en", "en-US"));              // RF-316
        Assert.Equal("en", LangCodes.KeyForOcr("eng"));              // RF-147
        Assert.Equal("pt-BR", LangCodes.DefaultTarget);
    }

    [Fact]
    public void Web_Parse_Joins_Segments_With_Space()
    {
        string json = "[[[\"Ol\\u00e1\",\"Hello\",null,null,10],[\" mundo\",\" world\",null,null,5]],null,\"en\"]";
        Assert.Equal("Olá  mundo", WebFreeTranslator.ParseResponse(json));  // RF-244
        Assert.Throws<System.InvalidOperationException>(
            () => WebFreeTranslator.ParseResponse("{}"));
    }

    [Fact]
    public void Web_Url_Carries_Params()
    {
        string url = WebFreeTranslator.BuildUrl("a b", "en", "pt", "gtx");
        Assert.Contains("client=gtx", url);
        Assert.Contains("sl=en", url);
        Assert.Contains("tl=pt", url);
        Assert.Contains("dt=t", url);
        Assert.Contains("q=a%20b", url);
    }

    [Fact]
    public void Raw_Format()
    {
        Assert.Equal("//////A\n//////\n",
            TranslationPipeline.BuildRaw(new List<string?> { "A", null }, "//////"));
    }
}
