using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gort.Translate;

namespace Gort.Tests;

/// <summary>Etapa 10 — memória, coletânea, DB e exibição.</summary>
public class CacheTests
{
    private static string Tmp(string content, string name = "m.txt")
    {
        var p = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Memory_Format_Rtrim_Roundtrip()
    {
        var d = ResultMemory.Parse("/s\nhello \n/t\nolá\n/e\n\n/s\na\nb\nc\n/t\nx\n/e\n");
        Assert.Equal("olá", d["hello"]);   // RF-209: rtrim na origem
        Assert.Equal("x", d["a\nb\nc"]);
    }

    [Fact]
    public void Memory_Suspends_During_Write()
    {
        var mem = new ResultMemory();
        mem.Store("s", "a", "b");
        Assert.Equal("b", mem.TryGet("s", "a"));
    }

    [Fact]
    public async Task Memory_Flush_Appends()
    {
        var mem = new ResultMemory();
        mem.Store("s", "k", "v");
        await mem.FlushAsync();
        Assert.Equal("v", mem.TryGet("s", "k"));
    }

    [Fact]
    public void Collectanea_Exact_And_Info()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "g.txt");
        File.WriteAllText(file, "# Jogo X\n# v1\n/s\nhi\n/t\nolá\n/e\n");
        Assert.Equal("Jogo X\nv1", Collectanea.InfoOf(file));
    }

    [Fact]
    public void Collectanea_DbMode_Only_En_Ja()
    {
        var col = new Collectanea(() => new List<string>(), () => true, () => false);
        // Sem arquivos: nulo, sem erro.
        Assert.Null(col.TryGet("hello world", "en"));
        Assert.Null(col.TryGet("hello world", "fr"));   // RF-219: cai p/ exata
    }

    [Fact]
    public void Db_Exact_Partial_IgnoreCase_Marker()
    {
        var db = new DbTranslator();
        var path = Tmp("/s\nHello World\n/t\nOlá Mundo\n/e\n\n/s\nBye\n/t\n@@NORESULT@@\n/e\n");
        db.Reload(path, ignoreCase: false, partialMultiLine: false);
        Assert.Equal("Olá Mundo", db.Lookup("Hello World"));
        Assert.Null(db.Lookup("hello world"));          // diferencia
        Assert.Null(db.Lookup("Bye"));                  // RF-241: marcador → vazio
        db.Reload(path, ignoreCase: true, partialMultiLine: true);
        Assert.Equal("Olá Mundo", db.Lookup("hello world"));
        Assert.Equal("Olá Mundo", db.Lookup("say Hello World please"));  // parcial
    }

    [Fact]
    public async Task Db_Empty_Guides_Instead_Of_Blank()
    {
        var db = new DbTranslator();   // sem Reload: nenhum par
        var r = await db.TranslateAsync(
            new List<string> { "hello" }, "en", "pt", CancellationToken.None);
        Assert.NotNull(r.Error);
        Assert.Contains("Banco", r.Error);
    }

    [Fact]
    public async Task Db_Service_Splits_And_Skips_Memory()
    {
        var db = new DbTranslator();
        var path = Tmp("/s\na\n/t\nA\n/e\n");
        db.Reload(path, false, false);
        var mem = new ResultMemory();
        var pipe = new TranslationPipeline(_ => db, mem);
        var r = await pipe.TranslateBatchAsync("db",
            new List<string> { "a", "zzz" }, "en", "pt", false, CancellationToken.None);
        Assert.Equal("A", r.PerText[0]);
        Assert.Equal(Text.TextPipeline.NoResultMarker, r.PerText[1]);  // RF-190 filtra
        Assert.Equal(0, mem.Count("db"));                              // RF-214
    }

    [Fact]
    public void DisplayMemory_Stacks_Expires()
    {
        var dm = new DisplayMemory(() => 2, () => 10);
        var t0 = DateTime.UtcNow;
        Assert.Equal("um", dm.Apply("um", t0));
        Assert.Equal("dois\n\n\num", dm.Apply("dois", t0.AddSeconds(1)));  // RF-222
        Assert.Equal("tres\n\n\ndois", dm.Apply("tres", t0.AddSeconds(2))); // teto N
        Assert.Equal("tres\n\n\ndois",
            dm.Apply("", t0.AddSeconds(3)));                            // RF-224: vivas
        Assert.Equal("novo", dm.Apply("novo", t0.AddSeconds(100)));      // RF-223: expirou
    }
}
