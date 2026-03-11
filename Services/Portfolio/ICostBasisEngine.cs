using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Portfolio;

public interface ICostBasisEngine
{
    CostBasisResult Build(IReadOnlyList<TradeFill> fillsUtcAnyOrder);
}

public sealed record CostBasisResult(
    IReadOnlyList<Lot>   LotsRemaining,
    IReadOnlyList<Match> Matches,
    decimal              RealizedPnlQuote
);