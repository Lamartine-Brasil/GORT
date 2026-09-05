using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gort.Imaging;
using Gort.Loop;
using Gort.Persistence;
using Gort.Platform;
using Gort.Store;
using Gort.Translate;
using Gort.Update;
using Tomlyn.Model;

namespace Gort.Tests;

/// <summary>
/// Revisão profunda: cobre lacunas puras sem rede/disco real/Sleep.
/// Tudo determinístico (relógio injetado, MemoryStream, TomlTable em memória).
/// </summary>
public sealed class DeepReviewTests
{
    // ── TomlFile.GetDouble ──

    [Fact]
    public void GetDouble_Double_Long_String_Missing()
    {
        var t = new TomlTable { ["a"] = 1.5, ["b"] = 3L, ["c"] = "2.5", ["e"] = "" };
        Assert.Equal(1.5, TomlFile.GetDouble(t, "a", 9.9));
        Assert.Equal(3.0, TomlFile.GetDouble(t, "b", 9.9));
        Assert.Equal(2.5, TomlFile.GetDouble(t, "c", 9.9));
        Assert.Equal(0.0, TomlFile.GetDouble(t, "e", 9.9));      // RF-042 vazio→0
        Assert.Equal(9.9, TomlFile.GetDouble(t, "falta", 9.9));  // ausente→padrão
    }

    [Fact]
    public void GetDouble_String_Invalida_Mantem_Padrao()
    {
        var t = new TomlTable { ["x"] = "abc" };
        Assert.Equal(4.2, TomlFile.GetDouble(t, "x", 4.2));
    }

