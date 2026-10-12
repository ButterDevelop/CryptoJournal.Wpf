namespace CryptoJournal.Wpf.Domain.Models;

public enum LedgerIssueKind
{
    InsufficientSpotQuantity,
    InsufficientFuturesQuantity
}

public sealed record LedgerIssue(
    Guid            TransactionId,
    string          Symbol,
    LedgerIssueKind Kind,
    decimal         RequestedQuantity,
    decimal         AvailableQuantity,
    string          Message);
