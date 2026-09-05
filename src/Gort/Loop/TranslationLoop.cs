using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gort.Config;
using Gort.Core;
using Gort.Imaging;
using Gort.Ocr;
using Gort.Platform;
using Gort.Regions;
using Gort.Store;
using Gort.Text;
using Gort.Translate;

namespace Gort.Loop;

/// <summary>
/// Ciclo de tradução (cap. 8): captura → pré-processa → OCR → agrupa →
/// trata → traduz → desenha, com detecção de mudança (cap. 16) e esperas
/// interrompíveis (P-125/P-126). Tudo síncrono na thread do laço (RF-009).
/// </summary>
public sealed class TranslationLoop : ILoopBody
{
    private readonly ConfigService _cfg;
    private readonly RegionManager _regions;
    private readonly TranslationPipeline _pipe;
    private readonly IDisplaySink _sink;
    private readonly ILoopEffects _effects;
    private readonly DictionaryStore _dict = new();
    private ChangeTracker _change = new();
    private DateTime _cycleStart = DateTime.UtcNow;
    private List<List<string>> _lastTreated = new();
    // Descarte por imagem: hash FNV da captura por área. Tela parada pula
    // pré-processo + OCR (o gargalo, com ONNX usando vários núcleos).
    // ChangeTracker textual continua decidindo o redesenho (RF-193).
    private readonly Dictionary<int, ulong> _lastHash = new();
    private readonly Dictionary<int, (Text.RegionText? Region, List<Text.Block> Blocks)> _lastKept = new();
    private int _lastFingerprint;
    private bool _hasFingerprint;
    /// <summary>Avisos à UI (fundo preto — RF-570).</summary>
    public Action<string>? Notice { get; set; }
    private int _blackStreak;

    public TranslationLoop(ConfigService cfg, RegionManager regions,
        TranslationPipeline pipe, IDisplaySink sink, ILoopEffects effects)
    {
        _cfg = cfg; _regions = regions; _pipe = pipe; _sink = sink; _effects = effects;
    }

    public void ReloadDict() =>
        _dict.Load(Path.Combine(Paths.DictDir, _cfg.Profile.DictFile));

    public void Begin(LoopMode mode)
    {
        _change = new();                                     // RF-199
        _lastTreated = new();
        _blackStreak = 0;
        if (_cfg.Profile.UseDict) ReloadDict();
        if (mode == LoopMode.Continuous && !_regions.SnapshotArea.HasValue)
            _regions.BeginNonSnapshotTranslation();          // RF-071
        // RF-072: o plano é remontado a cada ciclo a partir do estado atual.
        _cycleStart = DateTime.UtcNow.AddMilliseconds(-Params.SpeedToInterval(_cfg.Profile.Speed));
        _sink.SetRunning(true);
    }

