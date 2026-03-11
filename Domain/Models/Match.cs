namespace CryptoJournal.Wpf.Domain.Models;

// Represents a matching relationship between a closing transaction and its source lot
public sealed record Match(
    Guid    MatchId,
    Guid    ConsumeFillId,   // Source transaction ID (e.g., Sell or Withdraw)
    Guid    SourceFillId,    // Target transaction ID (e.g., Buy or Deposit)
    Guid    LotId,
    string  Symbol,
    decimal Qty,
    decimal BuyPrice,
    decimal SellPrice,
    decimal FeeBuyPartQuote,
    decimal FeeSellPartQuote,
    decimal RealizedPnlQuote  // Computed realized PnL in the active quote currency
);