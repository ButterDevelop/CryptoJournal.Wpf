using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Exchanges;

public interface IMarketDataClient
{
    string ExchangeId { get; } // "binance", "bybit", ...

    Task<IReadOnlyList<Candle>> GetCandlesAsync(
        string            symbol,
        string            interval,
        DateTimeOffset?   startUtc,
        DateTimeOffset?   endUtc,
        int               limit,
        CancellationToken ct = default);

    Task<decimal?> GetLastPriceAsync(string symbol, CancellationToken ct = default);
}