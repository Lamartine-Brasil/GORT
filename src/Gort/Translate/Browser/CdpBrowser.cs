using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Gort.Translate.Browser;

/// <summary>
/// Edge headless via Chrome DevTools Protocol (sem dependências): navega e
/// avalia script de extração. Processo próprio com perfil temporário.
/// </summary>
public sealed class CdpBrowser : IDisposable
{
    private Process? _proc;
    private int _port;
    private string _dir = "";
    private static readonly HttpClient Http = new();

    public static string? FindEdge()
    {
        string[] cands =
        [
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\Application\msedge.exe"),
        ];
        foreach (var c in cands)
            if (File.Exists(c)) return c;
        return null;
    }

    public async Task EnsureStartedAsync(CancellationToken ct)
    {
        if (_proc is not null && !_proc.HasExited) return;
        string? edge = FindEdge()
            ?? throw new InvalidOperationException("Microsoft Edge não encontrado.");
        _dir = Path.Combine(Path.GetTempPath(), "gort-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _proc = new Process
        {
            StartInfo = new ProcessStartInfo(edge,
                "--headless=new --disable-gpu --no-first-run --no-default-browser-check " +
                $"--remote-debugging-port=0 --user-data-dir=\"{_dir}\"")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        if (!_proc.Start()) throw new InvalidOperationException("Edge não iniciou.");
        // A porta sai no stderr: "DevTools listening on ws://127.0.0.1:PORT/…".
        string? line = null;
        var until = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < until)
        {
            line = await _proc.StandardError.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is not null && line.Contains("DevTools listening on ws://127.0.0.1:"))
                break;
        }
        if (line is null)
            throw new InvalidOperationException("Depurador do Edge não respondeu.");
        int p1 = line.LastIndexOf(':') + 1, p2 = line.IndexOf('/', p1);
        // stderr malformado não pode derrubar o laço com exceção obscura.
        if (p1 <= 0 || p2 <= p1 || !int.TryParse(line[p1..p2], out _port))
            throw new InvalidOperationException("Depurador do Edge não respondeu.");
    }

