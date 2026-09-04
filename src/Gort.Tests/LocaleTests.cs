using Gort.Locale;

namespace Gort.Tests;

public class LocaleTests
{
    [Fact]
    public void Csv_Handles_Quotes_Commas_Newlines()
    {
        var rows = Strings.ParseCsv(
            "key,pt-BR\na,simples\nb,\"com, vírgula\"\nc,\"com\nquebra\"\n");
        Assert.Equal(4, rows.Count);   // cabeçalho + 3
        Assert.Equal("simples", rows[1].Cols[0]);
        Assert.Equal("com, vírgula", rows[2].Cols[0]);   // RF-482
        Assert.Equal("com\nquebra", rows[3].Cols[0]);
    }

    [Fact]
    public void Missing_Key_Shows_Key()
    {
        Strings.Load("/nonexistent.csv", "pt-BR");
        Assert.Equal("alguma.chave", Strings.Get("alguma.chave"));  // RF-485
    }

    [Fact]
    public void PtBr_Loads()
    {
        string bin = AppContext.BaseDirectory;
        string[] cands =
        [
            Path.GetFullPath(Path.Combine(bin, "..", "..", "..", "..",
                "Gort", "Locale", "pt-BR.csv")),
            Path.Combine(bin, "Locale", "pt-BR.csv"),
            "C:\\Users\\lamph\\Desktop\\GORT\\src\\Gort\\Locale\\pt-BR.csv",
        ];
        string? found = null;
        foreach (var c in cands)
            if (System.IO.File.Exists(c)) { found = c; break; }
        Assert.NotNull(found);
        Strings.Load(found!, "pt-BR");
        Assert.Equal("Aplicar", Strings.Get("apply"));
        Assert.Equal("Configuração básica", Strings.Get("tab.basic"));  // RF-487
    }
}
