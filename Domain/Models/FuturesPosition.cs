using CryptoJournal.Wpf.Domain.Enums;

namespace CryptoJournal.Wpf.Domain.Models;

public sealed record FuturesPosition(
    string       Symbol,
    PositionSide Side,

    decimal Quantity,
    decimal AvgEntryPrice,
    decimal Leverage,

    decimal  Margin,
    decimal? LiquidationPrice,

    decimal? TakeProfit,
    decimal? StopLoss,

    decimal? MarkPrice,
    decimal? UnrealizedPnl,
    decimal? UnrealizedPnlPct,
    decimal? RoePct
);
