using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Store;

namespace Gort.Translate;

/// <summary>
/// Tradutor por planilha em nuvem (VI.4, RF-255..259): escreve o texto numa
/// linha aleatória com fórmula de tradução ao lado e lê o resultado. Único
/// com ponte no nível de serviço (RF-259). Token de delegação local (RF-256).
/// </summary>
public sealed class SheetsTranslator : HttpTranslator
{
    public override string Id => "sheets";
    public override string Display => "Tradutor por planilha em nuvem";
    public override string DefaultToken => RemoteDefaults.DefaultToken;
    public override bool SupportsBridge => true;   // RF-259: único

    public const string TabName = "GORT";
    private const string Scope = "https://www.googleapis.com/auth/spreadsheets";

    private readonly Func<string> _sheetId;
    private readonly Func<bool> _bridge;
    private readonly string _tokenFile;

    public SheetsTranslator(Func<string> sheetId, Func<bool> bridge,
        HttpMessageHandler? handler = null)
        : base(handler, 30000)
    {
        _sheetId = sheetId;
        _bridge = bridge;
        _tokenFile = Path.Combine(Paths.BaseDir, "sheets-token.json");
    }

    public static string? LoadRefreshToken(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("refresh_token", out var t)
                ? t.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>RF-200? Não — RF-256: comando para apagar todos os tokens.</summary>
    public void ClearTokens()
    {
        try { if (File.Exists(_tokenFile)) File.Delete(_tokenFile); } catch { }
    }

    public bool HasToken() => LoadRefreshToken(_tokenFile) is not null;

    /// <summary>
    /// Troca o código de autorização (colado pelo usuário) pelos tokens e
    /// grava em sheets-token.json. Sem isso a autenticação não se completa.
    /// </summary>
    public async Task<bool> ExchangeCodeAsync(string clientId, string clientSecret,
        string code, string redirectUri, CancellationToken ct)
    {
        using var resp = await PostFormAsync("https://oauth2.googleapis.com/token",
            new Dictionary<string, string>
            {
                ["code"] = code,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code",
            }, new Dictionary<string, string>(), ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return false;
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("refresh_token", out var rt)) return false;
            string tmp = _tokenFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["refresh_token"] = rt.GetString() ?? "",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            }));
            File.Move(tmp, _tokenFile, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    public static string ConsentUrl(string clientId, string redirectUri) =>
        "https://accounts.google.com/o/oauth2/v2/auth?response_type=code" +
        $"&client_id={Uri.EscapeDataString(clientId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&scope={Uri.EscapeDataString(Scope)}&access_type=offline&prompt=consent";

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        string sheet = _sheetId();
        if (string.IsNullOrWhiteSpace(sheet))
            return Fail("Informe o endereço da planilha.");
        string? refresh = LoadRefreshToken(_tokenFile);
        if (refresh is null)
            return Fail("Sem token de planilha. Autentique primeiro.");
        try
        {
            string access = await RefreshAsync(refresh, ct).ConfigureAwait(false);
            await EnsureTabAsync(sheet, access, ct).ConfigureAwait(false);
            // RF-259: ponte no nível de serviço.
            if (_bridge() && srcCode != "ja")
            {
                string mid = await CellTranslateAsync(sheet, access,
                    texts.Count > 0 ? texts[0] : "", srcCode, "ja", ct).ConfigureAwait(false);
                string fin = await CellTranslateAsync(sheet, access, mid, "ja", dstCode, ct)
                    .ConfigureAwait(false);
                return new ServiceResult { Translations = new List<string> { fin } };
            }
            string out0 = await CellTranslateAsync(sheet, access,
                texts.Count > 0 ? texts[0] : "", srcCode, dstCode, ct).ConfigureAwait(false);
            return new ServiceResult { Translations = new List<string> { out0 } };
        }
        catch (OperationCanceledException) { throw; }
        catch (SheetsException ex) { return Fail(ex.Message); }
        catch (Exception ex) { return Fail("Falha de processamento: " + ex.Message); }
    }

    private async Task<string> RefreshAsync(string refresh, CancellationToken ct)
    {
        string clientId = "", clientSecret = "";
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_tokenFile));
            clientId = doc.RootElement.GetProperty("client_id").GetString() ?? "";
            clientSecret = doc.RootElement.GetProperty("client_secret").GetString() ?? "";
        }
        catch { }
        using var resp = await PostFormAsync("https://oauth2.googleapis.com/token",
            new Dictionary<string, string>
            {
                ["refresh_token"] = refresh,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "refresh_token",
            }, new Dictionary<string, string>(), ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc2 = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)
            .ConfigureAwait(false));
        return doc2.RootElement.GetProperty("access_token").GetString() ?? "";
    }

    /// <summary>RF-255/256: linha aleatória 1..P-57, apóstrofo anti-fórmula.</summary>
    internal static (int Row, string Formula) BuildCell(string text, string src, string dst)
    {
        int row = Random.Shared.Next(1, Params.P57_SheetMinRows + 1);   // 🔒 1..50
        string formula = $"=GOOGLETRANSLATE(A{row},\"{src}\",\"{dst}\")";
        return (row, formula);
    }

    private async Task EnsureTabAsync(string sheet, string access, CancellationToken ct)
    {
        string meta = await GetRawAsync(
            $"https://sheets.googleapis.com/v4/spreadsheets/{sheet}" +
            "?fields=sheets.properties", access, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(meta);
        bool has = false;
        int rows = 0;
        if (doc.RootElement.TryGetProperty("sheets", out var sheets))
            foreach (var s in sheets.EnumerateArray())
            {
                var pr = s.GetProperty("properties");
                if (pr.GetProperty("title").GetString() == TabName)
                {
                    has = true;
                    if (pr.TryGetProperty("gridProperties", out var gp)
                        && gp.TryGetProperty("rowCount", out var rc))
                        rows = rc.GetInt32();
                }
            }
        if (!has)
        {
            await PostSheetsAsync(sheet + ":batchUpdate",
                "{\"requests\":[{\"addSheet\":{\"properties\":{\"title\":\"" + TabName + "\"}}}]}",
                access, ct).ConfigureAwait(false);
            rows = 0;
        }
        if (rows < Params.P57_SheetMinRows)
        {
            await PostSheetsAsync(sheet + ":batchUpdate",
                "{\"requests\":[{\"updateSheetProperties\":{\"properties\":" +
                "{\"sheetId\":0,\"gridProperties\":{\"rowCount\":" + Params.P57_SheetMinRows +
                ",\"columnCount\":2}},\"fields\":\"gridProperties.rowCount,gridProperties.columnCount\"}}]}",
                access, ct).ConfigureAwait(false);
        }
    }

    private async Task<string> CellTranslateAsync(string sheet, string access,
        string text, string src, string dst, CancellationToken ct)
    {
        var (row, formula) = BuildCell(text, src, dst);
        await PutAsync(sheet, access, $"{TabName}!A{row}", "'" + text, ct)
            .ConfigureAwait(false);
        await PutAsync(sheet, access, $"{TabName}!B{row}", formula, ct, userEntered: true)
            .ConfigureAwait(false);
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(500, ct).ConfigureAwait(false);
            string val = await GetCellAsync(sheet, access,
                $"{TabName}!B{row}", ct).ConfigureAwait(false);
            if (val.StartsWith("#VALUE", StringComparison.Ordinal))
                throw new SheetsException("");   // RF: erro de valor, sem mensagem
            if (val.Length > 0 && !val.StartsWith("#")) return val;
        }
        throw new SheetsException("A planilha não respondeu a tempo.");
    }

    private async Task<string> GetCellAsync(string sheet, string access,
        string range, CancellationToken ct)
    {
        string api = $"https://sheets.googleapis.com/v4/spreadsheets/{sheet}/values/" +
            Uri.EscapeDataString(range);
        string body = await GetRawAsync(api, access, ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("values", out var vs)
                && vs.GetArrayLength() > 0 && vs[0].GetArrayLength() > 0)
                return vs[0][0].GetString() ?? "";
            return "";
        }
        catch { return ""; }
    }

    private async Task<string> GetRawAsync(string url, string access, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        using var resp = await Http.SendAsync(req, linked.Token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)   // RF: planilha inexistente
            throw new SheetsException("Planilha inexistente. Confira o endereço.");
        if (!resp.IsSuccessStatusCode)
            throw new SheetsException($"HTTP {(int)resp.StatusCode}");
        return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private async Task PutAsync(string sheet, string access, string range,
        string value, CancellationToken ct, bool userEntered = false)
    {
        using var timeout = new CancellationTokenSource(TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        string api = $"https://sheets.googleapis.com/v4/spreadsheets/{sheet}/values/" +
            $"{Uri.EscapeDataString(range)}?valueInputOption=" +
            (userEntered ? "USER_ENTERED" : "RAW");
        using var req = new HttpRequestMessage(HttpMethod.Put, api)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["values"] = new object[][] { new object[] { value } },
                }), System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
        using var resp = await Http.SendAsync(req, linked.Token).ConfigureAwait(false);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new SheetsException("Planilha inexistente. Confira o endereço.");
        if (!resp.IsSuccessStatusCode)
            throw new SheetsException($"HTTP {(int)resp.StatusCode}");
    }

    private async Task PostSheetsAsync(string path, string json, string access,
        CancellationToken ct)
    {
        string api = $"https://sheets.googleapis.com/v4/spreadsheets/{path}";
        await PostJsonAsync(api, json,
            new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + access,
            }, ct).ConfigureAwait(false);
    }

    public sealed class SheetsException : Exception
    {
        public SheetsException(string m) : base(m) { }
    }
}
