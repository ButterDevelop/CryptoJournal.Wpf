namespace CryptoJournal.Wpf.Domain.Models;

public sealed record Match(
    Guid    MatchId,
    Guid    ConsumeFillId,
    Guid    SourceFillId,
    Guid    LotId,
    string  Symbol,
    decimal Qty,
    decimal BuyPrice,
    decimal SellPrice,
    decimal FeeBuyPartQuote,
    decimal FeeSellPartQuote,
    decimal RealizedPnlQuote
);