    [Fact]
    public void Load_Invalido_Volta_Padroes_Usaveis()
    {
        // Arquivo inválido ("null" puro): Load não devolve nulo nem quebra —
        // ou tabela vazia (padrões) ou Fresh (padrões). Era CS8619 com NRE
        // real no caminho sem Fresh.
        string path = Path.Combine(Path.GetTempPath(), "gort-null-" + Guid.NewGuid() + ".toml");
        try
        {
            File.WriteAllText(path, "null");
            var (raw, fresh) = TomlFile.Load(path);
            Assert.NotNull(raw);
            Assert.Equal("padrão", TomlFile.GetString(raw, "qualquer", "padrão"));
            Assert.True(fresh || raw.Count == 0);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // ── TomlFile.GetSchema / GetInt ──

    [Theory]
    [InlineData(0, 1, 1)]   // zero→atual
    [InlineData(-3, 1, 1)]  // negativo→atual
    [InlineData(2, 1, 2)]   // positivo→valor
    public void GetSchema_Bordas(long stored, int current, int expected)
    {
        var t = new TomlTable { ["schema_version"] = stored };
        Assert.Equal(expected, TomlFile.GetSchema(t, current));
    }

    [Fact]
    public void GetSchema_Ausente_Mantem_Atual()
    {
        Assert.Equal(7, TomlFile.GetSchema(new TomlTable(), 7));
    }

    [Fact]
    public void GetInt_String_Vazia_Vira_Zero()
    {
        var t = new TomlTable { ["v"] = "" };
        Assert.Equal(0, TomlFile.GetInt(t, "v", 5));
        Assert.Equal(5, TomlFile.GetInt(new TomlTable(), "v", 5));
    }

    // ── VersionFile.Parse ──

    [Fact]
    public void VersionFile_Parse_App_E_Dicts()
    {
        var vf = VersionFile.Parse("[app]\n{version}1.4.0\n{inline-min}1.2.0\n"
            + "{url-exe}https://x/y.exe\n{url-sum}https://x/y.sha\n"
            + "[dicts]\n{dict-ja}3 https://x/ja.txt\n{ruim sem chave\n{dict-xx}só-um-token\n");
        Assert.Equal("1.4.0", vf.Version);
        Assert.Equal("1.2.0", vf.InlineMin);
        Assert.Equal("https://x/y.exe", vf.ExeUrl);
        Assert.Single(vf.Dicts);
        Assert.Equal(("3", "https://x/ja.txt"), vf.Dicts["ja"]);
    }

    [Fact]
    public void VersionFile_Parse_Vazio_Nao_Quebra()
    {
        var vf = VersionFile.Parse("");
        Assert.Equal("", vf.Version);
        Assert.Empty(vf.Dicts);
    }

    // ── VersionFile.IsMinor / Compare ──

    [Theory]
    [InlineData("1.2.5", "1.4.0", "1.2.0", true)]
    [InlineData("1.4.0", "1.4.0", "1.2.0", false)]  // sem novidade
    [InlineData("1.5.0", "1.4.0", "", false)]       // local mais novo
    [InlineData("1.0.0", "1.4.0", "1.2.0", false)]  // abaixo do mínimo
    [InlineData("1.2.0", "1.2.1", "", true)]        // sem mínimo = menor vale
    [InlineData("1.2.0", "2.0.0", "1.2.0", true)]
    public void IsMinor_Tabela(string local, string remote, string min, bool expected)
    {
        Assert.Equal(expected, VersionFile.IsMinor(local, remote, min));
    }

    [Theory]
    [InlineData("http://x/y.exe", "https://x/y.exe")]
    [InlineData("https://x/y.exe", "https://x/y.exe")]
    [InlineData("nota-url", "nota-url")]
    public void ForceHttps_Tabela(string entrada, string esperado)
    {
        Assert.Equal(esperado, VersionFile.ForceHttps(entrada));
    }

    // ── PipeFraming.Encode / ReadAsync ──

    [Fact]
    public void PipeFraming_Vazio_Tem_So_Cabecalho()
    {
        byte[] msg = PipeFraming.Encode("");
        Assert.Equal(2, msg.Length);
        Assert.Equal(0, (msg[0] << 8) | msg[1]);
    }

    [Fact]
    public void PipeFraming_Cabecalho_E_BigEndian()
    {
        byte[] msg = PipeFraming.Encode("olá");
        int n = (msg[0] << 8) | msg[1];
        Assert.Equal(Encoding.Unicode.GetByteCount("olá"), n);
        Assert.Equal(2 + n, msg.Length);
    }

    [Fact]
    public void PipeFraming_Trunca_Em_65535()
    {
        byte[] msg = PipeFraming.Encode(new string('x', 70000));
        int n = (msg[0] << 8) | msg[1];
        Assert.Equal(PipeFraming.MaxBytes, n);
        Assert.Equal(2 + PipeFraming.MaxBytes, msg.Length);
    }

    [Fact]
    public async Task PipeFraming_RoundTrip_MemoryStream()
    {
        const string texto = "Olá, mundo — 日本語";
        byte[] msg = PipeFraming.Encode(texto);
        using var ms = new MemoryStream(msg);
        string volta = await PipeFraming.ReadAsync(ms, CancellationToken.None);
        Assert.Equal(texto, volta);
    }

    // ── DisplayMemory ──

    [Fact]
    public void DisplayMemory_Empilha_Recente_Primeiro_E_Limita_N()
    {
        var mem = new DisplayMemory(() => 2, () => 60);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("um", mem.Apply("um", t0));
        Assert.Equal("dois\n\n\num", mem.Apply("dois", t0.AddSeconds(1)));
        Assert.Equal("três\n\n\ndois", mem.Apply("três", t0.AddSeconds(2)));
    }

    [Fact]
    public void DisplayMemory_Expira_Apos_Segundos()
    {
        var mem = new DisplayMemory(() => 5, () => 10);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        mem.Apply("velha", t0);
        string ainda = mem.Apply("", t0.AddSeconds(9));
        Assert.Contains("velha", ainda);
        string fora = mem.Apply("", t0.AddSeconds(11));
        Assert.DoesNotContain("velha", fora);
    }

    [Fact]
    public void DisplayMemory_Vazio_So_Mostra_Vivas()
    {
        var mem = new DisplayMemory(() => 5, () => 60);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal("", mem.Apply("", t0));
    }

    // ── ChangeTracker fronteiras P-47 ──

    [Fact]
    public void ChangeTracker_P47_Exato_Repinta_So_Camada()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tr = new ChangeTracker();
        var d0 = tr.Step("abc", t0, overlayOrLayer: true);
        Assert.True(d0.FullPath);

        var antes = tr.Step("abc", t0.AddMilliseconds(999), overlayOrLayer: true);
        Assert.False(antes.FullPath);
        Assert.False(antes.RepaintIdle);

        var exato = tr.Step("abc", t0.AddMilliseconds(1000), overlayOrLayer: true);
        Assert.False(exato.FullPath);
        Assert.True(exato.RepaintIdle);

        var tr2 = new ChangeTracker();
        tr2.Step("abc", t0, overlayOrLayer: false);
        var semModo = tr2.Step("abc", t0.AddHours(1), overlayOrLayer: false);
        Assert.False(semModo.RepaintIdle);  // fora de camada/sobreposição nunca repinta
    }

    [Fact]
    public void ChangeTracker_Vazio_Sempre_Caminho_Completo()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var tr = new ChangeTracker();
        Assert.True(tr.Step("", t0, overlayOrLayer: true).FullPath);
        Assert.True(tr.Step("", t0.AddHours(1), overlayOrLayer: true).FullPath);
    }

