using System.IO;
using Gort.Config;
using Gort.Core;
using Gort.Store;

namespace Gort.Tests;

/// <summary>Critérios de aceite da Etapa 1 (cap. 10) + valores 🔒 da Parte IV.</summary>
public class Etapa1Tests
{
    private static string TempToml(string content)
    {
        var p = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".toml");
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Defaults_Match_PartIV_12()
    {
        var p = Profile.Defaults();
        Assert.Equal("overlay", p.WindowMode);
        Assert.Equal("web-free", p.TranslationService);
        Assert.Equal("modern", p.OcrEngine);
        Assert.Equal("en", p.OcrLanguage);
        Assert.Equal("pt-BR", p.TargetLanguage);
        Assert.Equal(2, p.Speed);
        Assert.Equal(2.0, p.Zoom);          // P-22 🔒
        Assert.Equal(127, p.Threshold);     // P-21
        Assert.True(p.UseDict);
        Assert.True(p.ShowOcrText);
        Assert.Equal(950, p.CloudMonthlyLimit);  // P-29 🔒
        Assert.Equal(15, p.FontSize);        // P-127 🔒
        Assert.Equal(300, Params.P05_Speed1Ms);  // 🔒
        Assert.Equal(1000, Params.P06_Speed2Ms);
        Assert.Equal(2500, Params.P09_Speed5Ms);
        Assert.Equal(250, Params.P04_HookWaitMs); // 🔒
    }

    [Fact]
    public void Speed_Maps_To_P05_P09()
    {
        Assert.Equal(300, Params.SpeedToInterval(1));
        Assert.Equal(1000, Params.SpeedToInterval(2));
        Assert.Equal(1500, Params.SpeedToInterval(3));
        Assert.Equal(2000, Params.SpeedToInterval(4));
        Assert.Equal(2500, Params.SpeedToInterval(5));
    }

    [Fact]
    public void RoundTrip_Preserves_State()
    {
        var svc = new ConfigService();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".toml");
        svc.Profile.Zoom = 3.5;
        svc.Profile.Speed = 1;
        svc.Profile.Areas.Add(new OcrArea { X = 10, Y = 20, W = 300, H = 100 });
        svc.SaveProfileTo(path);

