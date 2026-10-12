using CryptoJournal.Wpf.Domain.Enums;
using System.Text.Json.Serialization;

namespace CryptoJournal.Wpf.Domain.Models;

public sealed record TradeFill(
    Guid                   Id,
    DateTimeOffset         TimeUtc,
    TxType                 Type,
    string                 Symbol,
    decimal                Quantity,
    decimal                Price,
    decimal                FeeQuote,
    string?                Note        = null,
    decimal                Leverage    = 1m,
    decimal?               TakeProfit  = null,
    decimal?               StopLoss    = null,
    IReadOnlyList<string>? Attachments = null
)
{
    [JsonIgnore] public decimal  ValueQuote => Quantity * Price;

    // Calculated values are not part of the stored ledger.
    [JsonIgnore] public decimal? RealizedPnlQuote { get; init; }
    [JsonIgnore] public decimal? RealizedPnlPct   { get; init; }

    public static TradeFill CreateNow(
        TxType  type,
        string  symbol,
        decimal qty,
        decimal price,
        decimal feeQuote,
        string? note = null,
        IReadOnlyList<string>? attachments = null
    ) => new(Guid.NewGuid(), DateTimeOffset.UtcNow, type, symbol.Trim().ToUpperInvariant(), qty, price, feeQuote, note, 1m, null, null, attachments);
}
