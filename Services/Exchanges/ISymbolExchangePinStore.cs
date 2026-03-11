namespace CryptoJournal.Wpf.Services.Exchanges
{
    /// <summary>
    /// Persists a mapping: symbol -> exchangeId (preferred candle source).
    /// </summary>
    public interface ISymbolExchangePinStore
    {
        Task<string?> GetAsync(string symbol, CancellationToken ct = default);
        Task SetAsync(string symbol, string exchangeId, CancellationToken ct = default);
        Task RemoveAsync(string symbol, CancellationToken ct = default);
    }
}