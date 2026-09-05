using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gort.Translate;

/// <summary>
/// Protocolo comum de tradução (18.1, RF-228..RF-240): vazio, cancelamento do
/// pedido anterior, cache antes da rede, lote único com token, divisão,
/// token avançado, gravação imediata, erro único, concatenação bruta,
/// cancelamento e ponte.
/// </summary>
public sealed class TranslationPipeline
{
    private readonly Func<string, ITranslationService?> _services;
    private readonly Collectanea? _collectanea;
    private readonly Func<string>? _ocrLang;
    private readonly ITranslationMemory _memory;
    private CancellationTokenSource? _inflight;
    private readonly object _inflightGate = new();

    public TranslationPipeline(Func<string, ITranslationService?> services,
        ITranslationMemory memory)
        : this(services, null, null, memory) { }

    public TranslationPipeline(Func<string, ITranslationService?> services,
        Collectanea? collectanea, Func<string>? ocrLang, ITranslationMemory memory)
    {
        _services = services;
        _collectanea = collectanea;
        _ocrLang = ocrLang;
        _memory = memory;
    }

    /// <summary>Contagem do cache por serviço (RF-491: marcador).</summary>
    public Func<string, int>? CacheCount { get; set; }

    public async Task<BatchResult> TranslateBatchAsync(string serviceId,
        IReadOnlyList<string> sources, string srcCode, string dstCode,
        bool bridge, CancellationToken ct)
    {
        var perText = new List<string?>();
        var fromCache = new List<bool>();
        for (int i = 0; i < sources.Count; i++) { perText.Add(null); fromCache.Add(false); }
        if (sources.Count == 0) return new BatchResult { PerText = perText, Raw = "" };

        bool allEmpty = true;
        foreach (var s in sources)
            if (!string.IsNullOrEmpty(s)) { allEmpty = false; break; }
        if (allEmpty) return new BatchResult { PerText = perText, Raw = "" };  // RF-228

        var svc = _services(serviceId);
        if (svc is null)
            return new BatchResult
            {
                PerText = perText,
                Error = $"Serviço de tradução '{serviceId}' indisponível nesta versão.",
            };

        // RF-229: novo pedido cancela o anterior ainda em curso.
        // Com lock: laço + clipboard podem chamar em concorrência.
        // Sem Dispose do anterior aqui: o concorrente ainda pode estar
        // usando o token dele (ObjectDisposedException). Cada pedido
        // descarta o seu ao terminar (finally).
        CancellationTokenSource mine;
        lock (_inflightGate)
        {
            CancellationTokenSource? old = _inflight;
            mine = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _inflight = mine;
            try { old?.Cancel(); } catch { }
        }
        var mineToken = mine.Token;

        try
        {
            // RF-230/395: rtrim + coletânea + memória antes da rede.
            var missing = new List<int>();
            var cached = new List<bool>();
            for (int i = 0; i < sources.Count; i++) cached.Add(false);
            for (int i = 0; i < sources.Count; i++)
            {
                string s = sources[i].TrimEnd();
                string? hit = null;
                if (svc.UsesCollectanea && _collectanea is not null)
                    hit = _collectanea.TryGet(s, _ocrLang?.Invoke() ?? "en");
                hit ??= svc.UsesResultMemory ? _memory.TryGet(serviceId, s) : null;
                if (hit is not null) { perText[i] = hit; cached[i] = true; }
                else missing.Add(i);
            }
            if (missing.Count == 0)
                return Done(svc, perText, cached);

            // RF-239: ponte via japonês (origem ≠ ja, serviço declara suporte).
            if (bridge && srcCode != "ja" && svc.SupportsBridge)
            {
                var req = new List<string>();
                foreach (int i in missing) req.Add(sources[i].TrimEnd());
                var leg1 = await SendAsync(svc, req, srcCode, "ja", mineToken).ConfigureAwait(false);
                if (leg1.Cancelled) return Cancelled(perText);
                if (leg1.Error is not null) return Fail(perText, leg1.Error);
                var leg2 = await SendAsync(svc, leg1.Translations!, "ja", dstCode, mineToken)
                    .ConfigureAwait(false);
                if (leg2.Cancelled) return Cancelled(perText);
                if (leg2.Error is not null) return Fail(perText, leg2.Error);
                StoreAll(serviceId, sources, missing, leg2.Translations!, svc.UsesResultMemory);
                return Done(svc, perText, cached, missing, leg2.Translations!);
            }

            var req2 = new List<string>();
            foreach (int i in missing) req2.Add(sources[i].TrimEnd());
            var r = await SendAsync(svc, req2, srcCode, dstCode, mineToken).ConfigureAwait(false);
            if (r.Cancelled) return Cancelled(perText);
            if (r.Error is not null) return Fail(perText, r.Error);            // RF-236
            StoreAll(serviceId, sources, missing, r.Translations!, svc.UsesResultMemory);            // RF-235
            return Done(svc, perText, cached, missing, r.Translations!);
        }
        catch (OperationCanceledException)
        {
            return Cancelled(perText);                                          // RF-238
        }
        finally
        {
            lock (_inflightGate) { if (_inflight == mine) _inflight = null; }
            try { mine.Dispose(); } catch { }
        }
    }

