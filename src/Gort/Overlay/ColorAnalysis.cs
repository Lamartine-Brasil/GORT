using System;
using System.Collections.Generic;
using Gort.Core;
using Gort.Imaging;

namespace Gort.Overlay;

/// <summary>
/// Análise automática de cor (cap. 20 🔒): fonte e fundo de cada bloco a
/// partir da imagem ORIGINAL. Três estratégias em cascata para o fundo
/// (bordas → anéis → dominante) e candidatos por contraste para a fonte,
/// com correção final de legibilidade (contraste ≥ P-115).
/// </summary>
public static class ColorAnalysis
{
    public sealed class Result
    {
        public (byte R, byte G, byte B) Font;
        public (byte R, byte G, byte B) Background;
        public double Contrast;
        public bool UsedFallback;
        public bool ContrastFixed;
        public bool Failed;
    }

    public sealed class WordBox
    {
        public int X, Y, W, H;
    }

    /// <param name="bgra">Imagem original (sem filtro), dimensões ampliadas.</param>
    public static Result Analyze(byte[] bgra, int w, int h, int channels,
        int bx, int by, int bw, int bh, List<WordBox> words)
    {
        var res = new Result { Failed = true };
        // Recorta para a imagem (RF-395: escala por eixo, piso/cima, satura).
        int x1 = Math.Clamp(bx, 0, w), y1 = Math.Clamp(by, 0, h);
        int x2 = Math.Clamp(bx + bw, 0, w), y2 = Math.Clamp(by + bh, 0, h);
        if (x2 <= x1 || y2 <= y1) return res;

        var bg = FindBackground(bgra, w, h, channels, x1, y1, x2, y2, words);
        if (bg is null) return res;
        res.Background = bg.Color;

        var font = FindFont(bgra, w, h, channels, words, bg.Color,
            bg.PerWord, res);
        res.Font = font;
        res.Failed = false;
        return res;
    }

    // ---- fundo ----

    private sealed class Bucket
    {
        public long R, G, B;
        public int Count;
        public double ContrastSum;
        public HashSet<int> WordIds = new();
    }

    private static int Quant(int c) => c >> Params.P158_QuantizeDropBits;  // 🔒 3 bits
    private static int Key(int r, int g, int b) => (Quant(r) << 10) | (Quant(g) << 5) | Quant(b);

    private static (byte R, byte G, byte B) Pixel(byte[] bgra, int w, int channels, int x, int y)
    {
        int o = (y * w + x) * channels;
        return channels == 1
            ? (bgra[o], bgra[o], bgra[o])
            : (bgra[o + 2], bgra[o + 1], bgra[o]);
    }

    private static bool Opaque(byte[] bgra, int w, int channels, int x, int y) =>
        channels < 4 || bgra[(y * w + x) * channels + 3] >= Params.P107_MinAlpha;  // RF-397

    private sealed class BgResult
    {
        public (byte R, byte G, byte B) Color;
        public Dictionary<int, (byte R, byte G, byte B)> PerWord = new();
    }

