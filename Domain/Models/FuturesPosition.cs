using CryptoJournal.Wpf.Domain.Enums;

namespace CryptoJournal.Wpf.Domain.Models;

/// <summary>
/// Computed aggregate of an open futures position (Long or Short), derived from active fills.
/// </summary>
public sealed record FuturesPosition(
    string       Symbol,
    PositionSide Side,

    decimal Quantity,       // Total active contract size measured in the base asset
    decimal AvgEntryPrice,  // Volume-weighted average entry price across all open fills
    decimal Leverage,       // Leverage multiplier applied to the position (e.g., 10 for 10x)

    decimal  Margin,             // Required initial margin (Quantity * AvgEntryPrice / Leverage)
    decimal? LiquidationPrice,   // Estimated liquidation price (null for un-leveraged positions)

    decimal? TakeProfit,         // Target take-profit price (inherited from initial open fill)
    decimal? StopLoss,           // Target stop-loss price (inherited from initial open fill)

    decimal? MarkPrice,          // Latest retrieved market price (injected during computation)
    decimal? UnrealizedPnl,      // Unrealized Profit and Loss calculated in the quote currency
    decimal? UnrealizedPnlPct,   // Unrealized PnL ratio relative to the initial cost basis
    decimal? RoePct              // Return on Equity ratio (UnrealizedPnl / Margin)
);