    public bool Step(LoopContext ctx)
    {
        var p = _cfg.Profile;
        try
        {
            // Passo 5: espera o intervalo em fatias de 100 ms (P-125),
            // salvo "destravar velocidade" da depuração (RF-491).
            int interval = Params.SpeedToInterval(p.Speed);
            if (!Debug.DebugFlags.UnlockSpeed)
            {
                while (!ctx.StopRequested)
                {
                    double elapsed = (DateTime.UtcNow - _cycleStart).TotalMilliseconds;
                    if (elapsed >= interval) break;
                    Thread.Sleep((int)Math.Min(Params.P125_IdleSleepMs, interval - elapsed));
                }
            }
            if (ctx.StopRequested) { _sink.SetRunning(false); return false; }
            _cycleStart = DateTime.UtcNow;

            if (!_sink.IsAlive)                              // passo 6
                return EndOfCycle(ctx, running: true);

            var plan = _regions.BuildPlan();
            // Oculta as janelas próprias que intersectam as áreas durante as
            // capturas (multiplataforma; no Windows é no-op — C8 resolve).
            using var conceal = ConcealForCapture(plan.Rects);
            bool overlay = p.WindowMode == "overlay" || p.WindowMode == "replace";
            var flatBlocks = new List<(int Area, string Text)>();
            var areaCounts = new List<int>();
            var areaTexts = new List<string>();
            var treatedPerArea = new List<List<string>>();
            var regionsKept = new List<Text.RegionText?>();
            var blocksKept = new List<List<Text.Block>>();
            var clientPerArea = new List<(bool Gone, int CX, int CY)>();
            // RF-098: original só com sobreposição + cor automática.
            bool needOrig = overlay && p.AutoColorMaster
                && (p.AutoColorFg || p.AutoColorBg);
            var origKept = new List<Imaging.RegionImage?>();
            // Qualquer mudança de config/áreas invalida os hashes (full pass).
            int fingerprint = Fingerprint(p, plan, needOrig, _cfg.Advanced,
                overlay, Debug.DebugFlags.OneLinePerBlock);
            if (!_hasFingerprint || fingerprint != _lastFingerprint)
            {
                _lastHash.Clear();
                _lastKept.Clear();
                _lastFingerprint = fingerprint;
                _hasFingerprint = true;
            }
            for (int r = 0; r < plan.Rects.Count; r++)
            {
                if (ctx.StopRequested) { _sink.SetRunning(false); return false; }
                var rect = plan.Rects[r];
                var (img, client) = CaptureArea(r, rect, needOrig, p, ctx);  // passo 7
                if (ctx.StopRequested) { _sink.SetRunning(false); return false; }
                if (img is null && client.Gone) { _sink.SetRunning(false); return false; }  // RF-097
                clientPerArea.Add(client);
                if (img is null)   // P8: área sem imagem é pulada
                {
                    areaCounts.Add(0); areaTexts.Add("");
                    treatedPerArea.Add(new List<string>());
                    regionsKept.Add(null); blocksKept.Add(new List<Text.Block>());
                    origKept.Add(null);
                    continue;
                }
                origKept.Add(needOrig ? img : null);
                // Fase 1: a saída (escuro/camada) nunca entra no OCR — apaga da
                // captura os retângulos do sink, mesmo onde a afinidade do
                // Windows falhar. Sem oclusores, sem custo.
                BlackoutOutput(img, rect, _sink.OutputOccluders());
                List<string>? treated = null;
                Text.RegionText? keptRegion = null;
                List<Text.Block> keptBlocks = new();
                // RF-570 conta todo ciclo (antes do descarte): preto estático
                // também precisa chegar a 3 para sugerir modo janela.
                if (Imaging.ColorFilter.IsBlack(img.Bytes, img.Channels))
                {
                    if (++_blackStreak == 3)
                        Notice?.Invoke("A captura devolve quadros pretos. " +
                            "Use o jogo em modo janela ou janela sem borda; " +
                            "tela cheia exclusiva não funciona.");
                }
                else _blackStreak = 0;
                // Tela parada: reusa tratado + geometria anteriores, sem
                // pré-processo nem OCR. O ChangeTracker abaixo descarta
                // (texto igual) salvo repintar ocioso.
                if (r < _lastTreated.Count
                    && _lastHash.TryGetValue(r, out ulong prev)
                    && prev == ImageHash(img.Bytes)
                    && _lastKept.TryGetValue(r, out var kprev))
                {
                    treated = new List<string>(_lastTreated[r]);
                    keptRegion = kprev.Region;
                    keptBlocks = kprev.Blocks;
                }
                else
                {
                    _lastHash[r] = ImageHash(img.Bytes);
                    var proc = Preprocess.Run(img, LocalExclusions(rect, plan.Exclusions),   // passo 8
                        FilterModeOf(p.ColorFilter), Tuples(plan.GroupsPerRect[r]),
                        p.Threshold, p.Erode, p.Zoom, p.PreprocessOff);

                    var ocr = RunOcr(proc, p, ctx);              // passos 9–10
                    if (ocr is null)                             // RF-205: reusa anterior
                    {
                        treated = r < _lastTreated.Count
                            ? new List<string>(_lastTreated[r]) : new List<string>();
                    }
                else if (ocr.Error is not null)
                {
                    treated = new List<string> { ocr.Error };  // erro vira conteúdo
                    Debug.DebugLog.Message("OCR: " + ocr.Error);
                }
                else
                {
                    keptRegion = Lines.Build(ocr, r, plan.IsSnapshot);
                    bool oneLine = Debug.DebugFlags.OneLinePerBlock;   // RF-491/157
                    keptBlocks = Grouping.Group(keptRegion.Lines,
                        overlay ? p.MergeLinesOverlay : true, p.RemoveSpaces,
                        oneLinePerBlock: oneLine);
                    treated = new List<string>();
                    foreach (var b in keptBlocks)
                        treated.Add(TextPipeline.ForTranslation(b, p.RemoveSpaces, _dict,
                            p.UseDict, p.DictByWord, _cfg.Advanced.DictExtraPasses,
                            overlay, oneLine, isDbService: false));
                    if (Debug.DebugFlags.NativeShowReplace)           // RF-500
                        System.Diagnostics.Trace.WriteLine("GORT nat: blocos=" + keptBlocks.Count);
                    _lastKept[r] = (keptRegion, keptBlocks);
                }
                }
                foreach (var t in treated) flatBlocks.Add((r, t));
                areaCounts.Add(treated.Count);
                areaTexts.Add(string.Join("\n", treated));
                treatedPerArea.Add(treated);
                regionsKept.Add(keptRegion);
                blocksKept.Add(keptBlocks);
            }
            _lastTreated = treatedPerArea;

            // RF-193: igualdade exata sobre o concatenado pós-tratamento.
            string current = string.Join("\n", areaTexts);
            var dec = _change.Step(current, DateTime.UtcNow, overlayOrLayer: overlay || p.WindowMode == "layer");
            if (!dec.FullPath)
            {
                if (dec.RepaintIdle) _sink.Repaint();        // RF-196/197
                return EndOfCycle(ctx, running: true);       // RF-202 (pontual)
            }

            // Passos 12–13: traduz com verificação de parada a cada 50 ms.
            var sources = new List<string>();
            foreach (var (_, t) in flatBlocks) sources.Add(t);
            var (srcCode, dstCode) = ResolvePair(p, p.TranslationService);
            using var stopCts = new CancellationTokenSource();
            var task = _pipe.TranslateBatchAsync(p.TranslationService, sources,
                srcCode, dstCode, _cfg.Advanced.Bridge, stopCts.Token);
            while (!task.Wait(Params.P126_StopPollMs))       // P-126
            {
                if (ctx.StopRequested) stopCts.Cancel();
            }
            BatchResult batch;
            try { batch = task.GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                _sink.SetRunning(false);
                return false;
            }
            if (ctx.StopRequested) { _sink.SetRunning(false); return false; }
            for (int i = 0; i < sources.Count; i++) Debug.DebugLog.Translated();  // RF-498
            if (batch.Error is not null)
                Debug.DebugLog.Message("Tradução: " + batch.Error);

            // Distribui por área e desenha (passos 14–18).
            // RF-491: traduções de cache ganham marcador ◈(n).
            if (Debug.DebugFlags.ShowCache && batch.Error is null)
            {
                int n = _pipe.CacheCount?.Invoke(p.TranslationService) ?? 0;
                for (int i = 0; i < batch.PerText.Count && i < batch.FromCache.Count; i++)
                    if (batch.FromCache[i] && batch.PerText[i] is string t)
                        batch.PerText[i] = $"◈({n}) " + t;
            }
            if (Debug.DebugFlags.SaveAnalysis)                       // RF-492
                WritePortrait(p, plan, regionsKept, blocksKept, batch, flatBlocks, areaCounts);
            if (overlay && _sink is UI.OverlaySink osink)
            {
                var frame = new OverlayFrame();
                int bj = 0;
                for (int r = 0; r < plan.Rects.Count; r++)
                {
                    var oreg = new OverlayRegion
                    {
                        Index = r, Rect = plan.Rects[r], Zoom = p.Zoom,
                        ClientX = clientPerArea[r].CX, ClientY = clientPerArea[r].CY,
                    };
                    var oi = origKept[r];
                    if (oi?.OrigBytes is not null)
                    {
                        oreg.OrigBytes = oi.OrigBytes;
                        oreg.OrigW = oi.OrigWidth; oreg.OrigH = oi.OrigHeight;
                    }
                    var kept = blocksKept[r];
                    for (int k = 0; k < areaCounts[r] && bj < batch.PerText.Count; k++, bj++)
                    {
                        var ob = new OverlayBlock
                        {
                            Text = batch.Error ?? batch.PerText[bj] ?? "",
                        };
                        if (k < kept.Count)
                        {
                            var sb = kept[k];
                            ob.IsTitle = sb.IsTitle;
                            ob.Vertical = sb.Orientation == Text.LineOrientation.Vertical;
                            ob.OX = sb.OX; ob.OY = sb.OY; ob.OW = sb.OW; ob.OH = sb.OH;
                            foreach (var ln in sb.Lines)
                            {
                                ob.LineBoxes.Add((ln.X, ln.Y, ln.W, ln.H));
                                foreach (var wd in ln.Words)
                                    ob.WordBoxes.Add((wd.X, wd.Y, wd.W, wd.H));
                            }
                        }
                        oreg.Blocks.Add(ob);
                    }
                    frame.Regions.Add(oreg);
                }
                if (ctx.Mode == LoopMode.Once)
                    osink.DrawSnapshot(frame, _cfg.Advanced.SnapshotStaySec);  // RF-384
                else
                    osink.DrawOverlay(frame);
                // Mesma Etapa 16 dos outros modos: clipboard/TTS/arquivo
                // recebem a tradução (antes passava display vazio e emudecia).
                var otexts = new List<string>();
                foreach (var t in batch.PerText)
                    if (!string.IsNullOrEmpty(t)) otexts.Add(t);
                string odisplay = _effects.ApplyDisplayMemory(string.Join("\n", otexts));
                _effects.SideEffects(odisplay, string.Join("\n", areaTexts));
                return EndOfCycle(ctx, running: true);
            }
            var perRegion = new List<(int, List<(string, string)>)>();
            int bi = 0;
            var recognized = new List<string>();
            for (int r = 0; r < plan.Rects.Count; r++)
            {
                var items = new List<(string, string)>();
                for (int k = 0; k < areaCounts[r] && bi < batch.PerText.Count; k++, bi++)
                {
                    string tr = batch.Error ?? batch.PerText[bi] ?? "";
                    items.Add((flatBlocks[bi].Text, tr));
                }
                recognized.Add(string.Join("\n", items.ConvertAll(i => i.Item1)));
                perRegion.Add((r, items));
            }
            string display = _effects.ApplyDisplayMemory(      // Etapa 10
                TextPipeline.FormatDisplay(perRegion, p.AreaNumbering));
            _sink.Draw(display, string.Join("\n", recognized));
            _effects.SideEffects(display, string.Join("\n", recognized));  // Etapa 16
            return EndOfCycle(ctx, running: true);
        }
        catch (OperationCanceledException)
        {
            _sink.SetRunning(false);
            return false;
        }
    }

