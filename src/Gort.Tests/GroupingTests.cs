using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gort.Ocr;
using Gort.Text;

namespace Gort.Tests;

/// <summary>Bateria do cap. 15 — "a parte mais fácil de quebrar sem perceber".</summary>
public class GroupingTests
{
    // Ordem de leitura: palavras em sequência; caixas (x, y, w, h).
    // Cada vetor interno = uma linha.
    private static OcrResult Ocr(params (string T, int X, int Y, int W, int H)[][] lines)
    {
        var r = new OcrResult();
        foreach (var line in lines)
        {
            foreach (var (t, x, y, w, h) in line)
                r.Words.Add(new OcrWord { Text = t, X = x, Y = y, W = w, H = h });
            r.WordsPerLine.Add(line.Length);
        }
        return r;
    }

    private static Line MkLine(string text, int x, int y, int w, int h, int font)
    {
        var l = new Line { Text = text, X = x, Y = y, W = w, H = h, FontSize = font };
        l.Orientation = Lines.Classify(w, h);
        l.Words.Add(new PlacedWord(text.TrimEnd(), x, y, w, h));
        return l;
    }

    private static Line H(string text, int x, int y, int w, int font = 20) =>
        MkLine(text, x, y, w, 26, font);

    // ---- critérios de aceite do capítulo ----

    [Fact]
    public void Dialogue_Three_Lines_Becomes_One_Block()
    {
        // Todas longas (nenhuma "curta"), mesma fonte, próximas, sem ponto.
        var lines = new List<Line>
        {
            H("we walked along the quiet road together ", 10, 10, 300),
            H("and talked about the journey ahead of us ", 10, 38, 300),
            H("while the sun was setting behind the hills ", 10, 66, 300),
        };
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        Assert.Single(blocks);
        Assert.Equal(3, blocks[0].Lines.Count);
    }

    [Fact]
    public void Short_Name_Above_Long_Dialogue_Is_Title()
    {
        var lines = new List<Line>
        {
            H("Ryu ", 10, 10, 60),                                            // curto
            H("we must go to the castle right now my friends ", 10, 40, 400), // ≥1,5×
        };
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        Assert.Equal(2, blocks.Count);
        Assert.True(blocks[0].IsTitle);
        Assert.False(blocks[1].IsTitle);
        Assert.Equal("Ryu ", blocks[0].Lines[0].Text);
    }

    [Fact]
    public void Five_Bullets_Become_Five_Blocks()
    {
        var lines = new List<Line>();
        for (int i = 0; i < 5; i++)
            lines.Add(H("• item number " + i + " ", 10, 10 + i * 30, 200));
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        Assert.Equal(5, blocks.Count);
    }

