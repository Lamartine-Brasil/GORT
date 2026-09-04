using System.Collections.Generic;
using Gort.Core;

namespace Gort.Text;

/// <summary>
/// Agrupamento de linhas em blocos (15.2). Todas as constantes calibradas
/// (🔒): reproduzir exatamente, ver Parte XII. Único algoritmo — sem legado.
/// </summary>
public static class Grouping
{
    // ---- RF-163: adjacência espacial ----

    internal static double Gap(int aIni, int aFim, int bIni, int bFim) =>
        System.Math.Max(0, System.Math.Max(aIni, bIni) - System.Math.Min(aFim, bFim));

    internal static double Overlap(int aIni, int aFim, int bIni, int bFim) =>
        System.Math.Max(0, System.Math.Min(aFim, bFim) - System.Math.Max(aIni, bIni))
        / (double)System.Math.Max(1, System.Math.Min(aFim - aIni, bFim - bIni));

    public static bool Adjacent(Line a, Line b)
    {
        if (a.Orientation != b.Orientation) return false;                    // 1
        if (a.FontSize <= 0 || b.FontSize <= 0) return false;
        double big = System.Math.Max(a.FontSize, b.FontSize);
        double small = System.Math.Min(a.FontSize, b.FontSize);
        if (big / (double)small > Params.P34_FontAdjacencyRatio) return false;  // 2 🔒 1,3
        double avg = (a.FontSize + b.FontSize) / 2.0;
        if (a.Orientation == LineOrientation.Horizontal)
        {
            bool transverse = Overlap(a.X, a.X + a.W, b.X, b.X + b.W) >= Params.P36_TransverseOverlap
                || System.Math.Abs(a.X - b.X) <= avg * Params.P37_StartAlignFactor;  // 🔒
            return Gap(a.Y, a.Y + a.H, b.Y, b.Y + b.H) <= avg * Params.P35_FlowGapFactor  // 🔒
                && transverse;
        }
        else
        {
            bool transverse = Overlap(a.Y, a.Y + a.H, b.Y, b.Y + b.H) >= Params.P36_TransverseOverlap
                || System.Math.Abs(a.Y - b.Y) <= avg * Params.P37_StartAlignFactor;
            return Gap(a.X, a.X + a.W, b.X, b.X + b.W) <= avg * Params.P35_FlowGapFactor
                && transverse;
        }
    }

    // ---- RF-165..169: listas ----

    internal static string LTrimmed(Line l) => l.Text.TrimStart();

    public static bool IsStrongMarker(Line l)
    {
        string t = LTrimmed(l);
        if (t.Length <= 1) return false;
        foreach (var m in Params.P39_StrongBullets)                            // 🔒
            if (t.StartsWith(m)) return true;
        return false;
    }

    public static bool IsWeakCandidate(Line l)
    {
        string t = LTrimmed(l);
        return t.Length > 1 && (t[0] == '-' || t[0] == '*' || t[0] == '.');
    }

    public static bool IsExplicitWeak(Line l)
    {
        string t = LTrimmed(l);
        return t.Length > 1 && (t[0] == '-' || t[0] == '*' || t[0] == '.')
            && char.IsWhiteSpace(t[1]);
    }

    public static bool IsNumbered(Line l)
    {
        string t = LTrimmed(l);                                                // RF-169 🔒
        int i = 0;
        bool open = false;
        if (i < t.Length && (t[i] == '(' || t[i] == '（')) { open = true; i++; }
        int start = i, len = 0;
        while (i < t.Length && len < Params.P150_NumberedMaxLen
            && (char.IsLetterOrDigit(t[i]))) { i++; len++; }
        if (len == 0 || len > Params.P150_NumberedMaxLen) return false;
        if (i >= t.Length) return false;
        if (open)
        {
            if (t[i] != ')' && t[i] != '）') return false;
            i++;
        }
        else if (t[i] != '.' && t[i] != ')' && t[i] != '．') return false;
        else i++;
        if (i >= t.Length || !char.IsWhiteSpace(t[i])) return false;
        i++;
        while (i < t.Length && char.IsWhiteSpace(t[i])) i++;
        return i < t.Length;   // ≥1 não-branco após o marcador
    }

