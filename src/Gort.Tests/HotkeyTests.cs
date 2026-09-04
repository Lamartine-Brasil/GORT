using Gort.Input;

namespace Gort.Tests;

public class HotkeyTests
{
    [Fact]
    public void Combo_Parse_Format()
    {
        var c = KeyCombo.Parse("Ctrl+Shift+Z");
        Assert.True(c.Ctrl && c.Shift && !c.Alt && !c.Win);
        Assert.Equal('Z', c.Key);
        Assert.Equal("Ctrl+Shift+Z", c.ToString());
        Assert.True(KeyCombo.Parse("").IsEmpty);
        Assert.True(KeyCombo.Parse("Ctrl+Shift+Alt+Win+X").ToString()
            .Split('+').Length <= 3);                        // RF-442
        Assert.True(KeyCombo.Parse("Ctrl+Bogus+Z").Key == 'Z');
    }

    [Fact]
    public void Matcher_Exact_Set_Any_Order()
    {
        var m = new HotkeyMatcher();
        m.Register("go", KeyCombo.Parse("Ctrl+Shift+Z"));
        Assert.Null(m.KeyDown(0x11));
        Assert.Null(m.KeyDown(0x10));
        Assert.Equal("go", m.KeyDown('Z'));                  // RF-438
    }

    [Fact]
    public void Matcher_Duplicate_First_Wins_Silently()
    {
        var m = new HotkeyMatcher();
        m.Register("first", KeyCombo.Parse("Ctrl+Z"));
        m.Register("second", KeyCombo.Parse("Ctrl+Z"));      // RF-439
        m.KeyDown(0x11);
        Assert.Equal("first", m.KeyDown('Z'));
    }

    [Fact]
    public void Matcher_Repeat_Ignored_Release_Clears()
    {
        var m = new HotkeyMatcher();
        m.Register("go", KeyCombo.Parse("Ctrl+Z"));
        m.KeyDown(0x11);
        Assert.Equal("go", m.KeyDown('Z'));
        Assert.Null(m.KeyDown('Z'));                         // RF-440
        m.KeyUp('Z');                                        // RF-441: limpa tudo
        Assert.Null(m.KeyDown('Z'));                         // só Z, sem Ctrl
        m.KeyUp('Z');
        m.KeyDown(0x11);
        Assert.Equal("go", m.KeyDown('Z'));
    }

    [Fact]
    public void Normalize_Merges_Sides()
    {
        Assert.Equal(0x10, Platform.Windows.WinHook.Normalize(0x10));
        Assert.Equal(0x11, Platform.Windows.WinHook.Normalize(0x11));
        Assert.Equal(0x12, Platform.Windows.WinHook.Normalize(0x12));
        Assert.Equal(0x5B, Platform.Windows.WinHook.Normalize(0x5C));
        Assert.Equal(0x41, Platform.Windows.WinHook.Normalize(0x41));
    }

    [Fact]
    public void Guard_Suspends()
    {
        HotkeyGuard.CaptureFieldFocused = true;
        Assert.True(HotkeyGuard.Suspended);
        HotkeyGuard.CaptureFieldFocused = false;
        Regions.InputGuard.SelectionOpen = true;
        Assert.True(HotkeyGuard.Suspended);                  // RF-443
        Regions.InputGuard.SelectionOpen = false;
        Assert.False(HotkeyGuard.Suspended);
    }
}
