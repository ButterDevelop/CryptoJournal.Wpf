namespace CryptoJournal.Wpf.Domain.Models;

// Represents an open fraction of a purchase or deposit transaction awaiting a closing match
public sealed record Lot(
    Guid           LotId,
    Guid           SourceFillId,
    DateTimeOffset TimeUtc,
    string         Symbol,
    decimal        QtyRemaining,
    decimal        Price,
    decimal        FeeQuoteAllocated
);