    private void WritePortrait(Profile p, Regions.RegionManager.CapturePlan plan,
        List<Text.RegionText?> regionsKept, List<List<Text.Block>> blocksKept,
        Translate.BatchResult batch, List<(int Area, string Text)> flatBlocks,
        List<int> areaCounts)
    {
        var areas = new List<(int, bool, Platform.ScreenRect, Platform.ScreenRect,
            List<Text.Block>, List<string>)>();
        int bj = 0;
        for (int r = 0; r < plan.Rects.Count; r++)
        {
            var trs = new List<string>();
            for (int k = 0; k < areaCounts[r] && bj < batch.PerText.Count; k++, bj++)
                trs.Add(batch.Error ?? batch.PerText[bj] ?? "");
            var rect = plan.Rects[r];
            areas.Add((r, plan.IsSnapshot, rect, rect, blocksKept[r], trs));
        }
        Debug.AnalysisPortrait.Write(p.WindowMode, p.OcrEngine, p.TranslationService, areas);
    }

    private bool EndOfCycle(LoopContext ctx, bool running)
    {
        if (ctx.Mode == LoopMode.Once || ctx.StopRequested)   // RF-202
        {
            _sink.SetRunning(false);
            return false;
        }
        return running;
    }

    /// <summary>
    /// Passo 7: tela, janela ativa (cliente cheio + recorte) ou anexada
    /// (RF-097: janela morta encerra o laço).
    /// </summary>
    private (Imaging.RegionImage? Img, (bool Gone, int CX, int CY) Client) CaptureArea(
        int index, Platform.ScreenRect rect, bool needOrig, Profile p, LoopContext ctx)
    {
        if (Platform.Windows.AttachedCapture.IsActive)
        {
            if (!Platform.Windows.AttachedCapture.IsAlive())
            {
                Platform.Windows.AttachedCapture.Stop();   // RF-090
                return (null, (true, 0, 0));               // RF-097
            }
            var client = Platform.Windows.AttachedCapture.ClientRect();
            var img = Platform.Windows.AttachedCapture.CaptureRect(
                index, rect, needOrig, () => ctx.StopRequested);
            return (img, (false, client.X, client.Y));
        }
        if (p.CaptureActiveWindow
            && PlatformFactory.Current.Capture.SupportsClientArea)
        {
            var img = PlatformFactory.Current.Capture.CaptureClientArea(index, rect, needOrig);
            return (img, (false, 0, 0));
        }
        return (PlatformFactory.Current.Capture.CaptureRect(index, rect, needOrig),
            (false, 0, 0));
    }