    // ── OverlayReuseCache ──

    [Fact]
    public void ReuseCache_Identico_Reusa_Diferente_Recria()
    {
        var c = new OverlayReuseCache();
        var a = new ScreenRect(0, 0, 100, 50);
        var cl = new ScreenRect(0, 0, 100, 50);
        Assert.False(c.ReuseOrStore(0, a, cl, "ocr", "tr"));
        Assert.True(c.ReuseOrStore(0, a, cl, "ocr", "tr"));
        Assert.False(c.ReuseOrStore(0, a, cl, "ocr", "outra"));
        Assert.Equal(1, c.Count);
    }

    [Fact]
    public void ReuseCache_Area_Mudou_Nao_Reusa_E_Prune_Limpa()
    {
        var c = new OverlayReuseCache();
        var cl = new ScreenRect(0, 0, 10, 10);
        Assert.False(c.ReuseOrStore(0, new ScreenRect(0, 0, 10, 10), cl, "o", "t"));
        Assert.False(c.ReuseOrStore(0, new ScreenRect(5, 0, 10, 10), cl, "o", "t"));
        Assert.False(c.ReuseOrStore(1, new ScreenRect(0, 0, 10, 10), cl, "o", "t"));
        Assert.Equal(2, c.Count);
        c.Prune(new System.Collections.Generic.HashSet<int> { 0 });
        Assert.Equal(1, c.Count);
    }

    // ── TokenHelper ──

    [Theory]
    [InlineData("abcdefg", true, "defg")]   // ≥7 tira 3
    [InlineData("abcdef", true, "cdef")]    // 6 tira 2
    [InlineData("abcde", true, "abcde")]    // ≤5 mantém
    [InlineData("abcdefg", false, "abcdefg")]
    public void Shorten_Tabela(string token, bool adv, string esperado)
    {
        Assert.Equal(esperado, TokenHelper.Shorten(token, adv));
    }

    [Fact]
    public void CleanParts_Remove_Pontas_E_Descarta_Vazias()
    {
        var out1 = TokenHelper.CleanParts(new[] { "@@a@@", "@@@@", "b" }, "@tk", true);
        Assert.Equal(new[] { "a", "b" }, out1);
        var out2 = TokenHelper.CleanParts(new[] { "@@a@@", "b" }, "@tk", false);
        Assert.Equal(new[] { "@@a@@", "b" }, out2);
    }

    // ── WebFree ParseResponse / BuildUrl ──

    [Fact]
    public void WebFree_Parse_Junta_Segmentos_Com_Espaco()
    {
        const string json = """[[["Olá","Hello",null,null,1],["mundo","world",null,null,1]],null,"en"]""";
        Assert.Equal("Olá mundo", WebFreeTranslator.ParseResponse(json));
    }

