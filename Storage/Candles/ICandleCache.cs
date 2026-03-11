using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Storage.Candles;

public interface ICandleCache
{
    Task UpsertAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candlesUtcSorted, CancellationToken ct = default);

    Task<CandleCacheIndex> LoadIndexAsync(string exchange, string symbol, string interval, CancellationToken ct = default);

    Task SaveIndexAsync(CandleCacheIndex index, CancellationToken ct = default);

    Task<DateTimeOffset?> GetLastOpenTimeUtcAsync(string exchange, string symbol, string interval, CancellationToken ct = default);

    Task AppendAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candlesUtcSorted, CancellationToken ct = default);

    // Reserved for future charting and analytics features
    IAsyncEnumerable<Candle> ReadAllAsync(string exchange, string symbol, string interval, CancellationToken ct = default);

    Task ClearAsync(string exchange, string symbol, string interval, CancellationToken ct);

    Task<CandleCacheIndex> GetOrBuildIndexAsync(string exchangeId, string pair, string interval, CancellationToken ct);
}