    [Fact]
    public void Vertical_Japanese_Reads_Right_To_Left()
    {
        // Três colunas verticais: mesma fonte, próximas → um componente;
        // ordem: coluna da direita primeiro.
        var lines = new List<Line>
        {
            MkLine("あ ", 200, 10, 24, 120, 22),
            MkLine("い ", 170, 10, 24, 120, 22),
            MkLine("う ", 140, 10, 24, 120, 22),
        };
        foreach (var l in lines) Assert.Equal(LineOrientation.Vertical, l.Orientation);
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: true);
        Assert.Single(blocks);
        Assert.Equal("あ ", blocks[0].Lines[0].Text);
        Assert.Equal("い ", blocks[0].Lines[1].Text);
        Assert.Equal("う ", blocks[0].Lines[2].Text);
    }

    [Fact]
    public void Sentence_End_Splits_Blocks()
    {
        var lines = new List<Line>
        {
            H("first sentence. ", 10, 10, 200),
            H("second sentence ", 10, 38, 200),
        };
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Merge_Toggle_Changes_Grouping_Not_Text()
    {
        var lines = new List<Line>
        {
            H("alpha beta ", 10, 10, 200),
            H("gamma delta ", 10, 38, 200),
        };
        var merged = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        var split = Grouping.Group(lines, mergeLines: false, removeSpaces: false);
        Assert.Single(merged);
        Assert.Equal(2, split.Count);
        Assert.Equal(merged[0].RawText, split[0].RawText + "\n" + split[1].RawText);
    }

    // ---- linhas ----

    [Fact]
    public void Line_Text_Has_Trailing_Space()
    {
        var reg = Lines.Build(Ocr(
            new[] { ("hi", 0, 0, 20, 20), ("there", 25, 0, 30, 20) }), 3, true);
        Assert.Single(reg.Lines);
        Assert.Equal("hi there ", reg.Lines[0].Text);      // RF-152 🔒
        Assert.Empty(reg.Blocks);   // Build só monta linhas; blocos vêm do Group
    }

    [Fact]
    public void Word_Box_Expands_Outward()
    {
        var (x, y, w, h) = Lines.ExpandBox(10.7, 5.2, 20.0, 9.9);  // RF-153
        Assert.Equal((10, 5, 21, 11), (x, y, w, h));
        var neg = Lines.ExpandBox(0, 0, -5, -5);
        Assert.Equal((0, 0, 0, 0), neg);
    }

    [Fact]
    public void Orientation_And_Font_Median()
    {
        Assert.Equal(LineOrientation.Vertical, Lines.Classify(20, 40));   // 40 > 20×1,5
        Assert.Equal(LineOrientation.Horizontal, Lines.Classify(40, 40));
        Assert.Equal(21, Lines.FontSizeOf(new List<(int, int)> { (20, 30), (22, 21), (100, 100) }));
        Assert.Equal(15, Lines.FontSizeOf(new List<(int, int)> { (10, 30), (20, 40) })); // par
        Assert.Equal(10, Lines.FontSizeOf(new List<(int, int)>()));       // P-38
        Assert.Equal(10, Lines.FontSizeOf(new List<(int, int)> { (0, 0) })); // inválida → P-38
        Assert.Equal(1, Lines.FontSizeOf(new List<(int, int)> { (1, 1), (1, 1) })); // piso 1
    }

    // ---- listas e títulos ----

    [Fact]
    public void Numbered_And_Weak_Markers()
    {
        Assert.True(Grouping.IsNumbered(H("1. first ", 0, 0, 50)));
        Assert.True(Grouping.IsNumbered(H("(a) second ", 0, 0, 50)));
        Assert.True(Grouping.IsNumbered(H("12) third ", 0, 0, 50)));
        Assert.False(Grouping.IsNumbered(H("1234. too long ", 0, 0, 50)));
        Assert.False(Grouping.IsNumbered(H("1. ", 0, 0, 50)));   // sem conteúdo
        Assert.True(Grouping.IsExplicitWeak(H("- dash ", 0, 0, 50)));
        Assert.False(Grouping.IsExplicitWeak(H("-dash ", 0, 0, 50)));
        // Sozinho, "-dash" não quebra (sem contexto); com par, vira lista.
        var solo = Grouping.Group(
            new List<Line> { H("a line ", 0, 0, 100), H("-dash ", 0, 30, 100) },
            mergeLines: true, removeSpaces: false);
        Assert.Single(solo);
        var pair = Grouping.Group(
            new List<Line> { H("- one ", 0, 0, 100), H("- two ", 0, 30, 100) },
            mergeLines: true, removeSpaces: false);
        Assert.Equal(2, pair.Count);
    }

    [Fact]
    public void Explicit_Titles()
    {
        Assert.True(Grouping.IsExplicitTitle(H("[chapter] ", 0, 0, 50)));
        Assert.True(Grouping.IsExplicitTitle(H("name: ", 0, 0, 50)));
        Assert.True(Grouping.IsExplicitTitle(H("名前： ", 0, 0, 50)));
        Assert.False(Grouping.IsExplicitTitle(H("plain line ", 0, 0, 50)));
    }

    [Fact]
    public void Sentence_End_With_Closers()
    {
        Assert.True(Grouping.EndsSentence(H("she said.\" ", 0, 0, 50)));
        Assert.True(Grouping.EndsSentence(H("really? ", 0, 0, 50)));
        Assert.True(Grouping.EndsSentence(H("行った。 ", 0, 0, 50)));
        Assert.False(Grouping.EndsSentence(H("no end ", 0, 0, 50)));
        Assert.False(Grouping.EndsSentence(H("   ", 0, 0, 50)));
        Assert.False(Grouping.EndsSentence(H(") ", 0, 0, 50)));
    }

    [Fact]
    public void Font_Ratio_Breaks_Block()
    {
        var lines = new List<Line>
        {
            H("normal text here ", 10, 10, 200, font: 20),
            H("BIG HEADER TEXT ", 10, 40, 200, font: 60),   // razão 3 > 1,2
        };
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void Short_Line_Rules()
    {
        // en: ≤3 palavras também é curta; ja (sem espaços): só caracteres (limite 6).
        var lines = new List<Line>
        {
            H("Bob ", 10, 10, 50),   // 1 palavra → curta
            H("this is a much longer dialogue line here ", 10, 40, 400),
        };
        var blocks = Grouping.Group(lines, mergeLines: true, removeSpaces: false);
        Assert.Equal(2, blocks.Count);
        Assert.True(blocks[0].IsTitle);
    }

    // ---- tratamento textual ----

    [Fact]
    public void RemoveSpaces_Before_Dict()
    {
        var b = new Block();
        b.Lines.Add(H("あ い う ", 0, 0, 60));
        var dict = new DictionaryStore();
        string t = TextPipeline.ForTranslation(b, removeSpaces: true, dict,
            dictActive: true, byWord: false, extraPasses: 0,
            overlayMode: false, oneLinePerBlock: false, isDbService: false);
        Assert.Equal("あいう", t);                               // RF-180 + RF-186
    }

    [Fact]
    public void Newlines_Kept_In_Overlay_Space_Outside()
    {
        var b = new Block();
        b.Lines.Add(H("line one ", 0, 0, 60));
        b.Lines.Add(H("line two ", 0, 30, 60));
        var dict = new DictionaryStore();
        string ov = TextPipeline.ForTranslation(b, false, dict, false, false, 0, true, false, false);
        string dark = TextPipeline.ForTranslation(b, false, dict, false, false, 0, false, false, false);
        Assert.Equal("line one \nline two ", ov);                // RF-187
        Assert.Equal("line one  line two ", dark);               // RF-186
    }

    [Fact]
    public void Dictionary_ByWord_And_Chained()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".txt");
        File.WriteAllText(path, "/s\ncolour\ncolor\n\n/s\na\nb\n\n/s\nb\nc\n\n");
        var dict = new DictionaryStore();
        dict.Load(path);
        Assert.Equal(3, dict.Count);
        Assert.Equal("color", dict.Apply("colour", byWord: true, extraPasses: 0));
        Assert.Equal("xcolorx", dict.Apply("xcolourx", byWord: false, extraPasses: 0));
        Assert.Equal("xcolourx", dict.Apply("xcolourx", byWord: true, extraPasses: 0));
        Assert.Equal("c", dict.Apply("a", byWord: true, extraPasses: 1));  // encadeada
        Assert.Equal("b", dict.Apply("a", byWord: true, extraPasses: 0));
        dict.AddPair("hello", "hi", path);                       // RF-184
        Assert.Equal(4, dict.Count);
    }

    [Fact]
    public void Overlay_Payload_And_Display_Format()
    {
        var b1 = new Block(); b1.Lines.Add(H("first ", 0, 0, 50));
        var b2 = new Block();                                    // vazio → pulado
        var dict = new DictionaryStore();
        string payload = TextPipeline.OverlayPayload(new[] { b1 }, "//////",
            false, dict, false, false, 0);
        Assert.Equal("\n//////first ", payload);                 // RF-188
        var regions = new List<(int, List<(string, string)>)>
        {
            (0, new List<(string, string)> { ("a", "A"), ("", "X"), ("b", TextPipeline.NoResultMarker) }),
            (1, new List<(string, string)> { ("c", "C") }),
        };
        Assert.Equal("1 : A\n2 : C", TextPipeline.FormatDisplay(regions, numbering: true));
        Assert.Equal("- A\n- C", TextPipeline.FormatDisplay(regions, numbering: false));
        var single = new List<(int, List<(string, string)>)>
            { (0, new List<(string, string)> { ("a", "A") }) };
        Assert.Equal("A", TextPipeline.FormatDisplay(single, numbering: true));  // RF-189
    }

    // ---- adjacência ----

    [Fact]
    public void Adjacency_Math()
    {
        var a = H("aaaa ", 10, 10, 100, font: 20);
        var near = H("bbbb ", 12, 38, 100, font: 20);   // intervalo 2 ≤ 25
        var far = H("cccc ", 12, 200, 100, font: 20);   // intervalo 164 > 25
        Assert.True(Grouping.Adjacent(a, near));
        Assert.False(Grouping.Adjacent(a, far));
        var diff = H("dddd ", 12, 38, 100, font: 40);   // razão 2 > 1,3
        Assert.False(Grouping.Adjacent(a, diff));
        var vert = MkLine("x ", 500, 10, 20, 100, 18);
        Assert.False(Grouping.Adjacent(a, vert));       // orientação difere
    }
}