    [Fact]
    public void WebFree_Parse_Pula_Segmento_Sem_Texto()
    {
        const string json = """[[["Ok","Ok",null,null,1],[42,"x"],null],null,"en"]""";
        Assert.Equal("Ok", WebFreeTranslator.ParseResponse(json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[\"só-string\"]")]
    public void WebFree_Parse_Invalido_Lanca(string json)
    {
        Assert.Throws<InvalidOperationException>(() => WebFreeTranslator.ParseResponse(json));
    }

    [Fact]
    public void WebFree_BuildUrl_Escapa_Texto_E_Codigos()
    {
        string url = WebFreeTranslator.BuildUrl("olá & adeus", "en", "pt-BR", "gtx");
        Assert.Contains("tl=pt-BR", url);
        Assert.Contains("sl=en", url);
        Assert.Contains("q=ol%C3%A1%20%26%20adeus", url);
    }

    // ── ConfigService.BuildExportText (só cabeçalho, sem depender de disco) ──

    [Fact]
    public void BuildExportText_Cabecalho_Traz_Modo_E_Servico()
    {
        var cfg = new ConfigService();
        string txt = cfg.BuildExportText();
        Assert.StartsWith("# GORT", txt);
        Assert.Contains("# window_mode = " + cfg.Profile.WindowMode, txt);
        Assert.Contains("# translator = " + cfg.Profile.TranslationService, txt);
    }
}

/// <summary>
/// Fase 1: a saída (escuro/camada) nunca entra no OCR. Prova que o laço
/// apaga da captura os retângulos informados pelo sink — mesmo onde a
/// afinidade do Windows falhar. Puro, sem UI/rede.
/// </summary>
public sealed class SelfCaptureTests
{
    private sealed class BareSink : Gort.Loop.IDisplaySink
    {
        public bool IsAlive => true;
        public void Draw(string display, string recognized) { }
        public void Repaint() { }
        public void SetRunning(bool running) { }
    }

    private static RegionImage White(int w, int h, int ch = 4)
    {
        var b = new byte[w * h * ch];
        for (int i = 0; i < b.Length; i++) b[i] = 255;
        return new RegionImage { Width = w, Height = h, Channels = ch, Bytes = b };
    }

    private static bool IsBlack(byte[] b, int x, int y, int w) =>
        b[(y * w + x) * 4] == 0 && b[(y * w + x) * 4 + 1] == 0 && b[(y * w + x) * 4 + 2] == 0;

    private static bool IsWhite(byte[] b, int x, int y, int w) =>
        b[(y * w + x) * 4] == 255 && b[(y * w + x) * 4 + 1] == 255 && b[(y * w + x) * 4 + 2] == 255;

    [Fact]
    public void Sink_Padrao_Sem_Oclusores()
    {
        // Quem não informa (sobreposição: coincide com a fonte) não apaga nada.
        Gort.Loop.IDisplaySink sink = new BareSink();
        Assert.Empty(sink.OutputOccluders());
    }

    [Fact]
    public void Blackout_Apaga_So_A_Intersecao_E_Mantem_Alfa()
    {
        var img = White(4, 2);
        var area = new ScreenRect(10, 10, 4, 2);
        TranslationLoop.BlackoutOutput(img, area,
            new List<ScreenRect> { new(12, 10, 2, 2) });
        for (int y = 0; y < 2; y++)
        {
            Assert.True(IsWhite(img.Bytes, 0, y, 4));
            Assert.True(IsWhite(img.Bytes, 1, y, 4));
            Assert.True(IsBlack(img.Bytes, 2, y, 4));
            Assert.True(IsBlack(img.Bytes, 3, y, 4));
        }
        // Alfa opaco preservado (o pipeline conta com P-107).
        for (int i = 3; i < img.Bytes.Length; i += 4)
            Assert.Equal(255, img.Bytes[i]);
    }

    [Fact]
    public void Blackout_Sem_Oclusores_Nao_Toca()
    {
        var img = White(3, 3);
        var before = (byte[])img.Bytes.Clone();
        TranslationLoop.BlackoutOutput(img, new ScreenRect(0, 0, 3, 3),
            new List<ScreenRect>());
        Assert.Equal(before, img.Bytes);
    }

    [Fact]
    public void Blackout_Fora_Da_Area_Nao_Toca()
    {
        var img = White(3, 3);
        var before = (byte[])img.Bytes.Clone();
        TranslationLoop.BlackoutOutput(img, new ScreenRect(0, 0, 3, 3),
            new List<ScreenRect> { new(10, 10, 5, 5) });
        Assert.Equal(before, img.Bytes);
    }

    [Fact]
    public void Blackout_Fora_De_4_Canais_Nao_Age()
    {
        var img = White(3, 3, ch: 1);
        var before = (byte[])img.Bytes.Clone();
        TranslationLoop.BlackoutOutput(img, new ScreenRect(0, 0, 3, 3),
            new List<ScreenRect> { new(0, 0, 3, 3) });
        Assert.Equal(before, img.Bytes);
    }

    [Fact]
    public void Blackout_Original_Acompanha_Nas_Mesmas_Dimensoes()
    {
        var raw = new byte[4 * 2 * 4];
        for (int i = 0; i < raw.Length; i++) raw[i] = 255;
        var img = new RegionImage
        {
            Width = 4, Height = 2, Channels = 4, Bytes = (byte[])raw.Clone(),
            OrigWidth = 4, OrigHeight = 2, OrigBytes = raw,
        };
        var area = new ScreenRect(10, 10, 4, 2);
        TranslationLoop.BlackoutOutput(img, area,
            new List<ScreenRect> { new(10, 10, 4, 2) });
        Assert.NotNull(img.OrigBytes);
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < 4; x++)
                Assert.True(IsBlack(img.OrigBytes, x, y, 4));
    }
}
