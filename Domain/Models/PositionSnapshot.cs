namespace CryptoJournal.Wpf.Domain.Models;

public sealed record PositionSnapshot(
    string   Symbol,
    decimal  Quantity,
    decimal  CostBasisQuote,
    decimal? MarkPriceQuote,
    decimal? MarketValueQuote,
    decimal? UnrealizedPnlQuote,
    decimal? UnrealizedPnlPct
);
