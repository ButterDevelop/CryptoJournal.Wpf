namespace CryptoJournal.Wpf.Domain.Models;

public sealed record PositionSnapshot(
    string   Symbol,
    decimal  Quantity,
    decimal  CostBasisQuote,     // Total cost basis (includes proportional purchase fees)
    decimal? MarkPriceQuote,     // Current market price (if available)
    decimal? MarketValueQuote,   // Derived market value (Quantity * MarkPriceQuote)
    decimal? UnrealizedPnlQuote, // Calculated unrealized PnL (MarketValueQuote - CostBasisQuote)
    decimal? UnrealizedPnlPct    // Return on Investment percentage (UnrealizedPnlQuote / CostBasisQuote)
);