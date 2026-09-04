using System;
using System.Collections.Generic;
using Gort.Core;

namespace Gort.UI;

/// <summary>
/// Layout da sobreposição, puro e testável (RF-352..RF-376): origem,
/// colisões, conteúdo, fonte automática, expansão, bissecção, quebra.
/// Medição injetada (Skia na janela, monoespaçada nos testes).
/// </summary>
public static class OverlayLayout
{
    public sealed class Item
    {
        public int Area;
        public bool Title;
        public bool Vertical;
        public string Text = "";
        public float PrefPx;
        public float BodyPx;
        public float X, Y, W, H;          // visualização (tela)
        public float CX, CY, CW, CH;      // conteúdo
        public float FontPx;
        public List<(string Text, float X, float Y)> Lines = new();
        public bool Clipped;
    }

    /// <summary>
    /// RF-352: origem em tela = origem da área − metade da borda + bloco/zoom
    /// − posição da janela. Piso no topo/esquerda, teto na base/direita.
    /// </summary>
    public static (float X, float Y, float W, float H) Origin(
        int areaX, int areaY, float borderHalf,
        int bx, int by, int bw, int bh, double zoom, int winX, int winY)
    {
        double z = zoom <= 0 ? 1.0 : zoom;   // zoom inválido não gera Infinito
        return (areaX - borderHalf + (float)(bx / z) - winX,
            areaY - borderHalf + (float)(by / z) - winY,
            (float)Math.Ceiling(bw / z),
            (float)Math.Ceiling(bh / z));
    }