    private void StoreAll(string serviceId, IReadOnlyList<string> sources,
        List<int> missing, List<string> got, bool useMemory)
    {
        if (!useMemory) return;                                    // RF-214
        for (int k = 0; k < missing.Count && k < got.Count; k++)
            _memory.Store(serviceId, sources[missing[k]].TrimEnd(), got[k]);
    }

    private static BatchResult Done(ITranslationService svc, List<string?> perText,
        List<bool>? cached = null, List<int>? missing = null, List<string>? got = null)
    {
        if (missing is not null && got is not null)
            for (int k = 0; k < missing.Count && k < got.Count; k++)
                perText[missing[k]] = got[k];
        var fromCache = new List<bool>();
        if (cached is not null) fromCache.AddRange(cached);
        else for (int i = 0; i < perText.Count; i++) fromCache.Add(false);
        if (missing is not null)
            foreach (int i in missing)
                if (i < fromCache.Count) fromCache[i] = false;
        return new BatchResult
        {
            PerText = perText,
            FromCache = fromCache,
            Raw = BuildRaw(perText, svc.DefaultToken),
        };
    }

    private static BatchResult Fail(List<string?> perText, string error) =>
        new() { PerText = perText, Error = error, FromCache = Falses(perText.Count) };

    private static BatchResult Cancelled(List<string?> perText) =>
        new() { PerText = perText, Raw = "", Cancelled = true, FromCache = Falses(perText.Count) };

    private static List<bool> Falses(int n)
    {
        var l = new List<bool>();
        for (int i = 0; i < n; i++) l.Add(false);
        return l;
    }

    /// <summary>
    /// Envia em requisição única (RF-231) e distribui a resposta (RF-233/234).
    /// </summary>
    internal async Task<ServiceResult> SendAsync(ITranslationService svc,
        List<string> texts, string src, string dst, CancellationToken ct)
    {
        bool adv = RemoteDefaults.AdvancedToken;                     // RF-234 🔒
        string token = TokenHelper.Shorten(svc.DefaultToken, adv);
        var sb = new System.Text.StringBuilder();
        foreach (var t in texts) { sb.Append(token); sb.Append(t); sb.Append('\n'); }
        var r = await svc.TranslateAsync(new List<string> { sb.ToString() },
            src, dst, ct).ConfigureAwait(false);
        if (r.Cancelled || r.Error is not null) return r;
        // Contrato garantido aqui: sem erro ⟹ lista presente (os `!` abaixo
        // são invariante, não máscara). init-only: normaliza por cópia.
        if (r.Translations is null)
            return new ServiceResult { Translations = new List<string>() };
        string raw = r.Translations.Count > 0 ? r.Translations[0] : "";
        // RF-233: divide pelo token; faltando partes → blocos sem tradução.
        var parts = TokenHelper.CleanParts(
            raw.Split(new[] { svc.DefaultToken }, StringSplitOptions.None),
            svc.DefaultToken, adv);
        if (parts.Count > 0 && parts[0] == "" && texts.Count > 0)
            parts.RemoveAt(0);   // prefixo vazio antes do primeiro token
        var outList = new List<string>();
        for (int i = 0; i < texts.Count; i++)
            outList.Add(i < parts.Count ? parts[i].Trim() : "");
        return new ServiceResult { Translations = outList };
    }

    /// <summary>RF-237: token + tradução + quebra por texto de origem.</summary>
    internal static string BuildRaw(List<string?> perText, string token)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var t in perText)
        {
            sb.Append(token);
            sb.Append(t ?? "");
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
