using System.Collections.Generic;
using Gort.UI;

namespace Gort.Tests;

public class OverlayTests
{
    private static OverlayLayout.Item It(float x, float y, float w, float h,
        bool title = false, string text = "abc")
    {
        var it = new OverlayLayout.Item
        {
            Area = 0, Title = title, Text = text,
            X = x, Y = y, W = w, H = h,
        };
        OverlayLayout.ApplyContent(it, outline: true);
        return it;
    }

    [Fact]
    public void Origin_Math()
    {
        var (x, y, w, h) = OverlayLayout.Origin(100, 200, 5.5f, 20, 30, 40, 50, 2.0, 90, 190);
        Assert.Equal(14.5f, x, precision: 3);   // RF-352
        Assert.Equal(19.5f, y, precision: 3);
        Assert.Equal(20, w);
        Assert.Equal(25, h);
    }

    [Fact]
    public void Collisions_Separate()
    {
        var items = new List<OverlayLayout.Item>
        {
            It(0, 0, 100, 50),
            It(50, 10, 100, 50),
        };
        OverlayLayout.ResolveCollisions(items);
        float w = Math.Max(0, Math.Min(items[0].X + items[0].W, items[1].X + items[1].W)
            - Math.Max(items[0].X, items[1].X));
        float h = Math.Max(0, Math.Min(items[0].Y + items[0].H, items[1].Y + items[1].H)
            - Math.Max(items[0].Y, items[1].Y));
        Assert.Equal(0, w * h);   // RF-355: sem sobreposição restante
    }

    [Fact]
    public void Title_Preserves_Rect()
    {
        var title = It(0, 0, 60, 30, title: true);
        var other = It(30, 5, 100, 40);
        OverlayLayout.ResolveCollisions(new List<OverlayLayout.Item> { title, other });
        Assert.Equal(60, title.W);   // RF-357: título intacto
        Assert.Equal(30, title.H);
    }

    [Fact]
    public void Preferred_Rule()
    {
        Assert.Equal(30, OverlayLayout.Preferred(30, 20, isTitle: true, isLeader: false));
        Assert.Equal(30, OverlayLayout.Preferred(30, 20, false, true));   // 30 ≥ 1,3×20
        Assert.Equal(20, OverlayLayout.Preferred(22, 20, false, true));   // 22 < 26
        Assert.Equal(20, OverlayLayout.Preferred(40, 20, false, false));
    }

    [Fact]
    public void FindFont_Shortcut_And_Bisect()
    {
        var (s1, ok1) = OverlayLayout.FindFont(20, 10, _ => true);
        Assert.Equal(20, s1);   // RF-363: testa o preferido direto
        Assert.True(ok1);
        var (s2, ok2) = OverlayLayout.FindFont(20, 10, s => s <= 12);
        Assert.True(ok2);
        Assert.InRange(s2, 11.75, 12);   // precisão P-97
        var (_, ok3) = OverlayLayout.FindFont(20, 10, _ => false);
        Assert.False(ok3);
    }

    [Fact]
    public void Wrap_Binary_Search()
    {
        // Monoespaçada 10 px, folga 1,2×10=12: largura 42 → 3 caracteres.
        var lines = OverlayLayout.Wrap("abcdef", 42, 10, t => t.Length * 10);
        Assert.Equal(new List<string> { "abc", "def" }, lines);   // RF-369
        var tiny = OverlayLayout.Wrap("ab", 5, 10, t => t.Length * 10);
        Assert.Equal(2, tiny.Count);   // RF-370: nem 1 cabe, progride
        var expl = OverlayLayout.Wrap("a\nb", 500, 10, t => t.Length * 10);
        Assert.Equal(new List<string> { "a", "b" }, expl);   // RF-372
    }

    [Fact]
    public void Place_Fits_And_Skips_Blanks()
    {
        var it = It(0, 0, 200, 100, text: "hello world test");
        it.CX = 0; it.CY = 0; it.CW = 200; it.CH = 100;
        var (ok, placed) = OverlayLayout.Place(it, 20, t => t.Length * 10, t => 20, false);
        Assert.True(ok);   // RF-364
        Assert.NotEmpty(placed);
        var small = It(0, 0, 200, 100, text: "hello world test");
        small.CX = 0; small.CY = 0; small.CW = 30; small.CH = 100;
        var (ok2, _) = OverlayLayout.Place(small, 20, t => t.Length * 10, t => 20, false);
        Assert.False(ok2);
        var blank = It(0, 0, 200, 100, text: "   ");
        blank.CX = 0; blank.CY = 0; blank.CW = 200; blank.CH = 100;
        var (ok3, placed3) = OverlayLayout.Place(blank, 20, t => t.Length * 10, t => 20, false);
        Assert.True(ok3);   // RF-367: só-espaços ignoradas
        Assert.Empty(placed3);
    }

    [Fact]
    public void Content_Shrinks_With_Outline()
    {
        var it = new OverlayLayout.Item { X = 0, Y = 0, W = 100, H = 50 };
        OverlayLayout.ApplyContent(it, outline: true);
        Assert.Equal(4f, it.CX); Assert.Equal(4f, it.CY);
        Assert.Equal(92f, it.CW); Assert.Equal(42f, it.CH);   // RF-359 P-93
        var it2 = new OverlayLayout.Item { X = 0, Y = 0, W = 100, H = 50 };
        OverlayLayout.ApplyContent(it2, outline: false);
        Assert.Equal(0f, it2.CX); Assert.Equal(0f, it2.CY);
        Assert.Equal(100f, it2.CW); Assert.Equal(50f, it2.CH);
    }

    [Fact]
    public void Expand_Grows_While_Fitting()
    {
        float got = OverlayLayout.ExpandToFit(50, 200, w => w <= 120);
        Assert.InRange(got, 119, 120);   // RF-362: busca binária
    }
}