    /// <summary>Navega num alvo novo (campo limpo) e extrai até estabilizar.</summary>
    public async Task<string> TranslatePageAsync(string url, string extractScript,
        string previous, TimeSpan timeout, CancellationToken ct)
    {
        await EnsureStartedAsync(ct).ConfigureAwait(false);
        string targetId = await NewTargetAsync(url, ct).ConfigureAwait(false);
        try
        {
            string ws = await TargetWsAsync(targetId, ct).ConfigureAwait(false);
            using var sock = new ClientWebSocket();
            await sock.ConnectAsync(new Uri(ws), ct).ConfigureAwait(false);
            var cdp = new CdpSession(sock);
            // Observa a falha em vez de largar a Task (falha de socket sumia).
            _ = cdp.RunAsync(ct).ContinueWith(
                t => System.Diagnostics.Trace.WriteLine("GORT cdp: " + t.Exception?.Message),
                TaskContinuationOptions.OnlyOnFaulted);
            await cdp.SendAsync("Page.enable", new Dictionary<string, object>(), ct)
                .ConfigureAwait(false);
            var loaded = new TaskCompletionSource<bool>();
            cdp.OnEvent += m => { if (m == "Page.loadEventFired") loaded.TrySetResult(true); };
            await cdp.SendAsync("Page.navigate",
                new Dictionary<string, object> { ["url"] = url }, ct).ConfigureAwait(false);
            using var loadCts = new CancellationTokenSource(timeout);
            using var reg = ct.Register(() => loadCts.Cancel());
            try { await loaded.Task.WaitAsync(loadCts.Token).ConfigureAwait(false); }
            catch (TimeoutException) { }
            catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
            // Sondagem do resultado (RF-263): diferente do anterior e do sentinela.
            var until = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < until)
            {
                ct.ThrowIfCancellationRequested();
                string cur = await EvaluateAsync(cdp, extractScript, ct).ConfigureAwait(false);
                if (cur != "" && cur != previous) return cur;
                await Task.Delay(80, ct).ConfigureAwait(false);   // P-137
            }
            throw new TimeoutException("Tempo esgotado aguardando a página.");
        }
        finally { await CloseTargetAsync(targetId, ct).ConfigureAwait(false); }
    }

    private async Task<string> NewTargetAsync(string url, CancellationToken ct)
    {
        using var resp = await Http.PutAsync(
            $"http://127.0.0.1:{_port}/json/new?{Uri.EscapeDataString(url)}",
            null, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)
            .ConfigureAwait(false));
        return doc.RootElement.GetProperty("id").GetString() ?? "";
    }

    private async Task<string> TargetWsAsync(string id, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(
            $"http://127.0.0.1:{_port}/json/list", ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct)
            .ConfigureAwait(false));
        foreach (var t in doc.RootElement.EnumerateArray())
            if (t.GetProperty("id").GetString() == id)
                return t.GetProperty("webSocketDebuggerUrl").GetString() ?? "";
        throw new InvalidOperationException("Alvo CDP sumiu.");
    }

    private async Task CloseTargetAsync(string id, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(
                $"http://127.0.0.1:{_port}/json/close/{id}", ct).ConfigureAwait(false);
        }
        catch { }
    }

    private static async Task<string> EvaluateAsync(CdpSession cdp, string script,
        CancellationToken ct)
    {
        using var doc = await cdp.SendAsync("Runtime.evaluate",
            new Dictionary<string, object>
            {
                ["expression"] = script,
                ["returnByValue"] = true,
            }, ct).ConfigureAwait(false);
        try
        {
            if (doc.RootElement.TryGetProperty("result", out var r)
                && r.TryGetProperty("result", out var rr)
                && rr.TryGetProperty("value", out var v))
                return v.GetString() ?? "";
        }
        catch { }
        return "";
    }

    public void Dispose()
    {
        try { _proc?.Kill(); } catch { }
        try { _proc?.Dispose(); } catch { }
        _proc = null;
        try { if (_dir.Length > 0) Directory.Delete(_dir, true); } catch { }
    }

    private sealed class CdpSession
    {
        private readonly ClientWebSocket _sock;
        private int _id;
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _wait = new();
        public event Action<string>? OnEvent;

        public CdpSession(ClientWebSocket sock) => _sock = sock;

        public async Task RunAsync(CancellationToken ct)
        {
            var buf = new byte[65536];
            var sb = new StringBuilder();
            try
            {
                while (!ct.IsCancellationRequested
                    && _sock.State == WebSocketState.Open)
                {
                    var seg = await _sock.ReceiveAsync(buf, ct).ConfigureAwait(false);
                    if (seg.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, seg.Count));
                    if (!seg.EndOfMessage) continue;
                    string msg = sb.ToString();
                    sb.Clear();
                    using var doc = JsonDocument.Parse(msg);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("id", out var id))
                    {
                        int i = id.GetInt32();
                        if (_wait.TryRemove(i, out var tcs))
                            tcs.TrySetResult(JsonDocument.Parse(msg));
                    }
                    else if (root.TryGetProperty("method", out var m))
                        OnEvent?.Invoke(m.GetString() ?? "");
                }
            }
            catch { }
        }

        public async Task<JsonDocument> SendAsync(string method,
            Dictionary<string, object> pars, CancellationToken ct)
        {
            int id = Interlocked.Increment(ref _id);
            var tcs = new TaskCompletionSource<JsonDocument>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _wait[id] = tcs;
            string json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = id, ["method"] = method, ["params"] = pars,
            });
            await _sock.SendAsync(Encoding.UTF8.GetBytes(json),
                WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            using (ct.Register(() => tcs.TrySetCanceled()))
                return await tcs.Task.ConfigureAwait(false);
        }
    }
}
