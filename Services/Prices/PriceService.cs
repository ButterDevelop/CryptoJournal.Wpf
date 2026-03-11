using CryptoJournal.Wpf.Exchanges;

namespace CryptoJournal.Wpf.Services.Prices;

public sealed class PriceService
{
    private readonly IReadOnlyList<IMarketDataClient> _clients;

    public PriceService(IEnumerable<IMarketDataClient> clients)
        => _clients = clients.ToList();

    public async Task<Dictionary<string, decimal>> GetLastPricesAsync(
        IEnumerable<string> symbols,
        CancellationToken   ct = default)
    {
        var dict = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in symbols.Select(x => x.Trim().ToUpperInvariant())
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            decimal? price = null;

            // Fallback mechanism: Attempt clients sequentially until successful
            foreach (var client in _clients)
            {
                price = await SafeGetLastPriceAsync(client, s, ct);
                if (price is not null)
                    break;
            }

            if (price is not null)
                dict[s] = price.Value;
        }

        return dict;
    }

    private static async Task<decimal?> SafeGetLastPriceAsync(
        IMarketDataClient client,
        string            symbol,
        CancellationToken ct)
    {
        try
        {
            return await client.GetLastPriceAsync(symbol, ct);
        }
        catch
        {
            return null;
        }
    }
}