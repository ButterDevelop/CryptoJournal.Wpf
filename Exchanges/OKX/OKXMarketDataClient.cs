using CryptoJournal.Wpf.Domain.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CryptoJournal.Wpf.Exchanges.Okx;

public sealed class OkxMarketDataClient : IMarketDataClient
{
    public string ExchangeId => "okx";

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://www.okx.com")
    };

    // OKX returns numbers as strings in JSON, e.g. "1.2345"
    private static decimal ParseDec(string? s)
        => decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string            symbol,
        string            interval,
        DateTimeOffset?   startUtc,
        DateTimeOffset?   endUtc,
        int               limit,
        CancellationToken ct = default)
    {
        // OKX has smaller limits than Binance (keep it safe)
        limit = Math.Clamp(limit, 1, 100);

        var instId = ToOkxInstId(symbol);
        var bar    = ToOkxBar(interval);

        // OKX: /api/v5/market/history-candles?instId=BTC-USDT&bar=1D&limit=100&after=...&before=...
        // "after/before" pagination semantics are OKX-specific; pass both when available
        var url = $"/api/v5/market/history-candles?instId={Uri.EscapeDataString(instId)}&bar={Uri.EscapeDataString(bar)}&limit={limit}";

        if (startUtc is not null)
            url += $"&after={startUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        if (endUtc is not null)
            url += $"&before={endUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        using var resp = await _http.GetAsync(url, ct);

        // if the instrument does not exist (or other 4xx), behave like Binance-client: return empty (caller stops)
        if (!resp.IsSuccessStatusCode)
            return [];

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // OKX responses typically look like:
        // { "code":"0", "msg":"", "data":[ [ "ts","o","h","l","c","vol", ... ], ... ] }
        if (!doc.RootElement.TryGetProperty("code", out var codeEl) || codeEl.GetString() != "0")
            return [];

        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Candle>(dataEl.GetArrayLength());

        foreach (var row in dataEl.EnumerateArray())
        {
            // Defensive parsing: expect an array with at least 6 elements
            if (row.ValueKind != JsonValueKind.Array) continue;
            if (row.GetArrayLength() < 6) continue;

            var tsMs  = long.Parse(row[0].GetString() ?? "0", CultureInfo.InvariantCulture);
            var open  = ParseDec(row[1].GetString());
            var high  = ParseDec(row[2].GetString());
            var low   = ParseDec(row[3].GetString());
            var close = ParseDec(row[4].GetString());
            var vol   = ParseDec(row[5].GetString());

            list.Add(new Candle(DateTimeOffset.FromUnixTimeMilliseconds(tsMs), open, high, low, close, vol));
        }

        // OKX may return newest-first; normalize to ascending by OpenTimeUtc for your cache logic.
        return list.OrderBy(c => c.OpenTimeUtc).ToList();
    }

    public async Task<decimal?> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        var instId = ToOkxInstId(symbol);

        // OKX: /api/v5/market/ticker?instId=BTC-USDT
        var url = $"/api/v5/market/ticker?instId={Uri.EscapeDataString(instId)}";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("code", out var codeEl) || codeEl.GetString() != "0")
            return null;

        if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Array)
            return null;

        var first = dataEl.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object)
            return null;

        if (!first.TryGetProperty("last", out var lastEl))
            return null;

        return ParseDec(lastEl.GetString());
    }

    private static string ToOkxBar(string interval)
    {
        interval = (interval ?? "").Trim();

        // app uses "1h/2h/1d" style; OKX uses "1H/2H/1D" for hours/days
        return interval.ToLowerInvariant() switch
        {
            "1m"  => "1m",
            "3m"  => "3m",
            "5m"  => "5m",
            "15m" => "15m",
            "30m" => "30m",

            "1h"  => "1H",
            "2h"  => "2H",
            "4h"  => "4H",
            "6h"  => "6H",
            "12h" => "12H",

            "1d"  => "1D",

            _ => interval
        };
    }

    private static string ToOkxInstId(string symbol)
    {
        // OKX spot uses "BASE-QUOTE", e.g. "BTC-USDT".
        // we often use "BTCUSDT" - convert common quote suffixes
        symbol = (symbol ?? "").Trim().ToUpperInvariant();

        if (symbol.Contains('-'))
            return symbol;

        // Common quote assets; order matters (longest first)
        string[] quotes =
        [
            "USDT", "USDC", "BUSD", "USD",
            "EUR",  "BTC",  "ETH",  "TRY"
        ];

        foreach (var q in quotes)
        {
            if (!symbol.EndsWith(q, StringComparison.OrdinalIgnoreCase)) continue;

            var @base = symbol[..^q.Length];
            if (string.IsNullOrWhiteSpace(@base)) break;

            return $"{@base}-{q}";
        }

        // Fallback: return as-is (maybe caller already sends "BTC-USDT-SWAP" etc.)
        return symbol;
    }
}