using CryptoJournal.Wpf.Domain.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CryptoJournal.Wpf.Exchanges.Mexc;

public sealed class MEXCMarketDataClient : IMarketDataClient
{
    public string ExchangeId => "mexc";

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.mexc.com")
    };

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string            symbol,
        string            interval,
        DateTimeOffset?   startUtc,
        DateTimeOffset?   endUtc,
        int               limit,
        CancellationToken ct = default)
    {
        // MEXC spot v3:
        // GET /api/v3/klines?symbol=BTCUSDT&interval=1m&startTime=...&endTime=...&limit=...
        // Response is an array of arrays, same shape as Binance.
        // Docs: /api/v3/klines

        limit    = Math.Clamp(limit, 1, 1000);
        symbol   = (symbol ?? "").Trim().ToUpperInvariant();
        interval = (interval ?? "").Trim();

        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(interval))
            return [];

        var url = $"/api/v3/klines?symbol={Uri.EscapeDataString(symbol)}&interval={Uri.EscapeDataString(interval)}&limit={limit}";

        if (startUtc is not null)
            url += $"&startTime={startUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        if (endUtc is not null)
            url += $"&endTime={endUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        using var resp = await _http.GetAsync(url, ct);

        // If the pair does not exist (or other non-200), return empty to let your fallback logic try another exchange.
        if (!resp.IsSuccessStatusCode)
            return [];

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        // Expected: JSON array of arrays
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Candle>(doc.RootElement.GetArrayLength());

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            // Indices (per docs):
            // 0 open time (ms)
            // 1 open
            // 2 high
            // 3 low
            // 4 close
            // 5 volume
            // 6 close time (ms)
            // 7 quote asset volume
            // Docs: /api/v3/klines response indexes

            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
                continue;

            var openMs = item[0].GetInt64();
            var open   = ParseDec(item[1].GetString());
            var high   = ParseDec(item[2].GetString());
            var low    = ParseDec(item[3].GetString());
            var close  = ParseDec(item[4].GetString());
            var vol    = ParseDec(item[5].GetString());

            list.Add(new Candle(DateTimeOffset.FromUnixTimeMilliseconds(openMs), open, high, low, close, vol));
        }

        return list.OrderBy(c => c.OpenTimeUtc).ToList();
    }

    public async Task<decimal?> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        // MEXC spot v3:
        // GET /api/v3/ticker/price?symbol=BTCUSDT
        // Response: { "symbol": "...", "price": "..." }
        // Docs: /api/v3/ticker/price

        symbol = (symbol ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var url = $"/api/v3/ticker/price?symbol={Uri.EscapeDataString(symbol)}";

        using var resp = await _http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        if (!doc.RootElement.TryGetProperty("price", out var p))
            return null;

        return ParseDec(p.GetString());
    }

    private static decimal ParseDec(string? s)
        => decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}