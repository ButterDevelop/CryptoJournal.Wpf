using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Exchanges;
using CryptoJournal.Wpf.Storage.Candles;
using CryptoJournal.Wpf.UI;
using System.Collections.Concurrent;

namespace CryptoJournal.Wpf.Services.Candles;

public sealed class AthService
{
    private const string INTERVAL = "1d";

    private readonly IMarketDataClientResolver _resolver;
    private readonly ICandleCache              _cache;
    private readonly IEnvironmentService       _env;

    // In-memory ATH cache keyed by environment, quote currency, and symbol
    private readonly ConcurrentDictionary<string, decimal>    _athByKey = new(StringComparer.OrdinalIgnoreCase);

    // De-duplicate concurrent background fetch requests
    private readonly ConcurrentDictionary<string, Lazy<Task>> _inflight = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, decimal>? AthUpdated; // Event triggered upon an ATH update, providing the symbol and its new value

    public AthService(IMarketDataClientResolver resolver, ICandleCache cache, IEnvironmentService env)
    {
        _resolver = resolver;
        _cache    = cache;
        _env      = env;
    }

    /// <summary>
    /// Returns cached ATH immediately if available (fast, no IO).
    /// </summary>
    public decimal? TryGetCachedAth(string symbol)
    {
        var key = MakeKey(symbol);
        return _athByKey.TryGetValue(key, out var v) ? v : null;
    }

    /// <summary>
    /// Starts background ATH fetching for this symbol (fire-and-forget).
    /// When ATH becomes available, AthUpdated event is raised.
    /// IMPORTANT: Avoid passing short-lived UI-bound CancellationTokens to background operations.
    /// </summary>
    public void PrefetchAth(string symbol, CancellationToken ct = default)
    {
        // Skip background execution if the caller's token is already canceled
        if (ct.IsCancellationRequested)
            return;

        var key = MakeKey(symbol);
        if (key.Length == 0)
            return;

        // Skip network prefetch if a value is already cached
        if (_athByKey.ContainsKey(key))
            return;

        // Enforce single concurrent execution for each specific key
        var lazy = _inflight.GetOrAdd(
            key,
            k => new Lazy<Task>(
                () => PrefetchAthCoreAsync(k, symbol, ct),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );

        _ = lazy.Value; // Trigger background initialization as a fire-and-forget task
    }

    private async Task PrefetchAthCoreAsync(string key, string symbol, CancellationToken ct)
    {
        try
        {
            // Use a timeout so we don't keep hanging in background forever
            // This is crucial for first run when the cache/index might be building
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));

            // If ct is short-lived (UI refresh token), it will cancel too early
            // Prefer passing CancellationToken.None or a long-lived app token
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, ct);

            var effectiveCt = linkedCts.Token;

            var ath = await GetAthAsync(symbol, effectiveCt).ConfigureAwait(false);
            if (ath is null || ath.Value <= 0m)
                return;

            // Store once and notify listeners.
            if (_athByKey.TryAdd(key, ath.Value))
                AthUpdated?.Invoke(NormalizeSymbol(symbol), ath.Value);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellations (timeout or external token)
        }
        catch
        {
            // Ignore transient errors (network, rate limit, missing market, etc.)
        }
        finally
        {
            // Allow future attempts (e.g., if cache becomes available later)
            _inflight.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Returns ATH if possible (can be slow on first run, depends on candle index/cache).
    /// </summary>
    public Task<decimal?> GetAthAsync(string symbol, CancellationToken ct = default)
        => GetAthInternalAsync(preferredExchangeId: null, symbol, ct);

    public Task<decimal?> GetAthAsync(string preferredExchangeId, string symbol, CancellationToken ct = default)
        => GetAthInternalAsync(preferredExchangeId, symbol, ct);

    /// <summary>
    /// Returns ATH if already known; otherwise returns last price quickly.
    /// Also triggers background ATH prefetch, so UI can update later via AthUpdated.
    /// </summary>
    public async Task<decimal?> GetAthOrLastAsync(string symbol, CancellationToken ct = default)
    {
        // 0) If ATH is already cached in memory, return it instantly
        var cached = TryGetCachedAth(symbol);
        if (cached is not null && cached.Value > 0m)
            return cached.Value;

        // 1) Try ATH via index/cache (might still be null on first run)
        var ath = await GetAthAsync(symbol, ct).ConfigureAwait(false);
        if (ath is not null && ath.Value > 0m)
        {
            // Store to memory cache and return
            _athByKey.TryAdd(MakeKey(symbol), ath.Value);
            return ath.Value;
        }

        // 2) Kick off background prefetch so ATH appears later without blocking UI
        PrefetchAth(symbol, CancellationToken.None);

        // 3) Fallback: last price across exchanges (fast)
        var quote      = _env.Current.QuoteCurrency;
        var marketPair = SymbolUtil.ToPair(symbol, quote);

        foreach (var ex in _resolver.Exchanges)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var last = await ex.GetLastPriceAsync(marketPair, ct).ConfigureAwait(false);
                if (last is not null && last.Value > 0m)
                    return last.Value;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* ignore exchange-specific errors */ }
        }

        return null;
    }

    private async Task<decimal?> GetAthInternalAsync(string? preferredExchangeId, string symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return null;

        var quote      = _env.Current.QuoteCurrency;
        var marketPair = SymbolUtil.ToPair(symbol, quote);

        var exchanges = _resolver.Exchanges;
        if (exchanges is null || exchanges.Count == 0)
            return null;

        // Try preferred exchange first (if specified)
        if (!string.IsNullOrWhiteSpace(preferredExchangeId))
        {
            var preferred = exchanges.FirstOrDefault(x =>
                x.ExchangeId.Equals(preferredExchangeId.Trim(), StringComparison.OrdinalIgnoreCase));

            if (preferred is not null)
            {
                var ath = await TryGetAthFromExchangeAsync(preferred.ExchangeId, marketPair, INTERVAL, ct).ConfigureAwait(false);
                if (ath is not null && ath.Value > 0m)
                    return ath;
            }
        }

        // Fallback: try all exchanges in DI priority order
        foreach (var ex in exchanges)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(preferredExchangeId) &&
                ex.ExchangeId.Equals(preferredExchangeId.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            var ath = await TryGetAthFromExchangeAsync(ex.ExchangeId, marketPair, INTERVAL, ct).ConfigureAwait(false);
            if (ath is not null && ath.Value > 0m)
                return ath;
        }

        return null;
    }

    private async Task<decimal?> TryGetAthFromExchangeAsync(string exchangeId, string marketPair, string interval, CancellationToken ct)
    {
        try
        {
            exchangeId = (exchangeId ?? "").Trim().ToLowerInvariant();
            interval   = (interval ?? "").Trim().ToLowerInvariant();

            if (exchangeId.Length == 0 || interval.Length == 0)
                return null;

            var idx = await _cache.GetOrBuildIndexAsync(exchangeId, marketPair, interval, ct).ConfigureAwait(false);
            return (idx?.AthHigh is > 0m) ? idx.AthHigh : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private string MakeKey(string symbol)
    {
        symbol = NormalizeSymbol(symbol);
        if (symbol.Length == 0) return "";

        var quote = (_env.Current.QuoteCurrency ?? "").Trim().ToUpperInvariant();
        if (quote.Length == 0) quote = "USDT";

        // Key must include quote currency because env can change
        return $"{quote}|{symbol}|{INTERVAL}";
    }

    private static string NormalizeSymbol(string? symbol)
        => (symbol ?? "").Trim().ToUpperInvariant();
}