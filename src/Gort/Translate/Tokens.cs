namespace Gort.Translate;

using System.Collections.Generic;

/// <summary>
/// Padrões remotos (RF-417): tokens, clientes e endpoints vêm do arquivo de
/// configuração remota na Etapa 18; até lá, os valores embutidos abaixo.
/// Coluna Exposto = REMOTO na Parte IV.
/// </summary>
public static class RemoteDefaults
{
    public static string DefaultToken { get; set; } = Core.Params.P51_DefaultToken;   // 🔒 //////
    public static string BrowserToken { get; set; } = Core.Params.P52_BrowserToken;   // 🔒 @@@@@@
    public static bool AdvancedToken { get; set; } = Core.Params.P151_AdvancedToken;  // 🔒 off

    public static string WebHighClient { get; set; } = "gtx";
    public static string WebLowClient { get; set; } = "webapp";
    public static string WebEndpoint { get; set; } =
        "https://translate.googleapis.com/translate_a/single";

    /// <summary>Prefixo visível do modo de baixa qualidade (RF-247).</summary>
    public static string LowQualityPrefix { get; set; } = "[baixa qualidade] ";

    /// <summary>Modelos de linguagem (dado, RF-279) + padrão.</summary>
    public static List<string> LlmModels { get; set; } = new()
    {
        "gemini-2.0-flash", "gemini-2.0-pro", "gemini-1.5-flash", "gemini-1.5-pro",
    };
    public static string LlmDefaultModel { get; set; } = "gemini-2.0-flash";

    /// <summary>Navegador embutido: formato da URL e script (RF-270, REMOTO).</summary>
    public static string BrowserUrlFormat { get; set; } =
        "https://translate.google.com/?sl={src}&tl={dst}&text={text}&op=translate";
    public static string BrowserExtractScript { get; set; } =
        "(()=>{const el=document.querySelector('[data-result-index]');return el?el.innerText:''})()";
}

/// <summary>
/// Token avançado (RF-234 🔒): envia encurtado e tolera alteração na volta.
/// </summary>
public static class TokenHelper
{
    /// <summary>Encurta: −3 do início se tem ≥7, −2 se tem 6.</summary>
    public static string Shorten(string token, bool advanced)
    {
        if (!advanced) return token;
        if (token.Length >= 7) return token[3..];
        if (token.Length == 6) return token[2..];
        return token;
    }

    /// <summary>
    /// Limpa cada parte: remove das pontas as repetições do primeiro caractere
    /// do token; descarta partes que ficarem vazias.
    /// </summary>
    public static List<string> CleanParts(string[] parts, string token, bool advanced)
    {
        var list = new List<string>();
        foreach (var p in parts)
        {
            string s = p;
            if (advanced && token.Length > 0)
            {
                char c = token[0];
                int a = 0, b = s.Length;
                while (a < b && s[a] == c) a++;
                while (b > a && s[b - 1] == c) b--;
                s = s[a..b];
                if (s.Length == 0) continue;   // descarta esvaziadas
            }
            list.Add(s);
        }
        return list;
    }
}
