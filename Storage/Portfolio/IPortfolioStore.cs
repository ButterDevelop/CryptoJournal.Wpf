using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Storage.Portfolio;

public interface IPortfolioStore
{
    Task<IReadOnlyList<TradeFill>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyList<TradeFill> fills, CancellationToken ct = default);
}