    internal static bool HasListContext(List<Line> comp)
    {
        int weak = 0;
        foreach (var l in comp)
        {
            if (IsStrongMarker(l) || IsExplicitWeak(l) || IsNumbered(l)) return true;  // RF-165
            if (IsWeakCandidate(l)) weak++;
        }
        return weak >= 2;
    }

    internal static bool IsListItem(Line l, bool context)
    {
        if (!context) return false;
        if (IsStrongMarker(l) || IsNumbered(l)) return true;
        if (IsWeakCandidate(l)) return true;   // em contexto, candidato basta
        return false;
    }

    // ---- RF-171..173: títulos ----

    public static bool IsExplicitTitle(Line l)
    {
        string t = l.Text.Trim();                                              // RF-171 🔒
        if (t.Length >= 2)
        {
            char a = t[0], b = t[^1];
            if ((a == '[' && b == ']') || (a == '<' && b == '>')
                || (a == '「' && b == '」') || (a == '『' && b == '』')) return true;
        }
        return t.EndsWith(":") || t.EndsWith("：");
    }

    internal static int NonBlank(string s)
    {
        int n = 0;
        foreach (char c in s) if (!char.IsWhiteSpace(c)) n++;
        return n;
    }

    internal static bool IsShort(Line l, bool removeSpaces)
    {
        int limit = removeSpaces ? Params.P41_ShortCharsNoSpaces : Params.P40_ShortChars;  // 🔒 6/10
        if (l.Orientation == LineOrientation.Vertical) limit -= Params.P42_VerticalDiscount; // 🔒 −3
        int sum = 0;
        foreach (var w in l.Words) sum += w.Text.Length;
        if (sum <= limit) return true;
        if (!removeSpaces && l.Words.Count <= Params.P43_ShortWords) return true;  // 🔒 3
        return false;
    }

    internal static bool IsContextTitle(Line first, Line? next, bool removeSpaces)
    {
        if (next is null) return false;                                        // RF-172 🔒
        if (first.Orientation != next.Orientation) return false;
        if (!IsShort(first, removeSpaces)) return false;
        int cur = NonBlank(first.Text), nxt = NonBlank(next.Text);
        return nxt >= System.Math.Ceiling(Params.P148_TitleLengthRatio * cur); // 🔒 1,5
    }

    // ---- RF-176/177: continuação e fim de frase ----

    internal static double Median(IReadOnlyList<int> v)
    {
        var s = new List<int>(v);
        s.Sort();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2.0;
    }

    internal static bool CanAppend(Block block, Line prev, Line cand)
    {
        if (!Adjacent(prev, cand)) return false;                               // 1
        if (cand.FontSize <= 0) return false;
        var sizes = new List<int>();
        foreach (var l in block.Lines) sizes.Add(l.FontSize);
        if (sizes.Count == 0) return false;
        double med = Median(sizes);
        if (med <= 0) return false;                                            // 2
        if (System.Math.Max(cand.FontSize, med) / System.Math.Min(cand.FontSize, med)
            > Params.P44_BlockAppendRatio) return false;                       // 3 🔒 1,2
        int big = cand.FontSize, small = cand.FontSize;
        foreach (int f in sizes) { big = System.Math.Max(big, f); small = System.Math.Min(small, f); }
        if (small <= 0) return false;
        return big / (double)small <= Params.P44_BlockAppendRatio;              // 4 🔒
    }

    public static bool EndsSentence(Line l)
    {
        string t = l.Text.TrimEnd();                                           // RF-177 🔒
        while (t.Length > 0 && IsCloser(t[^1])) t = t[..^1];
        if (t.Length == 0) return false;   // vazia ou só fechamentos
        char c = t[^1];
        foreach (var p in Params.P149_SentenceEnd)
            if (c.ToString() == p) return true;
        return false;
    }

    private static bool IsCloser(char c)
    {
        foreach (var m in Params.P45_ClosingChars)
            if (m.Length == 1 && m[0] == c) return true;
        return false;
    }

    // ---- RF-159..162, RF-174/175/178/179: agrupamento ----

