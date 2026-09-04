using System.Collections.Generic;
using System.IO;
using Gort.Translate;
using Gort.UI;
using Gort.Update;

namespace Gort.Tests;

public class Update18Tests
{
    [Fact]
    public void Version_Parse_And_Minor_Major()
    {
        var vf = VersionFile.Parse(
            "[app]\n{version}1.4.0\n{inline-min}1.2.0\n{url-exe}http://x/y.exe\n" +
            "{url-sum}http://x/y.sha\n{url-notes}http://x/n\n" +
            "[dicts]\n{dict-ja}3 http://x/ja.txt\n");
        Assert.Equal("1.4.0", vf.Version);
        Assert.True(VersionFile.IsMinor("1.2.5", "1.4.0", "1.2.0"));   // RF-420
        Assert.False(VersionFile.IsMinor("1.0.0", "1.4.0", "1.2.0"));  // maior
        Assert.False(VersionFile.IsMinor("1.4.0", "1.4.0", "1.2.0"));  // sem novidade
        Assert.Equal(("3", "http://x/ja.txt"), vf.Dicts["ja"]);
        Assert.Equal("https://x/y.exe", VersionFile.ForceHttps("http://x/y.exe"));  // RF-424
        var bad = VersionFile.Parse("lixo\n{sem-fechar\n[app]\n{version}");
        Assert.Equal("", bad.Version);                                 // malformado: nada
    }

    [Fact]
    public void Remote_Config_Applies_And_Keeps_Missing()
    {
        string keep = RemoteDefaults.DefaultToken;
        bool keepAdv = RemoteDefaults.AdvancedToken;
        try
        {
            RemoteConfig.Apply("{token-default}@@@\n{browser-url}\n");
            Assert.Equal("@@@", RemoteDefaults.DefaultToken);              // RF-417
            RemoteConfig.Apply("{advanced-token}1\n");
            Assert.True(RemoteDefaults.AdvancedToken);
        }
        finally
        {
            RemoteDefaults.DefaultToken = keep;                            // restaura
            RemoteDefaults.AdvancedToken = keepAdv;
        }
    }

    [Fact]
    public void FailMarker_NoWait_When_Absent()
    {
        try { File.Delete(FailMarker.Path); } catch { }
        Assert.False(FailMarker.InWait());                             // RF-429
        FailMarker.Write();
        Assert.True(FailMarker.InWait());                              // RF-428
        File.WriteAllText(FailMarker.Path, "lixo");
        Assert.False(FailMarker.InWait());                             // malformado
        try { File.Delete(FailMarker.Path); } catch { }
    }

    [Fact]
    public void Sha_Verify()
    {
        string f = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllBytes(f, new byte[] { 1, 2, 3 });
        using var sha = System.Security.Cryptography.SHA256.Create();
        string hex = Convert.ToHexString(sha.ComputeHash(new byte[] { 1, 2, 3 }));
        Assert.True(VersionFile.ShaOk(f, hex));
        Assert.True(VersionFile.ShaOk(f, hex.ToLowerInvariant()));  // RF-427
        Assert.False(VersionFile.ShaOk(f, "00"));
        Assert.False(VersionFile.ShaOk(f + "-nope", "00"));
    }

    [Fact]
    public void Move_Retry_Works()
    {
        string a = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string b = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(a, "x");
        UpdateHelper.MoveWithRetry(a, b);                              // RF-430
        Assert.True(File.Exists(b));
        Assert.False(File.Exists(a));
    }

    [Fact]
    public void Remote_Matches_Config()
    {
        Assert.True(Updater.RemoteConfigMatches("{app-version}1.2.3\n", "1.2.3"));  // RF-422
        Assert.False(Updater.RemoteConfigMatches("{app-version}1.2.4\n", "1.2.3"));
        Assert.False(Updater.RemoteConfigMatches("nada\n", "1.2.3"));
    }

    [Fact]
    public void Community_Info_Parse()
    {
        var e = new CommunityWindow.Entry();
        CommunityWindow.ParseInfo(e,
            "title: Jogo X\nlinks: http://a http://b\ndesc: Um jogo.\n" +
            "profile: jogo.toml\ndb: jogo.txt\n");
        Assert.Equal("Jogo X", e.InfoTitle);
        Assert.Equal("jogo.toml", e.Profile);
        Assert.Equal("jogo.txt", e.Db);
    }

    [Fact]
    public void Portrait_Writes_Schema()
    {
        var b = new Text.Block();
        b.Lines.Add(new Text.Line { Text = "oi ", X = 1, Y = 2, W = 3, H = 4 });
        b.OX = 1; b.OY = 2; b.OW = 3; b.OH = 4;
        var areas2 = new List<(int, bool, Platform.ScreenRect, Platform.ScreenRect,
            System.Collections.Generic.List<Text.Block>,
            System.Collections.Generic.List<string>)>
        {
            (0, false, new Platform.ScreenRect(0, 0, 10, 10),
                new Platform.ScreenRect(0, 0, 10, 10),
                new System.Collections.Generic.List<Text.Block> { b },
                new System.Collections.Generic.List<string> { "OLÁ" }),
        };
        string path = Debug.AnalysisPortrait.Write("dark", "modern", "web-free", areas2);
        Assert.True(File.Exists(path));                                // RF-492
        string json = File.ReadAllText(path);
        Assert.Contains("window", json);
        Assert.Contains("dark", json);
        Assert.Contains("areas", json);
        Assert.Contains("OLÁ", json);
    }
}
