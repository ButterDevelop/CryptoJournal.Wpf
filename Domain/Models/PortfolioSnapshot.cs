namespace CryptoJournal.Wpf.Domain.Models;

public sealed record PortfolioSnapshot(
    IReadOnlyList<PositionSnapshot>  Positions,
    IReadOnlyList<Lot>               LotsRemaining,
    IReadOnlyList<Match>             Matches,
    decimal                          RealizedPnlQuote,
    decimal?                         EquityQuote,
    IReadOnlyList<FuturesPosition>   FuturesPositions,
    decimal                          FuturesRealizedPnlQuote
);