    /// <summary>
    /// Assinatura do que influencia pixels+tratamento: qualquer mudança
    /// força um ciclo completo (os hashes de imagem caducam).
    /// </summary>
    internal static int Fingerprint(Config.Profile p,
        RegionManager.CapturePlan plan, bool needOrig,
        Config.AdvancedOptions adv, bool overlay, bool oneLine)
    {
        var h = new HashCode();
        h.Add(p.Zoom); h.Add(p.Threshold); h.Add(p.Erode);
        h.Add(p.ColorFilter); h.Add(p.PreprocessOff); h.Add(needOrig);
        // Tudo que muda o tratado: motor/idioma OCR, agrupamento, dicionário.
        h.Add(p.OcrEngine); h.Add(p.OcrLanguage);
        h.Add(p.MergeLinesOverlay); h.Add(p.RemoveSpaces);
        h.Add(p.UseDict); h.Add(p.DictByWord); h.Add(adv.DictExtraPasses);
        h.Add(p.WindowMode); h.Add(overlay); h.Add(oneLine);
        h.Add(plan.Rects.Count);
        foreach (var rc in plan.Rects) { h.Add(rc.X); h.Add(rc.Y); h.Add(rc.W); h.Add(rc.H); }
        foreach (var g in plan.GroupsPerRect) foreach (var i in g) h.Add(i);
        return h.ToHashCode();
    }

