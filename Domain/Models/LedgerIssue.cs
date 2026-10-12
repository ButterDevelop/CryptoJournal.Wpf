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
    string          Message,
    bool            IsReconciled = false)
{
    public decimal ShortageQuantity => Math.Max(0m, RequestedQuantity - AvailableQuantity);

    public string DisplayMessage => IsReconciled
        ? $"{Message} Later transactions cover the shortage, so the current position was recovered automatically. Realized PnL for this transaction is unavailable."
        : $"{Message} Add or correct an earlier transaction to restore the position.";
}
