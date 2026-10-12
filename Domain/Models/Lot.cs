namespace CryptoJournal.Wpf.Domain.Models;

public sealed record Lot(
    Guid           LotId,
    Guid           SourceFillId,
    DateTimeOffset TimeUtc,
    string         Symbol,
    decimal        QtyRemaining,
    decimal        Price,
    decimal        FeeQuoteAllocated
);