    /// <summary>FNV-1a 64 sobre os bytes capturados (1 passada, sem alocação).</summary>
    internal static ulong ImageHash(byte[] b)
    {
        ulong h = 1469598103934665603ul;
        foreach (byte x in b) { h ^= x; h *= 1099511628211ul; }
        return h;
    }

    /// <summary>
    /// Apaga da captura (BGRA) a interseção com os retângulos da janela de
    /// saída, em pixels físicos da área. Puro e testável: sem oclusores ou
    /// sem interseção, não toca em nada; fora de 4 canais, não age.
    /// </summary>
    internal static void BlackoutOutput(Imaging.RegionImage img, Platform.ScreenRect area,
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect> occluders)
    {
        if (occluders.Count == 0 || img.Channels != 4) return;
        BlackoutPlane(img.Bytes, img.Width, img.Height, area, occluders);
        // O original acompanha quando tem as mesmas dimensões (a análise de
        // cor só o pede em sobreposição, onde os oclusores são vazios).
        var ob = img.OrigBytes;
        if (ob is not null && img.OrigWidth == img.Width && img.OrigHeight == img.Height
            && ob.Length == img.Width * img.Height * 4)
            BlackoutPlane(ob, img.Width, img.Height, area, occluders);
    }

    private static void BlackoutPlane(byte[] bytes, int w, int h, Platform.ScreenRect area,
        System.Collections.Generic.IReadOnlyList<Platform.ScreenRect> occluders)
    {
        foreach (var o in occluders)
        {
            int x1 = Math.Max(o.X, area.X), y1 = Math.Max(o.Y, area.Y);
            int x2 = Math.Min(o.X + o.W, area.X + w);
            int y2 = Math.Min(o.Y + o.H, area.Y + h);
            if (x2 <= x1 || y2 <= y1) continue;
            for (int y = y1 - area.Y; y < y2 - area.Y; y++)
                for (int x = x1 - area.X; x < x2 - area.X; x++)
                {
                    int i = (y * w + x) * 4;
                    bytes[i] = 0; bytes[i + 1] = 0; bytes[i + 2] = 0;
                }
        }
    }

    /// <summary>
    /// Par origem/destino do serviço: o par da aba Dicionário vale quando
    /// preenchido; senão o global (OcrLanguage/TargetLanguage).
    /// Padrão continua en→pt-BR; ja→en funciona por serviço.
    /// </summary>
    internal static (string Src, string Dst) ResolvePair(Config.Profile p, string svcId)
    {
        string srcKey = p.ServiceSource.TryGetValue(svcId, out var ssk) && ssk != "" ? ssk
            : LangCodes.KeyForOcr(Config.LanguageTable.Find(p.OcrLanguage)?.OcrCode ?? "eng");
        string dstKey = p.ServiceTarget.TryGetValue(svcId, out var stk) && stk != ""
            ? stk : p.TargetLanguage;
        string srcCode = LangCodes.CodeFor(svcId, srcKey);
        if (srcCode == "") srcCode = LangCodes.CodeFor(svcId, "en");
        string dstCode = LangCodes.CodeFor(svcId, dstKey);
        return (srcCode, dstCode);
    }

