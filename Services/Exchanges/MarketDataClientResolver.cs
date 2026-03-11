using CryptoJournal.Wpf.Exchanges;

namespace CryptoJournal.Wpf.Services.Exchanges;

public interface IMarketDataClientResolver
{
    IMarketDataClient GetRequired(string exchangeId);
    IReadOnlyList<IMarketDataClient> Exchanges { get; }
}

public sealed class MarketDataClientResolver : IMarketDataClientResolver
{
    private readonly Dictionary<string, IMarketDataClient> _byId;
    public IReadOnlyList<IMarketDataClient> Exchanges { get; }

    public MarketDataClientResolver(IEnumerable<IMarketDataClient> clients)
    {
        // Important: The order of IEnumerable is preserved by Microsoft DI - can use it as a priority
        Exchanges = clients.ToList();

        _byId = Exchanges.ToDictionary(
            c => c.ExchangeId.Trim().ToLowerInvariant(),
            c => c,
            StringComparer.OrdinalIgnoreCase);
    }

    public IMarketDataClient GetRequired(string exchangeId)
    {
        exchangeId = (exchangeId ?? "").Trim();
        if (_byId.TryGetValue(exchangeId, out var c)) return c;

        throw new KeyNotFoundException($"IMarketDataClient for exchange '{exchangeId}' is not registered.");
    }
}