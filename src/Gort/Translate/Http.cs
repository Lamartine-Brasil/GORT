using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Gort.Translate;

/// <summary>Base HTTP dos tradutores (timeout por serviço, cancelamento limpo).</summary>
public abstract class HttpTranslator : ITranslationService, IDisposable
{
    public abstract string Id { get; }
    public abstract string Display { get; }
    public abstract string DefaultToken { get; }
    public virtual bool SupportsBridge => false;
    public virtual bool UsesResultMemory => true;
    public virtual bool UsesCollectanea => true;

    protected readonly HttpClient Http;
    protected readonly int TimeoutMs;
    private bool _disposed;

    protected HttpTranslator(HttpMessageHandler? handler, int timeoutMs)
    {
        Http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        TimeoutMs = timeoutMs;
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Http.Dispose(); } catch { }
    }

    public abstract Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct);

    protected async Task<HttpResponseMessage> PostFormAsync(string url,
        IEnumerable<KeyValuePair<string, string>> form,
        IEnumerable<KeyValuePair<string, string>> headers,
        CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form),
        };
        foreach (var (k, v) in headers)
            req.Headers.TryAddWithoutValidation(k, v);
        return await Http.SendAsync(req, linked.Token).ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> PostJsonAsync(string url, string json,
        IEnumerable<KeyValuePair<string, string>> headers,
        CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeoutMs);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        foreach (var (k, v) in headers)
            req.Headers.TryAddWithoutValidation(k, v);
        return await Http.SendAsync(req, linked.Token).ConfigureAwait(false);
    }

    protected static ServiceResult Fail(string message) =>
        new() { Error = message };
}
