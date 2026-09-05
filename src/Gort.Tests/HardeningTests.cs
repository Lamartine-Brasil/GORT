using System;
using System.Collections.Generic;
using System.IO;
using Gort.Imaging;
using Gort.Translate;

namespace Gort.Tests;

/// <summary>Etapa 19 — endurecimento (RF-552..567) e Parte VIII.</summary>
public class HardeningTests
{
    private static string SrcDir()
    {
        // Localiza a raiz pelo Gort.sln: funciona com bin/ local ou com
        // ArtifactsPath centralizado (releases/build/).
        string? dir = AppContext.BaseDirectory;
        while (dir is not null
            && !File.Exists(Path.Combine(dir, "src", "Gort.sln")))
            dir = Directory.GetParent(dir)?.FullName;
        if (dir is not null)
            return Path.Combine(dir, "src", "Gort");
        // Fallback: layout antigo src/Gort.Tests/bin/<cfg>/<tfm>/.
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Gort"));
    }

    [Fact]
    public void No_Forced_Gc_Anywhere()
    {
        // RF-380/RF-555: nenhuma coleta forçada no desenho nem no laço.
        foreach (var f in Directory.GetFiles(SrcDir(), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(f);
            Assert.DoesNotContain("GC.Collect", text);
        }
    }

    [Fact]
    public void No_Legacy_Code()
    {
        // RF-564: sem compatibilidade com produto anterior em lugar algum.
        foreach (var f in Directory.GetFiles(SrcDir(), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(f);
            Assert.DoesNotContain("legacy", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Fictitious_Language_Needs_Only_Data()
    {
        // RF-566/567: idioma novo = entrada na tabela, sem tocar o núcleo.
        var entry = new LangCodes.Entry
        {
            Key = "xx",
            Ocr = "xxx",
            NameEn = "Xx",
            Svc = new Dictionary<string, string> { ["web-free"] = "xx" },
        };
        LangCodes.All.Add(entry);
        try
        {
            Assert.Equal("xx", LangCodes.CodeFor("web-free", "xx"));
            Assert.Equal("xx", LangCodes.KeyForOcr("xxx"));
            Assert.False(LangCodes.IsRtl("xx"));
        }
        finally { LangCodes.All.Remove(entry); }
        Assert.Equal("", LangCodes.CodeFor("web-free", "xx"));
    }

    [Fact]
    public void Black_Frame_Detection()
    {
        Assert.True(ColorFilter.IsBlack(new byte[400], 4));        // RF-570
        var px = new byte[400];
        px[100] = 200;
        Assert.False(ColorFilter.IsBlack(px, 4));
        Assert.True(ColorFilter.IsBlack(new byte[100], 1));
    }

    [Fact]
    public void Cloud_Usage_Tolerates_Old_Format()
    {
        // Sem "v": carrega (RF-038 tolerante).
        var q = new Ocr.Cloud.CloudQuota();
        Assert.True(q.TryConsume("hard-" + Guid.NewGuid(), 950));
    }

    [Fact]
    public void Cloud_Usage_Rollover_And_Limit()
    {
        // Arquivo antigo (sem "v") com mes passado zera; limite barra.
        string file = System.IO.Path.Combine(
            Gort.Core.Paths.BaseDir, "cloud-usage.json");
        string? backup = null;
        try
        {
            Gort.Core.Paths.EnsureAll();
            if (System.IO.File.Exists(file))
                backup = System.IO.File.ReadAllText(file);
            var old = DateTime.UtcNow.AddMonths(-1);
            string content = "{\"k\":{\"used\":5,\"year\":"
                + old.Year + ",\"month\":" + old.Month + "}}";
            System.IO.File.WriteAllText(file, content);
            var q = new Ocr.Cloud.CloudQuota();
            Assert.Equal((0, 950), q.Status("k", 950));
            Assert.True(q.TryConsume("k", 950));
            Assert.Equal((1, 950), q.Status("k", 950));
            Assert.False(q.TryConsume("full", 0));
        }
        finally
        {
            try
            {
                if (backup is null) System.IO.File.Delete(file);
                else System.IO.File.WriteAllText(file, backup);
            }
            catch { }
        }
    }
}