    public static List<Block> Group(List<Line> lines, bool mergeLines,
        bool removeSpaces, bool oneLinePerBlock = false)
    {
        var blocks = new List<Block>();
        if (lines.Count == 0) return blocks;
        if (!mergeLines || oneLinePerBlock)                                    // RF-157
        {
            foreach (var l in lines) blocks.Add(NewBlock(l, false));
            return blocks;
        }

        var comps = Components(lines);                                         // RF-159
        comps.Sort((a, b) =>                                                   // RF-161
        {
            int ta = Top(a), tb = Top(b);
            if (ta != tb) return ta.CompareTo(tb);
            bool va = IsVerticalComp(a);
            if (va != IsVerticalComp(b)) return 0;
            return va ? Right(a).CompareTo(Right(b)) * -1 : Left(a).CompareTo(Left(b));
        });

        foreach (var comp in comps)
        {
            var ordered = new List<Line>(comp);
            if (IsVerticalComp(comp))                                          // RF-160 🔒
                ordered.Sort((a, b) => (a.X + a.W) != (b.X + b.W)
                    ? (b.X + b.W).CompareTo(a.X + a.W) : a.Y.CompareTo(b.Y));
            else
                ordered.Sort((a, b) => a.Y != b.Y
                    ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

            bool context = HasListContext(ordered);                            // RF-165
            Block? current = null;
            Line? prev = null;
            for (int i = 0; i < ordered.Count; i++)
            {
                var line = ordered[i];
                Line? next = i + 1 < ordered.Count ? ordered[i + 1] : null;
                bool item = IsListItem(line, context);                         // RF-170
                bool title = !item && (IsExplicitTitle(line)                   // RF-171/174
                    || (i == 0 && IsContextTitle(line, next, removeSpaces)));  // RF-172

                if (item)
                {
                    blocks.Add(NewBlock(line, false));
                    current = null; prev = line;
                    continue;
                }
                if (title)
                {
                    var b = NewBlock(line, true);
                    blocks.Add(b);
                    current = null; prev = line;
                    continue;
                }
                if (current is null || prev is null
                    || EndsSentence(prev)                                      // RF-175/177
                    || !CanAppend(current, prev, line))                        // RF-176
                {
                    current = NewBlock(line, false);
                    blocks.Add(current);
                }
                else current.Lines.Add(line);

                prev = line;
                if (EndsSentence(line)) current = null;                        // RF-178
            }
        }
        return blocks;
    }

    private static Block NewBlock(Line line, bool title)
    {
        var b = new Block { IsTitle = title, Orientation = line.Orientation };
        b.Lines.Add(line);
        SetBoxes(b);                                                           // RF-179
        return b;
    }

    internal static void SetBoxes(Block b)
    {
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
        foreach (var l in b.Lines)
        {
            x1 = System.Math.Min(x1, l.X); y1 = System.Math.Min(y1, l.Y);
            x2 = System.Math.Max(x2, l.X + l.W); y2 = System.Math.Max(y2, l.Y + l.H);
        }
        b.OX = b.VX = b.CX = x1; b.OY = b.VY = b.CY = y1;
        b.OW = b.VW = b.CW = x2 - x1; b.OH = b.VH = b.CH = y2 - y1;
    }

    // ---- união-busca por adjacência (RF-159) ----

    internal static List<List<Line>> Components(List<Line> lines)
    {
        int n = lines.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (Adjacent(lines[i], lines[j])) Union(i, j);
        var map = new Dictionary<int, List<Line>>();
        for (int i = 0; i < n; i++)
        {
            int r = Find(i);
            if (!map.TryGetValue(r, out var l)) { l = new List<Line>(); map[r] = l; }
            l.Add(lines[i]);
        }
        return new List<List<Line>>(map.Values);
    }

    private static bool IsVerticalComp(List<Line> c) =>
        c.Count > 0 && c[0].Orientation == LineOrientation.Vertical;

    private static int Top(List<Line> c)
    {
        int t = int.MaxValue;
        foreach (var l in c) t = System.Math.Min(t, l.Y);
        return t;
    }

    private static int Right(List<Line> c)
    {
        int r = int.MinValue;
        foreach (var l in c) r = System.Math.Max(r, l.X + l.W);
        return r;
    }

    private static int Left(List<Line> c)
    {
        int l = int.MaxValue;
        foreach (var x in c) l = System.Math.Min(l, x.X);
        return l;
    }
}
