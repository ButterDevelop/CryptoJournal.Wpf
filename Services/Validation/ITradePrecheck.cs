using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Validation;

public sealed record PrecheckResult(bool Ok, string Message);

public interface ITradePrecheck
{
    PrecheckResult Validate(
        IReadOnlyList<TradeFill> existingFills,
        DateTime                 timeUtc,
        TxType                   type,
        string                   symbol,
        decimal                  qty,
        decimal                  price,
        decimal                  feeQuote,
        decimal                  leverage,
        string                   quoteSymbol);
}