    private static BgResult? FindBackground(byte[] bgra, int w, int h, int ch,
        int x1, int y1, int x2, int y2, List<WordBox> words)
    {
        var perWord = new Dictionary<int, (byte, byte, byte)>();
        // Estratégia 1 — bordas das palavras (RF-399/400/401).
        var localVotes = new List<(int Wi, (byte, byte, byte) Color, int Corners, int Probes, int Pop)>();
        for (int wi = 0; wi < words.Count; wi++)
        {
            var wd = words[wi];
            var probes = EdgeProbes(bgra, w, h, ch, wd);
            if (probes is null) continue;
            var best = ElectLocal(bgra, w, h, ch, probes);
            if (best is null) continue;                              // cai p/ anel
            perWord[wi] = best.Color;
            localVotes.Add((wi, best.Color, best.Corners, best.Probes, best.Pop));
        }
        if (localVotes.Count > 0)
        {
            var global = ElectGlobal(localVotes, words.Count);
            if (global is not null)
                return new BgResult { Color = global.Value, PerWord = perWord };
        }
        // Estratégia 2 — anéis (RF-402): por palavra, depois voto global simples.
        var ringVotes = new Dictionary<int, (int Count, long R, long G, long B, int Pop)>();
        for (int wi = 0; wi < words.Count; wi++)
        {
            var c = RingColor(bgra, w, h, ch, words[wi], x1, y1, x2, y2, words);
            if (c is null) continue;
            perWord.TryAdd(wi, c.Value);
            int k = Key(c.Value.R, c.Value.G, c.Value.B);
            if (!ringVotes.TryGetValue(k, out var v)) v = (0, 0, 0, 0, 0);
            ringVotes[k] = (v.Count + 1, v.R + c.Value.R, v.G + c.Value.G, v.B + c.Value.B, v.Pop + 1);
        }
        if (ringVotes.Count > 0)
        {
            int bkk = -1, bcc = -1;
            foreach (var (k, vv) in ringVotes)
                if (vv.Count > bcc || (vv.Count == bcc && k < bkk)) { bkk = k; bcc = vv.Count; }
            var rv = ringVotes[bkk];
            int n = Math.Max(1, rv.Pop);
            return new BgResult
            {
                Color = ((byte)(rv.R / n), (byte)(rv.G / n), (byte)(rv.B / n)),
                PerWord = perWord,
            };
        }
        // Estratégia 3 — dominante do bloco (RF-403).
        var dom = Dominant(bgra, w, ch, x1, y1, x2, y2, Params.P105_MaxSamplesBg);
        if (dom is null) return null;                                // RF-404
        return new BgResult { Color = dom.Value, PerWord = perWord };
    }

    private sealed class Probe
    {
        public int X1, Y1, X2, Y2;
        public bool Corner;
    }

    /// <summary>RF-399: 8 sondas por palavra (4 faixas + 4 cantos).</summary>
    private static List<Probe>? EdgeProbes(byte[] bgra, int w, int h, int ch, WordBox wd)
    {
        int wx1 = Math.Clamp(wd.X, 0, w), wy1 = Math.Clamp(wd.Y, 0, h);
        int wx2 = Math.Clamp(wd.X + wd.W, 0, w), wy2 = Math.Clamp(wd.Y + wd.H, 0, h);
        int ww = wx2 - wx1, wh = wy2 - wy1;
        if (ww <= 0 || wh <= 0) return null;
        int minor = Math.Min(ww, wh);
        int thick = Math.Clamp((int)Math.Ceiling(Params.P108_EdgeRatio * minor),
            1, Params.P109_EdgeMax);                                 // 🔒 0,15 / 4
        int cw = Math.Min(ww, Math.Max(thick, Math.Min(4, ww / 3)));
        int chh = Math.Min(wh, Math.Max(thick, Math.Min(4, wh / 3)));
        var list = new List<Probe>
        {
            new() { X1 = wx1, Y1 = wy1, X2 = wx2, Y2 = wy1 + thick },
            new() { X1 = wx1, Y1 = wy2 - thick, X2 = wx2, Y2 = wy2 },
            new() { X1 = wx1, Y1 = wy1, X2 = wx1 + thick, Y2 = wy2 },
            new() { X1 = wx2 - thick, Y1 = wy1, X2 = wx2, Y2 = wy2 },
            new() { X1 = wx1, Y1 = wy1, X2 = wx1 + cw, Y2 = wy1 + chh, Corner = true },
            new() { X1 = wx2 - cw, Y1 = wy1, X2 = wx2, Y2 = wy1 + chh, Corner = true },
            new() { X1 = wx1, Y1 = wy2 - chh, X2 = wx1 + cw, Y2 = wy2, Corner = true },
            new() { X1 = wx2 - cw, Y1 = wy2 - chh, X2 = wx2, Y2 = wy2, Corner = true },
        };
        return list;
    }

