using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gort.Config;
using Gort.Core;
using Gort.Imaging;
using Gort.Loop;
using Gort.Persistence;
using Gort.Platform;
using Gort.Regions;
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

    [Fact]
    public void WebFree_Headers_Tem_Cara_De_Navegador()
    {
        // O endpoint gratuito barra sem User-Agent com 403 mesmo com cota.
        using var req = new System.Net.Http.HttpRequestMessage();
        WebFreeTranslator.ApplyHeaders(req);
        Assert.True(req.Headers.Contains("User-Agent"));
        string ua = string.Join(" ", req.Headers.GetValues("User-Agent"));
        Assert.StartsWith("Mozilla/5.0", ua);
        Assert.Contains("Chrome/", ua);
        Assert.True(req.Headers.Contains("Cache-Control"));
    }

    // ── Auditoria etapa 3 ──

    [Fact]
    public void CustomApi_ParseStandard_Sem_Aspas_E_Erro_String()
    {
        // ToString() em string devolvia JSON com aspas ("olá"→"\"olá\"") e
        // error:"0" virava "\"0\"" (sucesso virava falha).
        var ok = CustomApiService.ParseStandard("""{"error":"0","result":"olá"}""");
        Assert.Null(ok.Error);
        Assert.NotNull(ok.Translations);
        Assert.Equal("olá", ok.Translations[0]);
        var arr = CustomApiService.ParseStandard("""{"error":"0","result":["a","b"]}""");
        Assert.NotNull(arr.Translations);
        Assert.Equal("ab", arr.Translations[0]);
        var fail = CustomApiService.ParseStandard("""{"error":"1","message":"ruim"}""");
        Assert.Equal("ruim", fail.Error);
    }

    [Fact]
    public void CancelOrTimeout_Distingue_Usuario_De_Rede()
    {
        using var userCts = new CancellationTokenSource();
        userCts.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => HttpTranslator.CancelOrTimeout(userCts.Token));
        using var netCts = new CancellationTokenSource();
        var r = HttpTranslator.CancelOrTimeout(netCts.Token);
        Assert.NotNull(r.Error);
        Assert.Contains("Tempo esgotado", r.Error);
    }

    [Fact]
    public void ResultMemory_Cap_Limita_Na_Carga()
    {
        var big = new Dictionary<string, string>();
        for (int i = 0; i < Core.Params.P48_MemoryMaxEntries + 10; i++)
            big["k" + i] = "v";
        var capped = ResultMemory.Cap(big);
        Assert.Equal(Core.Params.P48_MemoryMaxEntries, capped.Count);
        Assert.True(capped.ContainsKey("k" + (Core.Params.P48_MemoryMaxEntries + 9)));
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

    // ── Auditoria: FailingColor, HsvToRgb, Fingerprint ──

    [Fact]
    public void FailingColor_Threshold_Sempre_Reprovada()
    {
        // A cor de preenchimento da exclusão nunca pode passar no filtro,
        // em nenhum limiar (o truncamento do Gray dava limiar-1).
        for (int t = 0; t <= 255; t++)
        {
            var (r, g, b) = Preprocess.FailingColor(FilterMode.Threshold,
                new List<(int, int, int, int, int, int, int)>(), t);
            Assert.False(ColorFilter.Passes(r, g, b, FilterMode.Threshold,
                new List<(int, int, int, int, int, int, int)>(), t));
        }
    }

    [Fact]
    public void HsvToRgb_Satura_Canais()
    {
        // s além de 255 (DeriveOutlines) gerava intermediário negativo e o
        // cast (byte) envolvia para ~248.
        // b+m dava -1,76 e o cast (byte) envolvia para 254.
        Assert.Equal(((byte)10, (byte)0, (byte)0), Preprocess.HsvToRgb(0, 300, 10));
        var (r, g, b) = Preprocess.HsvToRgb(0, 268, 100);
        Assert.InRange(r, 0, 255);
        Assert.InRange(g, 0, 255);
        Assert.InRange(b, 0, 255);
    }

    private static RegionManager.CapturePlan PlanoBase()
    {
        var plan = new RegionManager.CapturePlan();
        plan.Rects.Add(new ScreenRect(0, 0, 100, 50));
        plan.GroupsPerRect.Add(new List<int> { 0 });
        return plan;
    }

    [Fact]
    public void Fingerprint_Muda_Com_Exclusao_E_Cor()
    {
        // Editar exclusão ou cor com a tela parada tem que invalidar o
        // cache do laço (antes só surtia efeito ao mudar a imagem).
        var adv = new AdvancedOptions();
        var p1 = Profile.Defaults();
        int base1 = TranslationLoop.Fingerprint(p1, PlanoBase(), false, adv, false, false);
        var planEx = PlanoBase();
        planEx.Exclusions.Add(new ScreenRect(10, 10, 20, 20));
        Assert.NotEqual(base1, TranslationLoop.Fingerprint(p1, planEx, false, adv, false, false));
        var p2 = Profile.Defaults();
        p2.ColorGroups[0].R = (p2.ColorGroups[0].R + 1) % 256;
        Assert.NotEqual(base1, TranslationLoop.Fingerprint(p2, PlanoBase(), false, adv, false, false));
        // Idêntico continua igual (sem invalidação espúria).
        Assert.Equal(base1, TranslationLoop.Fingerprint(Profile.Defaults(), PlanoBase(), false, adv, false, false));
    }

    [Fact]
    public void TranslationFingerprint_Muda_Com_Servico_E_Par()
    {
        // Trocar de serviço com a tela parada tem que retraduzir (antes a
        // troca "não pegava" até o texto do jogo mudar).
        var adv = new AdvancedOptions();
        var p1 = Profile.Defaults();
        int base1 = TranslationLoop.TranslationFingerprint(p1, adv);
        var p2 = Profile.Defaults();
        p2.TranslationService = "db";
        Assert.NotEqual(base1, TranslationLoop.TranslationFingerprint(p2, adv));
        var p3 = Profile.Defaults();
        p3.TargetLanguage = "en";
        Assert.NotEqual(base1, TranslationLoop.TranslationFingerprint(p3, adv));
        var p4 = Profile.Defaults();
        p4.ServiceSource["web-free"] = "ja";
        Assert.NotEqual(base1, TranslationLoop.TranslationFingerprint(p4, adv));
        var p5 = Profile.Defaults();
        p5.OcrLanguage = "ja";   // origem global do par quando sem mapa
        Assert.NotEqual(base1, TranslationLoop.TranslationFingerprint(p5, adv));
        Assert.Equal(base1, TranslationLoop.TranslationFingerprint(Profile.Defaults(), new AdvancedOptions()));
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

/// <summary>
/// Auditoria etapa 1: chaves que morriam no save agora fazem round-trip;
/// backup, schema, caixa e limites. Só arquivos temporários.
/// </summary>
public sealed class PersistenceAuditTests
{
    private static string Temp() =>
        Path.Combine(Path.GetTempPath(), "gort-audit-" + Guid.NewGuid() + ".toml");

    [Fact]
    public void ProfileRoundTrip_Chaves_Antes_Mortas()
    {
        string path = Temp();
        try
        {
            var svc = new ConfigService();
            var p = svc.Profile;
            p.ClassicDataset = "jpn"; p.ClassicFast = true;
            p.SaveResultFile = true; p.CopyToClipboard = true; p.CopyFormat = "both";
            p.Erode = true; p.TextOrder = "center"; p.TextBackground = false;
            p.AreaNumbering = true; p.CaptureActiveWindow = true;
            p.Tts = true; p.TtsWait = true;
            p.LayerX = 10; p.LayerY = 20; p.LayerW = 300; p.LayerH = 200;
            p.TextColor = [1, 2, 3]; p.BgColor = [4, 5, 6, 7];
            p.ServiceSource["web-free"] = "en"; p.ServiceTarget["web-free"] = "pt-BR";
            p.BgTransparency = true;
            svc.SaveProfileTo(path);

            var svc2 = new ConfigService();
            svc2.LoadProfile(path, isMain: false);
            var q = svc2.Profile;
            Assert.Equal("jpn", q.ClassicDataset);
            Assert.True(q.ClassicFast);
            Assert.True(q.SaveResultFile);
            Assert.True(q.CopyToClipboard);
            Assert.Equal("both", q.CopyFormat);
            Assert.True(q.Erode);
            Assert.Equal("center", q.TextOrder);
            Assert.False(q.TextBackground);
            Assert.True(q.AreaNumbering);
            Assert.True(q.CaptureActiveWindow);
            Assert.True(q.Tts);
            Assert.True(q.TtsWait);
            Assert.Equal(10, q.LayerX); Assert.Equal(20, q.LayerY);
            Assert.Equal(300, q.LayerW); Assert.Equal(200, q.LayerH);
            Assert.Equal(new byte[] { 1, 2, 3 }, q.TextColor);
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, q.BgColor);
            Assert.Equal("en", q.ServiceSource["web-free"]);
            Assert.Equal("pt-BR", q.ServiceTarget["web-free"]);
            Assert.True(q.BgTransparency);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void AdvancedRoundTrip_Campos_Antes_Mortos()
    {
        string path = Temp();
        try
        {
            var svc = new ConfigService();
            var a = svc.Advanced;
            a.RightToLeft = true; a.RemoteAlwaysOnTop = true;
            a.FollowCompat = true; a.FollowOnly = false;
            a.AttachedYellowBorder = true;
            a.SelectBg = "#FF112233"; a.SelectAccent = "#FF445566";
            a.OpenProfile[0] = new OpenProfileShortcut { Keys = "Ctrl+1", File = "a.toml" };
            a.ToggleForcedTransparency = "Ctrl+T";
            a.ServiceSwitch["db"] = "Ctrl+D";
            a.OverlayBgAlpha = true; a.DarkFont = "Arial";
            a.LayerBottom = true; a.LayerRight = true;
            a.CollectActive.Add("x.txt"); a.CollectAsDb = false; a.CollectIgnoreCase = false;
            a.CustomPresets.Add(new CustomPreset { Name = "p1", Url = "http://x", ReqTemplate = "{}", ResTemplate = "{}" });
            a.CustomPresets.Add(new CustomPreset { Name = "arq", Url = "http://f", FromFile = true });
            a.CustomSameCodes = false; a.CustomSource = "ja"; a.CustomTarget = "en";
            a.CustomUrl = "http://h:1/t"; a.LlmInstruction = "seja breve";
            a.LlmCustomModel = "m-x"; a.LlmNoDefault = true; a.LlmPreset = "eco";
            a.LlmTemp = 42; a.LlmReason = 2; a.LlmMaxOut = 5000;
            a.ClipboardTranslate = true; a.ClipboardShowOriginal = true;
            a.ClipboardShowWorking = true; a.ClipboardCopyFormat = "both";
            a.CloudPriority = true; a.SnapshotStaySec = 7;
            svc.SaveAdvancedTo(path);

            var svc2 = new ConfigService();
            svc2.LoadAdvancedFrom(path);
            var b = svc2.Advanced;
            Assert.True(b.RightToLeft);
            Assert.True(b.RemoteAlwaysOnTop);
            Assert.True(b.FollowCompat);
            Assert.False(b.FollowOnly);
            Assert.True(b.AttachedYellowBorder);
            Assert.Equal("#FF112233", b.SelectBg);
            Assert.Equal("Ctrl+1", b.OpenProfile[0].Keys);
            Assert.Equal("a.toml", b.OpenProfile[0].File);
            Assert.Equal("Ctrl+T", b.ToggleForcedTransparency);
            Assert.Equal("Ctrl+D", b.ServiceSwitch["db"]);
            Assert.True(b.OverlayBgAlpha);
            Assert.Equal("Arial", b.DarkFont);
            Assert.True(b.LayerBottom);
            Assert.Contains("x.txt", b.CollectActive);
            Assert.False(b.CollectAsDb);
            Assert.Single(b.CustomPresets);   // o de arquivo não persiste (fonte é o disco)
            Assert.Equal("p1", b.CustomPresets[0].Name);
            Assert.Equal("{}", b.CustomPresets[0].ReqTemplate);
            Assert.False(b.CustomSameCodes);
            Assert.Equal("ja", b.CustomSource);
            Assert.Equal("http://h:1/t", b.CustomUrl);
            Assert.Equal("seja breve", b.LlmInstruction);
            Assert.Equal("eco", b.LlmPreset);
            Assert.Equal(42, b.LlmTemp);
            Assert.Equal(2, b.LlmReason);
            Assert.Equal(5000, b.LlmMaxOut);
            Assert.True(b.ClipboardTranslate);
            Assert.Equal("both", b.ClipboardCopyFormat);
            Assert.True(b.CloudPriority);
            Assert.Equal(7, b.SnapshotStaySec);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Corrompido_Sinaliza_E_Perfil_Futuro_Preserva_Schema()
    {
        string bad = Temp(), bak = bad + ".bak";
        string v99 = Temp(), v99b = Temp();
        try
        {
            File.WriteAllText(bad, "{{{inválido");
            var (raw, fresh, corrupt) = TomlFile.LoadEx(bad);
            Assert.True(corrupt);
            Assert.NotNull(raw);

            File.WriteAllText(v99, "schema_version = 99\nspeed = 2\n");
            var svc = new ConfigService();
            svc.LoadProfile(v99, isMain: false);
            Assert.Equal(99, svc.Profile.LoadedSchema);
            Assert.Equal(2, svc.Profile.Speed);
            svc.SaveProfileTo(v99b);
            Assert.Contains("schema_version = 99", File.ReadAllText(v99b));
        }
        finally
        {
            foreach (var f in new[] { bad, bak, v99, v99b })
                try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void Caixa_Idioma_E_Normalizada()
    {
        Assert.Equal("en", LanguageTable.Find("EN")?.Key);
        Assert.Equal("pt-BR", LanguageTable.Find("pt-br")?.Key);
        Assert.Equal("layer", Catalogs.CanonicalId(Catalogs.WindowModes, "Layer"));
        var p = Profile.Defaults();
        p.WindowMode = "Layer"; p.OcrLanguage = "EN"; p.TargetLanguage = "pt-br";
        p.Normalize(out var notices, deriveLangDefaults: false);
        Assert.Equal("layer", p.WindowMode);
        Assert.Equal("en", p.OcrLanguage);
        Assert.Equal("pt-BR", p.TargetLanguage);
        Assert.DoesNotContain(notices, n => n.Contains("window_mode"));
    }

    [Fact]
    public void Normalize_Limita_Avancado_E_Avisa_Cor()
    {
        var a = new AdvancedOptions
        {
            LlmTemp = 999, LlmReason = 9, LlmMaxOut = 1, SnapshotStaySec = -5,
            LlmPreset = "x", ClipboardCopyFormat = "x", SelectBg = "zzz",
        };
        a.Normalize();
        Assert.Equal(100, a.LlmTemp);
        Assert.Equal(3, a.LlmReason);
        Assert.Equal(500, a.LlmMaxOut);
        Assert.Equal(0, a.SnapshotStaySec);
        Assert.Equal("default", a.LlmPreset);
        Assert.Equal("ocr-only", a.ClipboardCopyFormat);
        Assert.Equal("#FFFFFFFF", a.SelectBg);

        var p = Profile.Defaults();
        p.TextColor = [1, 2];
        p.Normalize(out var notices, deriveLangDefaults: false);
        Assert.Equal(new byte[] { 255, 255, 255 }, p.TextColor);
        Assert.Contains(notices, n => n.Contains("text_color"));
    }

    [Fact]
    public void GetInt_Overflow_E_Double_Truncam()
    {
        var t = new TomlTable { ["big"] = long.MaxValue, ["frac"] = 2.9, ["s"] = "" };
        Assert.Equal(int.MaxValue, TomlFile.GetInt(t, "big", 0));
        Assert.Equal(2, TomlFile.GetInt(t, "frac", 0));
        Assert.Equal(5, TomlFile.GetInt(t, "falta", 5));
        Assert.Equal(0, TomlFile.GetInt(t, "s", 5));   // RF-042 vazio→0 mantido
    }

    [Fact]
    public void CredFile_Higieniza_E_Marker_No_Base()
    {
        Assert.EndsWith("creds-commercial-kr.toml", Paths.CredFile("commercial-kr"));
        Assert.EndsWith("creds-custom.toml", Paths.CredFile("../evil"));
        Assert.EndsWith("creds-custom.toml", Paths.CredFile(""));
        Assert.StartsWith(Paths.BaseDir, Paths.MultiInstanceMarker);
    }
}

/// <summary>
/// Auditoria etapa 4: interruptor do segue-mouse e retângulos vazios.
/// Puros, sem SO.
/// </summary>
public sealed class RegionsAuditTests
{
    [Fact]
    public void FollowOnly_Sincroniza_Do_Advanced()
    {
        // O interruptor "somente mouse" não tinha efeito (nunca propagado).
        var cfg = new ConfigService();
        var mgr = new RegionManager(cfg);
        cfg.Advanced.FollowOnly = false;
        mgr.BuildPlan();
        Assert.False(mgr.FollowOnly);
        Assert.False(mgr.CanTranslate(out _));
    }

    [Fact]
    public void Areas_Vazias_Rejeitadas()
    {
        var cfg = new ConfigService();
        var mgr = new RegionManager(cfg);
        Assert.Null(mgr.AddArea(new ScreenRect(0, 0, 0, 0), exclusion: false));
        mgr.SetQuick(new ScreenRect(0, 0, -1, 5));
        Assert.Null(mgr.QuickArea);
        mgr.SetSnapshot(new ScreenRect(0, 0, 10, 0));
        Assert.Null(mgr.SnapshotArea);
    }
}
