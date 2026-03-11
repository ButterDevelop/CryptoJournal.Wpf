using CryptoJournal.Wpf.Domain.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CryptoJournal.Wpf.Exchanges.Kraken;

public sealed class KrakenMarketDataClient : IMarketDataClient
{
    public string ExchangeId => "kraken";

    // Kraken Spot REST base (versioned)
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.kraken.com/0/")
    };

    // Kraken uses XBT instead of BTC in many markets.
    // We'll try both "BTC..." and "XBT..." automatically.
    private static IEnumerable<string> PairCandidates(string symbol)
    {
        var s = (symbol ?? "").Trim().ToUpperInvariant().Replace("-", "");
        if (string.IsNullOrWhiteSpace(s)) yield break;

        yield return s;

        // BTCxxxx -> XBTxxxx
        if (s.StartsWith("BTC", StringComparison.OrdinalIgnoreCase))
            yield return string.Concat("XBT", s.AsSpan(3));

        // xxxxBTC -> xxxxXBT (for markets quoted in BTC)
        if (s.EndsWith("BTC", StringComparison.OrdinalIgnoreCase))
            yield return string.Concat(s.AsSpan(0, s.Length - 3), "XBT");
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string            symbol,
        string            interval,
        DateTimeOffset?   startUtc,
        DateTimeOffset?   endUtc,
        int               limit,
        CancellationToken ct = default)
    {
        // Kraken OHLC returns up to 720 most recent entries (per docs).
        // Keep the signature compatible, but cap internally to avoid misleading callers.
        limit = Math.Clamp(limit, 1, 720);

        if (!TryToKrakenIntervalMinutes(interval, out var intervalMin))
            return [];

        var span = IntervalToTimeSpan(interval);

        // Kraken docs: "The last entry ... is for the current, not-yet-committed timeframe and will always be present"
        // We'll drop it if it's incomplete.
        // https://docs.kraken.com/api/docs/rest-api/get-ohlc-data/

        // Kraken OHLC supports "since" (historically), but per current docs it still won't return older than ~720 entries.
        // We'll still pass "since" when available for efficiency.
        long? sinceSec = null;

        if (startUtc is not null)
        {
            sinceSec = startUtc.Value.ToUniversalTime().ToUnixTimeSeconds();
        }
        else if (endUtc is not null)
        {
            // Best-effort: request a window that should contain up to `limit` candles before endUtc.
            var guessStart = endUtc.Value.ToUniversalTime() - TimeSpan.FromTicks(span.Ticks * limit);
            if (guessStart < DateTimeOffset.FromUnixTimeSeconds(0)) guessStart = DateTimeOffset.FromUnixTimeSeconds(0);
            sinceSec = guessStart.ToUnixTimeSeconds();
        }

        foreach (var pair in PairCandidates(symbol))
        {
            try
            {
                var candles = await TryFetchOhlcOnceAsync(pair, intervalMin, sinceSec, ct);

                if (candles.Count == 0)
                    continue;

                // Apply endUtc/startUtc filters locally (Kraken has no "end" param for OHLC).
                if (startUtc is not null)
                    candles = candles.Where(c => c.OpenTimeUtc >= startUtc.Value.ToUniversalTime()).ToList();

                if (endUtc is not null)
                    candles = candles.Where(c => c.OpenTimeUtc <= endUtc.Value.ToUniversalTime()).ToList();

                candles = candles.OrderBy(c => c.OpenTimeUtc).ToList();

                // Drop the current, not-yet-committed candle (if present).
                var now = DateTimeOffset.UtcNow;
                if (candles.Count > 0)
                {
                    var last = candles[^1];
                    if (last.OpenTimeUtc + span > now)
                        candles.RemoveAt(candles.Count - 1);
                }

                if (candles.Count == 0)
                    continue;

                // Respect caller's limit (take the most recent `limit` in the filtered window).
                if (candles.Count > limit)
                    candles = candles.TakeLast(limit).ToList();

                return candles;
            }
            catch
            {
                // Any network/parse issue => treat as "not available", let fallback try another exchange.
            }
        }

        return [];
    }

    public async Task<decimal?> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        foreach (var pair in PairCandidates(symbol))
        {
            try
            {
                var price = await TryFetchLastPriceAsync(pair, ct);
                if (price is not null)
                    return price;
            }
            catch
            {
                // ignore -> fallback to next candidate
            }
        }

        return null;
    }

    private async Task<List<Candle>> TryFetchOhlcOnceAsync(string pair, int intervalMin, long? sinceSec, CancellationToken ct)
    {
        // GET /public/OHLC
        var url = $"public/OHLC?pair={Uri.EscapeDataString(pair)}&interval={intervalMin}";
        if (sinceSec is not null)
            url += $"&since={sinceSec.Value}";

        using var resp = await _http.GetAsync(url, ct);

        // Unknown pair, rate-limit, etc. => "not available"
        if (!resp.IsSuccessStatusCode)
            return [];

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Kraken schema: { "error": [], "result": { "<PAIRKEY>": [ [..], ... ], "last": "..." } }
        if (!doc.RootElement.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Array)
            return [];

        if (err.GetArrayLength() > 0)
            return []; // e.g. "EQuery:Unknown asset pair"

        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            return [];

        // Find the first property that is not "last" (the pair data key can be different from the input pair)
        JsonElement arr = default;
        var found = false;

        foreach (var prop in result.EnumerateObject())
        {
            if (string.Equals(prop.Name, "last", StringComparison.OrdinalIgnoreCase))
                continue;

            arr = prop.Value;
            found = true;
            break;
        }

        if (!found || arr.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Candle>(arr.GetArrayLength());

        foreach (var item in arr.EnumerateArray())
        {
            // OHLC item: [ time, open, high, low, close, vwap, volume, count ]
            // time is unix seconds
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 7)
                continue;

            var tSec  = item[0].GetInt64();
            var open  = ParseDec(item[1].GetString());
            var high  = ParseDec(item[2].GetString());
            var low   = ParseDec(item[3].GetString());
            var close = ParseDec(item[4].GetString());
            var vol   = ParseDec(item[6].GetString());

            list.Add(new Candle(DateTimeOffset.FromUnixTimeSeconds(tSec), open, high, low, close, vol));
        }

        return list;
    }

    private async Task<decimal?> TryFetchLastPriceAsync(string pair, CancellationToken ct)
    {
        // GET /public/Ticker
        var url = $"public/Ticker?pair={Uri.EscapeDataString(pair)}";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("error", out var err) || err.ValueKind != JsonValueKind.Array)
            return null;

        if (err.GetArrayLength() > 0)
            return null; // e.g. unknown pair

        if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
            return null;

        // result has one property per requested pair, key may differ from input.
        foreach (var pairObj in result.EnumerateObject().Select(x => x.Value))
        {
            if (pairObj.ValueKind != JsonValueKind.Object)
                continue;

            // "c": [ "last trade closed price", "lot volume" ]
            if (!pairObj.TryGetProperty("c", out var c) || c.ValueKind != JsonValueKind.Array || c.GetArrayLength() < 1)
                continue;

            return ParseDec(c[0].GetString());
        }

        return null;
    }

    private static bool TryToKrakenIntervalMinutes(string interval, out int minutes)
    {
        // Kraken accepts interval in minutes.
        // Keep only the intervals your app uses, and return false for unsupported ones.
        minutes = interval switch
        {
            "1m"  => 1,
            "5m"  => 5,
            "15m" => 15,
            "30m" => 30,
            "1h"  => 60,
            "4h"  => 240,
            "1d"  => 1440,
            _     => 0
        };

        return minutes != 0;
    }

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
            _     => TimeSpan.FromMinutes(1)
        };

    private static decimal ParseDec(string? s)
        => decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}