    private static int Step(int area, int max) =>
        Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area / (double)max)));  // RF-396

    private static (byte R, byte G, byte B)? DominantIn(
        byte[] bgra, int w, int ch, int x1, int y1, int x2, int y2,
        int maxSamples, out int pop)
    {
        var buckets = new Dictionary<int, Bucket>();
        int area = Math.Max(1, (x2 - x1) * (y2 - y1));
        int step = Step(area, maxSamples);
        for (int y = y1; y < y2; y += step)
            for (int x = x1; x < x2; x += step)
            {
                if (!Opaque(bgra, w, ch, x, y)) continue;            // RF-397
                var (r, g, b) = Pixel(bgra, w, ch, x, y);
                int k = Key(r, g, b);
                if (!buckets.TryGetValue(k, out var bk)) { bk = new Bucket(); buckets[k] = bk; }
                bk.R += r; bk.G += g; bk.B += b; bk.Count++;
            }
        int bk2 = -1, bc = -1;
        foreach (var (k, v) in buckets)                              // empate: menor chave
            if (v.Count > bc || (v.Count == bc && k < bk2)) { bk2 = k; bc = v.Count; }
        if (bk2 < 0) { pop = 0; return null; }
        var f = buckets[bk2];
        pop = f.Count;
        return ((byte)(f.R / f.Count), (byte)(f.G / f.Count), (byte)(f.B / f.Count));  // mediana≈média
    }

    private sealed class LocalPick
    {
        public (byte R, byte G, byte B) Color;
        public int Corners, Probes, Pop;
    }

    // ---- implementação com imagem ----

    private static LocalPick? ElectLocal(
        byte[] bgra, int w, int h, int ch, List<Probe> probes)
    {
        var groups = new Dictionary<int, (long R, long G, long B, int N, int Corners, int Pop)>();
        foreach (var pr in probes)
        {
            var d = DominantIn(bgra, w, ch, pr.X1, pr.Y1, pr.X2, pr.Y2,
                Params.P106_MaxSamplesWord, out int pop);
            if (d is null || pop == 0) continue;
            int k = Key(d.Value.R, d.Value.G, d.Value.B);
            if (!groups.TryGetValue(k, out var g)) g = (0, 0, 0, 0, 0, 0);
            groups[k] = (g.R + d.Value.R * pop, g.G + d.Value.G * pop, g.B + d.Value.B * pop,
                g.N + 1, g.Corners + (pr.Corner ? 1 : 0), g.Pop + pop);
        }
        int bk = -1, bc = -1, bp = -1, bpop = -1;
        foreach (var (k, g) in groups)
        {
            bool eligible = g.N >= Params.P110_MinProbes                    // 🔒 3
                && (g.Corners >= 2 || g.N >= 5);                            // P-159 🔒
            if (!eligible) continue;
            if (g.Corners > bc || (g.Corners == bc && (g.N > bp || (g.N == bp && g.Pop > bpop))))
            { bk = k; bc = g.Corners; bp = g.N; bpop = g.Pop; }
        }
        if (bk < 0) return null;
        var f = groups[bk];
        int n = Math.Max(1, f.Pop);
        return new LocalPick
        {
            Color = ((byte)(f.R / n), (byte)(f.G / n), (byte)(f.B / n)),
            Corners = f.Corners, Probes = f.N, Pop = f.Pop,
        };
    }

    private static (byte R, byte G, byte B)? ElectGlobal(
        List<(int Wi, (byte, byte, byte) Color, int Corners, int Probes, int Pop)> votes,
        int wordCount)
    {
        var groups = new Dictionary<int, (long R, long G, long B, int Words, int Probes, int Pop)>();
        foreach (var (_, c, _, probes, pop) in votes)
        {
            int k = Key(c.Item1, c.Item2, c.Item3);
            if (!groups.TryGetValue(k, out var g)) g = (0, 0, 0, 0, 0, 0);
            groups[k] = (g.R + c.Item1, g.G + c.Item2, g.B + c.Item3,
                g.Words + 1, g.Probes + probes, g.Pop + pop);
        }
        int need = (int)Math.Ceiling(Params.P111_GlobalSupport * wordCount);  // 🔒 0,4
        int bk = -1, bw = -1, bp = -1, bpop = -1;
        foreach (var (k, g) in groups)
        {
            if (g.Words < need) continue;
            if (g.Words > bw || (g.Words == bw && (g.Probes > bp || (g.Probes == bp && g.Pop > bpop))))
            { bk = k; bw = g.Words; bp = g.Probes; bpop = g.Pop; }
        }
        if (bk < 0) return null;
        var f = groups[bk];
        int n = Math.Max(1, f.Words);
        return ((byte)(f.R / n), (byte)(f.G / n), (byte)(f.B / n));
    }

    private static (byte R, byte G, byte B)? RingColor(byte[] bgra, int w, int h, int ch,
        WordBox wd, int bx1, int by1, int bx2, int by2, List<WordBox> all)
    {
        int minor = Math.Min(Math.Max(1, wd.W), Math.Max(1, wd.H));
        int pad = Math.Clamp((int)Math.Ceiling(Params.P112_RingRatio * minor),  // 🔒
            Params.P113_RingMin, Params.P114_RingMax);
        int rx1 = Math.Max(wd.X - pad, bx1 - Params.P114_RingMax);
        int ry1 = Math.Max(wd.Y - pad, by1 - Params.P114_RingMax);
        int rx2 = Math.Min(wd.X + wd.W + pad, bx2 + Params.P114_RingMax);
        int ry2 = Math.Min(wd.Y + wd.H + pad, by2 + Params.P114_RingMax);
        rx1 = Math.Clamp(rx1, 0, w); ry1 = Math.Clamp(ry1, 0, h);
        rx2 = Math.Clamp(rx2, 0, w); ry2 = Math.Clamp(ry2, 0, h);
        bool InsideAny(int x, int y)
        {
            foreach (var o in all)
                if (x >= o.X && x < o.X + o.W && y >= o.Y && y < o.Y + o.H) return true;
            return false;
        }
        var buckets = new Dictionary<int, Bucket>();
        int step = Step(Math.Max(1, (rx2 - rx1) * (ry2 - ry1)), Params.P106_MaxSamplesWord);
        for (int y = ry1; y < ry2; y += step)
            for (int x = rx1; x < rx2; x += step)
            {
                if (InsideAny(x, y)) continue;
                if (!Opaque(bgra, w, ch, x, y)) continue;
                var (r, g, b) = Pixel(bgra, w, ch, x, y);
                int k = Key(r, g, b);
                if (!buckets.TryGetValue(k, out var bk)) { bk = new Bucket(); buckets[k] = bk; }
                bk.R += r; bk.G += g; bk.B += b; bk.Count++;
            }
        int bkk = -1, bc = -1;
        foreach (var (k, v) in buckets)
            if (v.Count > bc || (v.Count == bc && k < bkk)) { bkk = k; bc = v.Count; }
        if (bkk < 0) return null;
        var f = buckets[bkk];
        return ((byte)(f.R / f.Count), (byte)(f.G / f.Count), (byte)(f.B / f.Count));
    }

    private static (byte R, byte G, byte B)? Dominant(byte[] bgra, int w, int ch,
        int x1, int y1, int x2, int y2, int max) =>
        DominantIn(bgra, w, ch, x1, y1, x2, y2, max, out _);

    // ---- fonte (RF-405..411) ----

    private static (byte R, byte G, byte B) FindFont(byte[] bgra, int w, int h, int ch,
        List<WordBox> words, (byte R, byte G, byte B) bg,
        Dictionary<int, (byte R, byte G, byte B)> perWord, Result res)
    {
        var buckets = new Dictionary<int, Bucket>();
        for (int wi = 0; wi < words.Count; wi++)
        {
            var wd = words[wi];
            // Fundo local: estratégia 1, senão anel, senão global (RF-405).
            var local = perWord.TryGetValue(wi, out var lw) ? lw : bg;
            int x1 = Math.Clamp(wd.X, 0, w), y1 = Math.Clamp(wd.Y, 0, h);
            int x2 = Math.Clamp(wd.X + wd.W, 0, w), y2 = Math.Clamp(wd.Y + wd.H, 0, h);
            if (x2 <= x1 || y2 <= y1) continue;
            int step = Step(Math.Max(1, (x2 - x1) * (y2 - y1)), Params.P106_MaxSamplesWord);
            for (int y = y1; y < y2; y += step)
                for (int x = x1; x < x2; x += step)
                {
                    if (!Opaque(bgra, w, ch, x, y)) continue;
                    var (r, g, b) = Pixel(bgra, w, ch, x, y);
                    if (ContrastRatio((r, g, b), local) < Params.P115_MinContrast) continue;  // 🔒
                    int k = Key(r, g, b);
                    if (!buckets.TryGetValue(k, out var bk)) { bk = new Bucket(); buckets[k] = bk; }
                    bk.R += r; bk.G += g; bk.B += b; bk.Count++;
                    bk.ContrastSum += ContrastRatio((r, g, b), local);
                    bk.WordIds.Add(wi);
                }
        }
        int bkk = -1, bw = -1, bp = -1;
        double bct = -1;
        foreach (var (k, v) in buckets)                            // RF-408 🔒
        {
            double avg = v.ContrastSum / Math.Max(1, v.Count);
            if (v.WordIds.Count > bw
                || (v.WordIds.Count == bw && (v.Count > bp
                    || (v.Count == bp && avg > bct))))
            { bkk = k; bw = v.WordIds.Count; bp = v.Count; bct = avg; }
        }
        (byte R, byte G, byte B) pick;
        if (bkk < 0)
        {
            pick = BestBW(bg);                                     // RF-409
            res.UsedFallback = true;
        }
        else
        {
            var f = buckets[bkk];
            pick = ((byte)(f.R / f.Count), (byte)(f.G / f.Count), (byte)(f.B / f.Count));
        }
        res.Contrast = ContrastRatio(pick, bg);
        if (res.Contrast < Params.P115_MinContrast)                // RF-410
        {
            pick = BestBW(bg);
            res.Contrast = ContrastRatio(pick, bg);
            res.ContrastFixed = true;
        }
        return pick;
    }

    private static (byte R, byte G, byte B) BestBW((byte R, byte G, byte B) bg) =>
        ContrastRatio(((byte)0, (byte)0, (byte)0), bg)
            >= ContrastRatio(((byte)255, (byte)255, (byte)255), bg)
            ? ((byte)0, (byte)0, (byte)0) : ((byte)255, (byte)255, (byte)255);

    /// <summary>RF-411: (Lmaior+0,05)/(Lmenor+0,05), luminância sRGB linearizada.</summary>
    public static double ContrastRatio((byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        double la = Lum(a), lb = Lum(b);
        return (Math.Max(la, lb) + Params.P162_ContrastConst)
             / (Math.Min(la, lb) + Params.P162_ContrastConst);    // 🔒 0,05
    }

    private static double Lum((byte R, byte G, byte B) c)
    {
        double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= Params.P161_LinearT ? s / Params.P161_LinearD
                : Math.Pow((s + Params.P161_GammaB) / Params.P161_GammaO, Params.P161_GammaE);
        }
        return Params.P160_LumR * Lin(c.R) + Params.P160_LumG * Lin(c.G)
            + Params.P160_LumB * Lin(c.B);
    }

    /// <summary>
    /// RF-393: contornos derivados da fonte — clara: mesma matiz, sat −0,05,
    /// brilho −0,1 + preto; escura: sat +0,05, brilho +0,1 + branco (🔒).
    /// </summary>
    public static ((byte R, byte G, byte B) C1, (byte R, byte G, byte B) C2)
        DeriveOutlines((byte R, byte G, byte B) font)
    {
        var (h, s, v) = ColorFilter.RgbToHsv(font.R, font.G, font.B);
        double sd = s / 255.0, vd = v / 255.0;
        if (vd >= 0.5)
        {
            var c1 = Preprocess.HsvToRgb(h,
                (int)Math.Round((sd - 0.05) * 255), (int)Math.Round((vd - 0.1) * 255));
            return (c1, ((byte)0, (byte)0, (byte)0));
        }
        var c1b = Preprocess.HsvToRgb(h,
            (int)Math.Round((sd + 0.05) * 255), (int)Math.Round((vd + 0.1) * 255));
        return (c1b, ((byte)255, (byte)255, (byte)255));
    }
}
