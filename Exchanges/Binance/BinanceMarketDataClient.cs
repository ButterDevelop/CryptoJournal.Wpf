using CryptoJournal.Wpf.Domain.Models;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace CryptoJournal.Wpf.Exchanges.Binance;

public sealed class BinanceMarketDataClient : IMarketDataClient
{
    public string ExchangeId => "binance";

    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("https://api.binance.com")
    };

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string symbol, string interval, DateTimeOffset? startUtc, DateTimeOffset? endUtc, int limit, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        symbol = symbol.Trim().ToUpperInvariant();

        var url = $"/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}";

        if (startUtc is not null)
            url += $"&startTime={startUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        if (endUtc is not null)
            url += $"&endTime={endUtc.Value.ToUniversalTime().ToUnixTimeMilliseconds()}";

        using var resp = await _http.GetAsync(url, ct);

        // if the symbol/pair does not exist, we will simply return empty (and Sync will stop)
        if (!resp.IsSuccessStatusCode)
            return [];

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var list = new List<Candle>(doc.RootElement.GetArrayLength());
        foreach (var item in doc.RootElement.EnumerateArray())
        {
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
        symbol = symbol.Trim().ToUpperInvariant();
        var url = $"/api/v3/ticker/price?symbol={symbol}";

        using var resp = await _http.GetAsync(url, ct);

        if (!resp.IsSuccessStatusCode)
            return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("price", out var p))
            return null;

        return ParseDec(p.GetString());
    }

    private static decimal ParseDec(string? s)
        => decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}