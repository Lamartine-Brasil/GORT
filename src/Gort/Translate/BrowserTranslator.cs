using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Gort.Core;
using Gort.Translate.Browser;

namespace Gort.Translate;

/// <summary>
/// Tradutor por navegador embutido (VI.5, RF-260..270): navega para URL
/// montada (par + texto escapado + sufixo P-58), extrai por script e
/// pós-processa. Tempos P-59..P-62, jitters P-56/P-63, alternativa (RF-267),
/// inspeção (RF-268/269). URL e script remotos (RF-270).
/// </summary>
public sealed class BrowserTranslator : HttpTranslator, IDisposable
{
    public override string Id => "embedded-browser";
    public override string Display => "Tradutor por navegador embutido";
    public override string DefaultToken => RemoteDefaults.BrowserToken;  // RF-232

    private readonly Func<ITranslationService?> _fallback;
    private readonly Func<bool> _useFallback;
    private readonly CdpBrowser _browser = new();
    // Instância única por serviço (Services): chamadas sobrepostas (o pipeline
    // cancela sem esperar) corriam nesses campos — serializa a tradução.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _previousText = "";
    private string _previousResult = "";
    private bool _firstDone;
    private string _lastUrl = "";

    public BrowserTranslator(Func<ITranslationService?> fallback, Func<bool> useFallback,
        HttpMessageHandler? handler = null)
        : base(handler, 1000)
    {
        _fallback = fallback; _useFallback = useFallback;
    }

    public override async Task<ServiceResult> TranslateAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await TranslateInnerAsync(texts, srcCode, dstCode, ct).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    private async Task<ServiceResult> TranslateInnerAsync(IReadOnlyList<string> texts,
        string srcCode, string dstCode, CancellationToken ct)
    {
        string text = texts.Count > 0 ? texts[0] : "";
        string url = BuildUrl(text, srcCode, dstCode);
        _lastUrl = url;
        TimeSpan timeout = SelectTimeout(text);
        try
        {
            // RF-266: atraso aleatório antes de navegar.
            await Task.Delay(Random.Shared.Next(Params.P63_BeforeNavigateJitterMs + 1), ct)
                .ConfigureAwait(false);
            string raw = await _browser.TranslatePageAsync(url,
                RemoteDefaults.BrowserExtractScript, _previousResult, timeout, ct)
                .ConfigureAwait(false);
            _previousText = text;
            _previousResult = raw;
            if (!_firstDone) _firstDone = true;
            string done = PostProcess(raw);
            // RF-266: bloqueio aleatório após receber.
            try
            {
                await Task.Delay(Random.Shared.Next(Params.P56_PostRequestJitterMs + 1), ct)
                    .ConfigureAwait(false);
            }
            catch { }
            return new ServiceResult { Translations = new List<string> { done } };
        }
        catch (OperationCanceledException) { return CancelOrTimeout(ct); }
        catch (Exception ex)
        {
            if (_useFallback() && _fallback() is { } fb)             // RF-267
            {
                var r = await fb.TranslateAsync(texts, srcCode, dstCode, ct)
                    .ConfigureAwait(false);
                return r;
            }
            return Fail("Falha no navegador embutido: " + ex.Message);
        }
    }

    /// <summary>RF-261: URL com par + texto escapado + sufixo em linha nova.</summary>
    internal static string BuildUrl(string text, string src, string dst)
    {
        // EscapeDataString já escapa as barras (%2F).
        string payload = text + "\n" + Params.P58_BrowserSuffix;  // 🔒 ^^^^
        return RemoteDefaults.BrowserUrlFormat
            .Replace("{src}", Uri.EscapeDataString(src))
            .Replace("{dst}", Uri.EscapeDataString(dst))
            .Replace("{text}", Uri.EscapeDataString(payload));
    }

    /// <summary>Pós-processamento VI.5: aspas, escapadas, sufixo, barras.</summary>
    internal static string PostProcess(string raw)
    {
        string s = raw.Trim();
        if (s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\""))
            s = s[1..^1];
        s = s.Replace("\\\\n\\\\n", "\n").Replace("\\n\\n", "\n");
        s = s.Replace("\\\\n", "").Replace("\\n", "");
        int cut = s.IndexOf(Params.P58_BrowserSuffix, StringComparison.Ordinal);
        if (cut >= 0) s = s[..cut];
        s = s.Trim().Trim('"').Replace("\\", "");
        return s;
    }

    /// <summary>P-59/P-60/P-61/P-62 (RF-264..265 🔒).</summary>
    internal TimeSpan SelectTimeout(string text)
    {
        int sec = _useFallback() ? Params.P60_BrowserTimeoutAltSec : Params.P59_BrowserTimeoutSec;
        if (!_firstDone) sec += Params.P61_BrowserFirstExtraSec;
        if (text == _previousText) sec = (int)Math.Ceiling(Params.P62_BrowserRepeatSec);
        return TimeSpan.FromSeconds(sec);
    }

    /// <summary>RF-268/269: abre a página visível para resolver bloqueios.</summary>
    public void ShowInspector()
    {
        try
        {
            string? edge = CdpBrowser.FindEdge();
            if (edge is null || _lastUrl == "") return;
            using var p = Process.Start(new ProcessStartInfo(edge, "--new-window \"" + _lastUrl + "\"")
            {
                UseShellExecute = false,
            });
        }
        catch { }
    }

    public override void Dispose() { _gate.Dispose(); _browser.Dispose(); base.Dispose(); }   // RF-016
}
