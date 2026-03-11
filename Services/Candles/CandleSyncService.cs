using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Exchanges;
using CryptoJournal.Wpf.Services.Exchanges;
using CryptoJournal.Wpf.Storage.Candles;

namespace CryptoJournal.Wpf.Services.Candles;

public sealed class CandleSyncService
{
    private readonly ICandleCache              _cache;
    private readonly IMarketDataClientResolver _resolver;
    private readonly ISymbolExchangePinStore   _pins;

    public CandleSyncService(ICandleCache cache, IMarketDataClientResolver resolver, ISymbolExchangePinStore pins)
    {
        _cache    = cache;
        _resolver = resolver;
        _pins     = pins;
    }

    public async Task SyncAsync(string exchange, string symbol, string interval, CancellationToken ct = default)
    {
        // We may switch exchange if the preferred one fails or doesn't support the symbol.
        var currentExchange = await GetPinnedOrPreferredExchangeAsync(exchange, symbol, ct);

        // Prevent infinite switching loops (at most N attempts).
        var remainingSwitches = Math.Max(1, _resolver.Exchanges.Count);

        while (remainingSwitches-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            // Cache is exchange-scoped, so we always read/write using the "currentExchange".
            var last = await _cache.GetLastOpenTimeUtcAsync(currentExchange, symbol, interval, ct);
            var start = last is null
                ? (DateTimeOffset?)null
                : last.Value + IntervalToTimeSpan(interval);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Only the very first request (empty cache) may treat "empty batch" as "symbol not supported"
                // and try the next exchange.
                var allowFallbackOnEmpty = last is null;

                var (UsedExchange, Batch) = await GetCandlesWithFallbackAsync(
                    preferredExchange:    currentExchange,
                    symbol:               symbol,
                    interval:             interval,
                    startUtc:             start,
                    endUtc:               null,
                    limit:                1000,
                    allowFallbackOnEmpty: allowFallbackOnEmpty,
                    ct:                   ct);

                // If we had to switch exchange, restart using the new exchange cache state.
                if (!UsedExchange.Equals(currentExchange, StringComparison.OrdinalIgnoreCase))
                {
                    currentExchange = UsedExchange;
                    break; // restart outer "remainingSwitches" loop
                }

                var batch = Batch;
                if (batch.Count == 0)
                    return;

                // Remove duplicates if API returned overlapping candles.
                if (last is not null)
                    batch = batch.Where(c => c.OpenTimeUtc > last.Value).ToList();

                if (batch.Count == 0)
                    return;

                await _cache.AppendAsync(currentExchange, symbol, interval, batch, ct);

                last  = batch[^1].OpenTimeUtc;
                start = last.Value + IntervalToTimeSpan(interval);

                if (batch.Count < 1000)
                    return;
            }
        }
    }

    public async Task SyncForwardAsync(string exchange, string symbol, string interval, CancellationToken ct = default)
    {
        var currentExchange   = await GetPinnedOrPreferredExchangeAsync(exchange, symbol, ct);
        var remainingSwitches = Math.Max(1, _resolver.Exchanges.Count);

        while (remainingSwitches-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            var idx = await _cache.LoadIndexAsync(currentExchange, symbol, interval, ct);

            var start = idx.LastOpenTimeUtc is null
                ? DateTimeOffset.FromUnixTimeMilliseconds(0) // from the beginning
                : idx.LastOpenTimeUtc.Value + IntervalToTimeSpan(interval);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var allowFallbackOnEmpty = idx.LastOpenTimeUtc is null;

                var (UsedExchange, Batch) = await GetCandlesWithFallbackAsync(
                    preferredExchange:    currentExchange,
                    symbol:               symbol,
                    interval:             interval,
                    startUtc:             start,
                    endUtc:               null,
                    limit:                1000,
                    allowFallbackOnEmpty: allowFallbackOnEmpty,
                    ct:                   ct);

                if (!UsedExchange.Equals(currentExchange, StringComparison.OrdinalIgnoreCase))
                {
                    currentExchange = UsedExchange;
                    break; // restart with another exchange (reload idx/start)
                }

                var batch = Batch;
                if (batch.Count == 0)
                    return;

                batch = batch.OrderBy(c => c.OpenTimeUtc).ToList();

                if (idx.LastOpenTimeUtc is not null)
                    batch = batch.Where(c => c.OpenTimeUtc > idx.LastOpenTimeUtc.Value).ToList();

                if (batch.Count == 0)
                    return;

                await _cache.UpsertAsync(currentExchange, symbol, interval, batch, ct);

                idx   = await _cache.LoadIndexAsync(currentExchange, symbol, interval, ct);
                start = idx.LastOpenTimeUtc!.Value + IntervalToTimeSpan(interval);

                if (batch.Count < 1000)
                    return;
            }
        }
    }

    public async Task BackfillAsync(string exchange, string symbol, string interval, CancellationToken ct = default)
    {
        var currentExchange   = await GetPinnedOrPreferredExchangeAsync(exchange, symbol, ct);
        var remainingSwitches = Math.Max(1, _resolver.Exchanges.Count);

        while (remainingSwitches-- > 0)
        {
            ct.ThrowIfCancellationRequested();

            var idx = await _cache.LoadIndexAsync(currentExchange, symbol, interval, ct);
            if (idx.FirstOpenTimeUtc is null)
                return; // forward with epoch will fill it automatically

            var end = idx.FirstOpenTimeUtc.Value.AddMilliseconds(-1);

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // For backfill, empty batch is normal ("we reached the beginning"),
                // so we do NOT fallback on empty here.
                var (UsedExchange, Batch) = await GetCandlesWithFallbackAsync(
                    preferredExchange:    currentExchange,
                    symbol:               symbol,
                    interval:             interval,
                    startUtc:             null,
                    endUtc:               end,
                    limit:                1000,
                    allowFallbackOnEmpty: false,
                    ct:                   ct);

                if (!UsedExchange.Equals(currentExchange, StringComparison.OrdinalIgnoreCase))
                {
                    currentExchange = UsedExchange;
                    break; // restart backfill with another exchange
                }

                var batch = Batch;
                if (batch.Count == 0)
                    return;

                batch = batch.OrderBy(c => c.OpenTimeUtc).ToList();

                // If we don't move the "first candle" - stop, otherwise we can get stuck.
                var newFirst = batch[0].OpenTimeUtc;
                if (newFirst >= idx.FirstOpenTimeUtc.Value)
                    return;

                await _cache.UpsertAsync(currentExchange, symbol, interval, batch, ct);

                idx = await _cache.LoadIndexAsync(currentExchange, symbol, interval, ct);
                if (idx.FirstOpenTimeUtc is null)
                    return;

                end = idx.FirstOpenTimeUtc.Value.AddMilliseconds(-1);

                if (batch.Count < 1000)
                    return;
            }
        }
    }

    public async Task EnsureDailyHistoryForAthAsync(string exchange, string symbol, CancellationToken ct = default)
    {
        const string interval = "1d";

        await BackfillAsync(exchange, symbol, interval, ct);
        await SyncForwardAsync(exchange, symbol, interval, ct);
    }

    // =============================
    // Fallback + pin logic
    // =============================

    private async Task<string> GetPinnedOrPreferredExchangeAsync(string preferredExchange, string symbol, CancellationToken ct)
    {
        // If a symbol is already pinned to some exchange, we prefer it.
        var pinned = await SafeGetPinAsync(symbol, ct);

        if (!string.IsNullOrWhiteSpace(pinned) && IsKnownExchange(pinned))
            return pinned!;

        return NormalizeExchange(preferredExchange);
    }

    private async Task<(string UsedExchange, List<Candle> Batch)> GetCandlesWithFallbackAsync(
        string            preferredExchange,
        string            symbol,
        string            interval,
        DateTimeOffset?   startUtc,
        DateTimeOffset?   endUtc,
        int               limit,
        bool              allowFallbackOnEmpty,
        CancellationToken ct)
    {
        Exception? lastError = null;

        // Build candidate exchanges: pinned -> preferred -> all others.
        var candidates = await BuildCandidatesAsync(preferredExchange, symbol, ct);

        foreach (var ex in candidates)
        {
            ct.ThrowIfCancellationRequested();

            IMarketDataClient client;
            try
            {
                client = _resolver.GetRequired(ex);
            }
            catch (Exception e)
            {
                lastError = e;
                continue;
            }

            try
            {
                var batch = await client.GetCandlesAsync(symbol, interval, startUtc, endUtc, limit, ct);

                // If we are at the very beginning and got empty, treat it like "symbol not supported"
                // and try another exchange.
                if (batch.Count == 0 && allowFallbackOnEmpty)
                    continue;

                var list = batch.OrderBy(c => c.OpenTimeUtc).ToList();

                // Pin exchange on a real success (non-empty).
                if (list.Count > 0)
                    await SafeSetPinAsync(symbol, ex, ct);

                return (ex, list);
            }
            catch (Exception e)
            {
                lastError = e;
                // Try next exchange
            }
        }

        // If everything failed with exceptions -> rethrow the last one,
        // otherwise return empty list to gracefully stop the sync loop.
        if (lastError is not null)
            throw lastError;

        return (NormalizeExchange(preferredExchange), new List<Candle>(0));
    }

    private async Task<List<string>> BuildCandidatesAsync(string preferredExchange, string symbol, CancellationToken ct)
    {
        var result = new List<string>();
        var used   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var pinned = await SafeGetPinAsync(symbol, ct);
        if (!string.IsNullOrWhiteSpace(pinned) && IsKnownExchange(pinned) && used.Add(pinned!))
            result.Add(pinned!);

        preferredExchange = NormalizeExchange(preferredExchange);
        if (IsKnownExchange(preferredExchange) && used.Add(preferredExchange))
            result.Add(preferredExchange);

        // Add remaining exchanges in resolver order
        foreach (var ex in _resolver.Exchanges)
        {
            var nx = NormalizeExchange(ex.ExchangeId);
            if (used.Add(nx))
                result.Add(nx);
        }

        return result;
    }

    private bool IsKnownExchange(string exchangeId)
        => _resolver.Exchanges.Any(x => NormalizeExchange(x.ExchangeId).Equals(NormalizeExchange(exchangeId), StringComparison.OrdinalIgnoreCase));

    private async Task<string?> SafeGetPinAsync(string symbol, CancellationToken ct)
    {
        try { return await _pins.GetAsync(symbol, ct); }
        catch { return null; }
    }

    private async Task SafeSetPinAsync(string symbol, string exchangeId, CancellationToken ct)
    {
        try { await _pins.SetAsync(symbol, exchangeId, ct); }
        catch { /* ignore persistence errors */ }
    }

    private static string NormalizeExchange(string s)
        => (s ?? "").Trim().ToLowerInvariant();

    private static TimeSpan IntervalToTimeSpan(string interval)
        => interval switch
        {
            "1m"  => TimeSpan.FromMinutes(1),
            "3m"  => TimeSpan.FromMinutes(3),
            "5m"  => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h"  => TimeSpan.FromHours(1),
            "2h"  => TimeSpan.FromHours(2),
            "4h"  => TimeSpan.FromHours(4),
            "6h"  => TimeSpan.FromHours(6),
            "12h" => TimeSpan.FromHours(12),
            "1d"  => TimeSpan.FromDays(1),
            _     => throw new NotSupportedException($"Unsupported interval: {interval}")
        };
}