    /// <summary>
    /// RF-355..358: enquanto houver sobreposição, separa o par de maior área
    /// no eixo que perde menos; fronteira proporcional às áreas (RF-356);
    /// título preserva (RF-357); teto n²×4 iterações (RF-358).
    /// </summary>
    public static void ResolveCollisions(List<Item> items)
    {
        int maxIter = items.Count * items.Count * 4;
        for (int iter = 0; iter < maxIter; iter++)
        {
            int bi = -1, bj = -1;
            float best = 0;
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    float a = OverlapArea(items[i], items[j]);
                    if (a > best) { best = a; bi = i; bj = j; }
                }
            if (bi < 0) return;
            Separate(items[bi], items[bj]);
        }
    }

    private static float OverlapArea(Item a, Item b)
    {
        float w = Math.Max(0, Math.Min(a.X + a.W, b.X + b.W) - Math.Max(a.X, b.X));
        float h = Math.Max(0, Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y));
        return w * h;
    }

    private static void Separate(Item a, Item b)
    {
        if (a.Title && !b.Title) { ShrinkLoser(b, a); return; }   // RF-357 🔒
        if (b.Title && !a.Title) { ShrinkLoser(a, b); return; }
        // Perda em cada eixo: quanto some se cortar a interseção toda.
        float ix1 = Math.Max(a.X, b.X), iy1 = Math.Max(a.Y, b.Y);
        float ix2 = Math.Min(a.X + a.W, b.X + b.W), iy2 = Math.Min(a.Y + a.H, b.Y + b.H);
        float iw = Math.Max(0, ix2 - ix1), ih = Math.Max(0, iy2 - iy1);
        float lossX = iw * (a.H + b.H);
        float lossY = ih * (a.W + b.W);
        if (lossX <= lossY) SplitAxis(a, b, horizontal: true, ix1, ix2);
        else SplitAxis(a, b, horizontal: false, iy1, iy2);
    }

    private static void ShrinkLoser(Item loser, Item keeper)
    {
        // O perdedor cede: recorta a interseção do seu retângulo.
        float ix1 = Math.Max(loser.X, keeper.X), iy1 = Math.Max(loser.Y, keeper.Y);
        float ix2 = Math.Min(loser.X + loser.W, keeper.X + keeper.W);
        float iy2 = Math.Min(loser.Y + loser.H, keeper.Y + keeper.H);
        float iw = ix2 - ix1, ih = iy2 - iy1;
        if (iw * loser.H <= ih * loser.W)
        {
            // Corta na horizontal: fica com o maior lado.
            float leftW = ix1 - loser.X, rightW = loser.X + loser.W - ix2;
            if (leftW >= rightW) loser.W = Math.Max(0, leftW);
            else { loser.X = ix2; loser.W = Math.Max(0, rightW); }
        }
        else
        {
            float topH = iy1 - loser.Y, botH = loser.Y + loser.H - iy2;
            if (topH >= botH) loser.H = Math.Max(0, topH);
            else { loser.Y = iy2; loser.H = Math.Max(0, botH); }
        }
    }

    private static void SplitAxis(Item a, Item b, bool horizontal, float i1, float i2)
    {
        float total = horizontal ? (i2 - i1) : (i2 - i1);
        float aa = a.W * a.H, bb = b.W * b.H;
        float frac = (aa + bb) > 0 ? aa / (aa + bb) : 0.5f;   // RF-356 🔒
        float cut = i1 + total * frac;
        if (horizontal)
        {
            // a fica à esquerda do corte se já estava mais à esquerda.
            if (a.X <= b.X) { a.W = Math.Max(0, cut - a.X); b.X = cut; b.W = Math.Max(0, b.X + b.W - cut); }
            else { b.W = Math.Max(0, cut - b.X); a.X = cut; a.W = Math.Max(0, a.X + a.W - cut); }
        }
        else
        {
            if (a.Y <= b.Y) { a.H = Math.Max(0, cut - a.Y); b.Y = cut; b.H = Math.Max(0, b.Y + b.H - cut); }
            else { b.H = Math.Max(0, cut - b.Y); a.Y = cut; a.H = Math.Max(0, a.Y + a.H - cut); }
        }
    }

    /// <summary>RF-359: conteúdo = visual − P-93 por lado com contorno.</summary>
    public static void ApplyContent(Item it, bool outline)
    {
        if (!outline) { it.CX = it.X; it.CY = it.Y; it.CW = it.W; it.CH = it.H; return; }
        it.CX = it.X + Params.P93_ContentShrink;
        it.CY = it.Y + Params.P93_ContentShrink;
        it.CW = Math.Max(0, it.W - 2 * Params.P93_ContentShrink);
        it.CH = Math.Max(0, it.H - 2 * Params.P93_ContentShrink);
    }

    /// <summary>
    /// RF-360: tamanho preferido. Usa próprio quando título, ou líder
    /// (mais acima/esquerda e ≥ P-94 × corpo — 🔒); senão o corpo.
    /// Entrada/saída em px de desenho.
    /// </summary>
    public static float Preferred(float ownPx, float bodyPx, bool isTitle, bool isLeader) =>
        (isTitle || (isLeader && ownPx >= Params.P94_LeaderRatio * bodyPx)) ? ownPx : bodyPx;  // 🔒

    /// <summary>
    /// RF-363: testa o preferido direto (atalho); senão bissecção em no máximo
    /// P-96 iterações até diferença ≤ P-97 (🔒 9 / 0,25).
    /// </summary>
    public static (float Size, bool Fits) FindFont(float prefer, float min,
        Func<float, bool> fits)
    {
        if (fits(prefer)) return (prefer, true);              // 🔒 atalho
        float lo = min, hi = prefer;
        bool ok = false;
        for (int i = 0; i < Params.P96_FontBisectIters; i++)  // 🔒 9
        {
            if (hi - lo <= Params.P97_FontBisectEps) break;   // 🔒 0,25
            float mid = (lo + hi) / 2;
            if (fits(mid)) { lo = mid; ok = true; }
            else hi = mid;
        }
        return (lo, ok);
    }

    /// <summary>
    /// RF-369: quebra caractere a caractere por busca binária: maior prefixo
    /// que cabe em (disponível − folga P-100×fonte). Um caractere no mínimo
    /// (RF-370); remove espaços iniciais do resto (RF-371).
    /// </summary>
    public static List<string> Wrap(string text, float avail, float fontPx,
        Func<string, float> measure)
    {
        var lines = new List<string>();
        foreach (var explicit_ in text.Replace("\r\n", "\n").Split('\n'))  // RF-372
        {
            string rest = explicit_;
            while (rest.Length > 0)
            {
                float slack = (float)(Params.P100_BreakSlack * fontPx);    // 🔒 1,2
                int lo = 1, hi = rest.Length, best = 1;      // RF-370
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (measure(rest[..mid]) <= avail - slack) { best = mid; lo = mid + 1; }
                    else hi = mid - 1;
                }
                lines.Add(rest[..best]);
                rest = rest[best..].TrimStart();             // RF-371
            }
        }
        return lines;
    }

    /// <summary>
    /// RF-364/365/367/368: posiciona cada linha onde será desenhada e verifica
    /// os limites do desenho contra o conteúdo. Avanço = fonte × P-98.
    /// Faixas: horizontal largura cheia deslocada pelo índice (piso); vertical
    /// altura cheia a partir da direita recuando (teto).
    /// </summary>
    public static (bool Fits, List<(string Text, float X, float Y)> Placed) Place(
        Item it, float fontPx, Func<string, float> hMeasure, Func<string, float> vMeasure,
        bool verticalMode)
    {
        var placed = new List<(string, float, float)>();
        float advance = fontPx * (float)Params.P98_LineAdvance;   // 🔒 1,2
        float slack = (float)(Params.P99_OutlineSlack);           // 🔒 2,5
        if (!verticalMode)
        {
            var lines = Wrap(it.Text, it.CW, fontPx, hMeasure);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Length == 0) continue;        // RF-367
                float y = (float)Math.Floor(it.CY + i * advance);
                float w = hMeasure(lines[i]);
                if (y + advance > it.CY + it.CH + 0.01f
                    || it.CX + w > it.CX + it.CW + slack + 0.01f)
                    return (false, placed);
                placed.Add((lines[i], it.CX, y));
            }
            return (true, placed);
        }
        else
        {
            var lines = Wrap(it.Text, it.CH, fontPx, vMeasure);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                float x = (float)Math.Ceiling(it.CX + it.CW - (i + 1) * advance);
                float h = vMeasure(lines[i]);
                if (x < it.CX - 0.01f || it.CY + h > it.CY + it.CH + slack + 0.01f)
                    return (false, placed);
                placed.Add((lines[i], x, it.CY));
            }
            return (true, placed);
        }
    }

    /// <summary>RF-362: expande o retângulo por busca binária (largura e altura).</summary>
    public static float ExpandToFit(float current, float limit, Func<float, bool> fits)
    {
        float lo = current, hi = limit;
        for (int i = 0; i < 16; i++)
        {
            if (hi - lo < 1) break;
            float mid = (lo + hi) / 2;
            if (fits(mid)) lo = mid;
            else hi = mid;
        }
        return lo;
    }
}
