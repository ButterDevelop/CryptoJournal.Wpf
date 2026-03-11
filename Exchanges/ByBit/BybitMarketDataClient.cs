using CryptoJournal.Wpf.Domain.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CryptoJournal.Wpf.Exchanges.Bybit;

public sealed class BybitMarketDataClient : IMarketDataClient
{
    public string ExchangeId => "bybit";

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.bybit.com")
    };

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string            symbol,
        string            interval,
        DateTimeOffset?   startUtc,
        DateTimeOffset?   endUtc,
        int               limit,
        CancellationToken ct = default)
    {
        limit  = Math.Clamp(limit, 1, 1000);
        symbol = (symbol ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(symbol))
            return [];

        // Bybit V5 Kline endpoint:
        // GET /v5/market/kline?category=spot&symbol=BTCUSDT&interval=1&start=...&end=...&limit=...
        // interval differs from Binance style, so we map "1m" -> "1", "1h" -> "60", "1d" -> "D", etc.
        var bybitInterval = MapInterval(interval);

        var url = $"/v5/market/kline?category=spot&symbol={symbol}&interval={bybitInterval}&limit={limit}";

        if (startUtc is not null)
            url += $"&start={startUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        if (endUtc is not null)
            url += $"&end={endUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        using var resp = await _http.GetAsync(url, ct);

        // If the symbol/pair does not exist (or any API error), return empty to allow fallback to another exchange.
        if (!resp.IsSuccessStatusCode)
            return [];

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        // Bybit returns { retCode, retMsg, result: { list: [...] } }
        if (!doc.RootElement.TryGetProperty("retCode", out var retCodeEl))
            return [];

        if (retCodeEl.ValueKind == JsonValueKind.Number && retCodeEl.GetInt32() != 0)
            return [];

        if (!doc.RootElement.TryGetProperty("result", out var resultEl))
            return [];

        if (!resultEl.TryGetProperty("list", out var listEl) || listEl.ValueKind != JsonValueKind.Array)
            return [];

        var list = new List<Candle>(capacity: listEl.GetArrayLength());

        // Each item is an array of strings:
        // [ startTime, open, high, low, close, volume, turnover ]
        foreach (var item in listEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
                continue;

            var openMsStr = item[0].GetString();
            if (!long.TryParse(openMsStr ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var openMs))
                continue;

            var open  = ParseDec(item[1].GetString());
            var high  = ParseDec(item[2].GetString());
            var low   = ParseDec(item[3].GetString());
            var close = ParseDec(item[4].GetString());
            var vol   = ParseDec(item[5].GetString());

            list.Add(new Candle(DateTimeOffset.FromUnixTimeMilliseconds(openMs), open, high, low, close, vol));
        }

        // The API can return descending by time; we normalize to ascending.
        return list.OrderBy(c => c.OpenTimeUtc).ToList();
    }

    public async Task<decimal?> GetLastPriceAsync(string symbol, CancellationToken ct = default)
    {
        symbol = (symbol ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        // Bybit V5 Tickers endpoint:
        // GET /v5/market/tickers?category=spot&symbol=BTCUSDT
        var url = $"/v5/market/tickers?category=spot&symbol={symbol}";

        using var resp = await _http.GetAsync(url, ct);

        // If not found / any error => null (fallback to next exchange)
        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("retCode", out var retCodeEl))
            return null;

        if (retCodeEl.ValueKind == JsonValueKind.Number && retCodeEl.GetInt32() != 0)
            return null;

        if (!doc.RootElement.TryGetProperty("result", out var resultEl))
            return null;

        if (!resultEl.TryGetProperty("list", out var listEl) || listEl.ValueKind != JsonValueKind.Array)
            return null;

        if (listEl.GetArrayLength() == 0)
            return null;

        var first = listEl[0];
        if (first.ValueKind != JsonValueKind.Object)
            return null;

        if (!first.TryGetProperty("lastPrice", out var lastPriceEl))
            return null;

        return ParseDec(lastPriceEl.GetString());
    }

    private static string MapInterval(string interval)
        => (interval ?? "").Trim().ToLowerInvariant() switch
        {
            "1m"  => "1",
            "3m"  => "3",
            "5m"  => "5",
            "15m" => "15",
            "30m" => "30",
            "1h"  => "60",
            "2h"  => "120",
            "4h"  => "240",
            "6h"  => "360",
            "12h" => "720",
            "1d"  => "D",
            _     => throw new NotSupportedException($"Unsupported interval for Bybit: {interval}")
        };

    private static decimal ParseDec(string? s)
        => decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}