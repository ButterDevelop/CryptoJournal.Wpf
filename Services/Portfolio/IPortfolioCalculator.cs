using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Portfolio;

public interface IPortfolioCalculator
{
    PortfolioSnapshot Calculate(IReadOnlyList<TradeFill> fillsUtcAnyOrder, IReadOnlyDictionary<string, decimal>? markPrices = null);
}