        var svc2 = new ConfigService();
        svc2.LoadProfile(path, isMain: false);
        Assert.Equal(3.5, svc2.Profile.Zoom);
        Assert.Equal(1, svc2.Profile.Speed);
        Assert.Single(svc2.Profile.Areas);
        Assert.Equal(300, svc2.Profile.Areas[0].W);
    }

    [Fact]
    public void Partial_File_Opens_With_Defaults()
    {
        // RF: perfil com linhas removidas ainda abre.
        var path = TempToml("schema_version = 1\nspeed = 5\n");
        var svc = new ConfigService();
        svc.LoadProfile(path, isMain: false);
        Assert.Equal(5, svc.Profile.Speed);
        Assert.Equal("overlay", svc.Profile.WindowMode);
        Assert.Equal(2.0, svc.Profile.Zoom);
    }

    [Fact]
    public void Unknown_Keys_Survive_Rewrite()
    {
        // RF-038: chave de versão mais nova abre sem erro e é preservada.
        var path = TempToml("schema_version = 99\nfuture_key = \"keep-me\"\nspeed = 3\n");
        var svc = new ConfigService();
        svc.LoadProfile(path, isMain: false);
        svc.SaveProfileTo(path);
        var text = File.ReadAllText(path);
        Assert.Contains("future_key", text);
        Assert.Contains("keep-me", text);
    }

    [Fact]
    public void Out_Of_Range_Saturates()
    {
        // RF-042: saturar, nunca rejeitar.
        var path = TempToml("schema_version = 1\nthreshold = 999\nzoom = 99\n");
        var svc = new ConfigService();
        svc.LoadProfile(path, isMain: false);
        Assert.Equal(255, svc.Profile.Threshold);
        Assert.Equal(2.0, svc.Profile.Zoom);   // acima de 10 → padrão
    }

    [Fact]
    public void Explicit_Dict_Setting_Survives_Reload()
    {
        // Valor explícito do usuário não é revertido pela derivação (RF-044).
        var path = TempToml("schema_version = 1\nocr_lang = \"ja\"\ndict_by_word = true\nremove_spaces = false\n");
        var svc = new ConfigService();
        svc.LoadProfile(path, isMain: false);
        Assert.True(svc.Profile.DictByWord);
        Assert.False(svc.Profile.RemoveSpaces);
    }

    [Fact]
    public void Inverted_Ranges_Swap()
    {
        var g = new ColorGroup { S1 = 80, S2 = 10, V1 = 90, V2 = 5 };
        g.Normalize();
        Assert.True(g.S1 <= g.S2);
        Assert.True(g.V1 <= g.V2);
    }

    [Fact]
    public void DictByWord_Follows_Language_Property()
    {
        // RF-044/RF-148 🔒: japonês (sem separador) → off + remove espaços;
        // inglês → on, sem remoção.
        var ja = TempToml("schema_version = 1\nocr_lang = \"ja\"\n");
        var s1 = new ConfigService();
        s1.LoadProfile(ja, isMain: false);
        Assert.False(s1.Profile.DictByWord);
        Assert.True(s1.Profile.RemoveSpaces);

        var en = TempToml("schema_version = 1\nocr_lang = \"en\"\n");
        var s2 = new ConfigService();
        s2.LoadProfile(en, isMain: false);
        Assert.True(s2.Profile.DictByWord);
        Assert.False(s2.Profile.RemoveSpaces);
    }

    [Fact]
    public void Unknown_Ids_Fall_Back_With_Notice()
    {
        var path = TempToml("schema_version = 1\ntranslation_service = \"nope\"\nocr_engine = \"nope\"\n");
        var svc = new ConfigService();
        svc.LoadProfile(path, isMain: false);
        Assert.Equal("web-free", svc.Profile.TranslationService);
        Assert.Equal("modern", svc.Profile.OcrEngine);
        Assert.NotEmpty(svc.Notices);
    }

    [Fact]
    public void Catalogs_Are_Data_Not_Code()
    {
        // RF-566: acrescentar item não invalida perfis - ids desconhecidos caem p/ padrão.
        Assert.Contains(Catalogs.OcrEngines, c => c.Id == "modern");
        Assert.Contains(Catalogs.TranslationServices, c => c.Id == "web-free");
        Assert.Contains(Catalogs.WindowModes, c => c.Id == "overlay");
    }

    [Fact]
    public void WebQuality_DefaultsAuto_NormalizesUnknown()
    {
        // Google: padrão auto; desconhecido volta p/ auto; high/low preservam.
        var p = Profile.Defaults();
        Assert.Equal("auto", p.WebQuality);
        p.WebQuality = "banana";
        p.Normalize(out _);
        Assert.Equal("auto", p.WebQuality);
        p.WebQuality = "high";
        p.Normalize(out _);
        Assert.Equal("high", p.WebQuality);
    }

    [Fact]
    public void LayerMax_Negative_Saturates_RoundTrips()
    {
        var svc = new ConfigService();
        svc.Profile.LayerAutoFit = true;
        svc.Profile.LayerMaxW = 600;
        svc.Profile.LayerMaxH = -5;   // fora de faixa → 0
        svc.Profile.WebQuality = "low";
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".toml");
        svc.SaveProfileTo(path);

        var svc2 = new ConfigService();
        svc2.LoadProfile(path, isMain: false);
        Assert.True(svc2.Profile.LayerAutoFit);
        Assert.Equal(600, svc2.Profile.LayerMaxW);
        Assert.Equal(0, svc2.Profile.LayerMaxH);
        Assert.Equal("low", svc2.Profile.WebQuality);
    }
}