    private OcrResult? RunOcr(ProcessedImage proc, Profile p, LoopContext ctx)    {
        var engine = OcrEngines.Get(p.OcrEngine);
        if (engine is null || !engine.IsAvailable)
            return OcrResult.Fail(engine?.UnavailableReason
                ?? $"Motor de OCR '{p.OcrEngine}' indisponível.");
        Debug.DebugLog.OcrAttempt();                                  // RF-498
        if (Debug.DebugFlags.NativeSaveShot)                          // RF-500
            SaveDebugShot(proc);
        string code = Config.LanguageTable.Find(p.OcrLanguage)?.OcrCode ?? "eng";
        using var stopCts = new CancellationTokenSource();
        var task = engine.RecognizeAsync(proc, code, stopCts.Token);
        while (!task.Wait(Params.P126_StopPollMs))            // passo 9: 50 ms
        {
            if (ctx.StopRequested) stopCts.Cancel();
        }
        try { return task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { return null; }   // RF-205: reusa anterior
    }

    private static IDisposable ConcealForCapture(List<ScreenRect> rects)
    {
        try { return Platform.PlatformFactory.CaptureConcealer.Conceal(rects); }
        catch { return Platform.NullConcealer.Instance.Conceal(rects); }
    }

    private static List<ScreenRect> LocalExclusions(ScreenRect area, List<ScreenRect> globals)
    {
        var list = new List<ScreenRect>();
        foreach (var g in globals)
        {
            int x1 = Math.Max(g.X, area.X), y1 = Math.Max(g.Y, area.Y);
            int x2 = Math.Min(g.X + g.W, area.X + area.W);
            int y2 = Math.Min(g.Y + g.H, area.Y + area.H);
            if (x2 > x1 && y2 > y1)
                list.Add(new ScreenRect(x1 - area.X, y1 - area.Y, x2 - x1, y2 - y1));
        }
        return list;
    }

    private List<(int R, int G, int B, int S1, int S2, int V1, int V2)> Tuples(List<int> idx)
    {
        var list = new List<(int, int, int, int, int, int, int)>();
        foreach (int i in idx)
            if (i >= 0 && i < _cfg.Profile.ColorGroups.Count)
            {
                var g = _cfg.Profile.ColorGroups[i];
                list.Add((g.R, g.G, g.B, g.S1, g.S2, g.V1, g.V2));
            }
        return list;
    }

    private static void SaveDebugShot(ProcessedImage proc)
    {
        try
        {
            byte[] px = proc.Channels == 1
                ? Preprocess.ConvertChannels(proc.Bytes, proc.Width, proc.Height, 1, 3)
                : Preprocess.ConvertChannels(proc.Bytes, proc.Width, proc.Height,
                    proc.Channels, 3);
            // BGR → BGRA para o BMP.
            var bgra = new byte[proc.Width * proc.Height * 4];
            for (int i = 0; i < proc.Width * proc.Height; i++)
            {
                bgra[i * 4] = px[i * 3];
                bgra[i * 4 + 1] = px[i * 3 + 1];
                bgra[i * 4 + 2] = px[i * 3 + 2];
                bgra[i * 4 + 3] = 255;
            }
            // RF-500: salva captura/resultado.
            string path = System.IO.Path.Combine(Core.Paths.DebugDir,
                $"shot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.bmp");
            System.IO.Directory.CreateDirectory(Core.Paths.DebugDir);
            System.IO.File.WriteAllBytes(path,
                Imaging.Bmp.Encode(bgra, proc.Width, proc.Height, 32));
        }
        catch { }
    }

    private static Imaging.FilterMode FilterModeOf(string s) => s switch
    {
        "rgb" => Imaging.FilterMode.Rgb,
        "hsv" => Imaging.FilterMode.Hsv,
        "threshold" => Imaging.FilterMode.Threshold,
        _ => Imaging.FilterMode.None,
    };
}
