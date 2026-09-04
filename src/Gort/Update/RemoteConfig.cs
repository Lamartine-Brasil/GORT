using Gort.Translate;

namespace Gort.Update;

/// <summary>
/// Configuração padrão remota (RF-417 🔒): tokens, token avançado e
/// parâmetros do navegador embutido. Ausentes/vazios mantêm o embutido (RF-418).
/// </summary>
public static class RemoteConfig
{
    public static void Apply(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (!line.StartsWith("{")) continue;
            int end = line.IndexOf('}');
            if (end < 0) continue;
            string key = line[1..end];
            string val = line[(end + 1)..].Trim();
            if (val == "") continue;                              // RF-418
            if (key == "token-default") RemoteDefaults.DefaultToken = val;
            else if (key == "token-browser") RemoteDefaults.BrowserToken = val;
            else if (key == "advanced-token")
                RemoteDefaults.AdvancedToken = val == "1" || val.ToLower() == "true";
            else if (key == "browser-url") RemoteDefaults.BrowserUrlFormat = val;
            else if (key == "browser-script") RemoteDefaults.BrowserExtractScript = val;
        }
    }
}
