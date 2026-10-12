using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Portfolio;

public sealed class TransactionPnlCalculator
{
    private readonly ICostBasisEngine _spot;
    private readonly FuturesEngine    _futures;

    public TransactionPnlCalculator(ICostBasisEngine spot, FuturesEngine futures)
    {
        _spot    = spot;
        _futures = futures;
    }

    public TransactionPnlCalculation Calculate(IReadOnlyList<TradeFill> fills)
    {
        ArgumentNullException.ThrowIfNull(fills);

        var fillTypes = fills.ToDictionary(fill => fill.Id, fill => fill.Type);
        var results   = new Dictionary<Guid, TransactionPnl>();
        var spot      = _spot.Build(fills);
        var futures   = _futures.Build(fills);

        var invalidSpotTransactions = spot.Issues
            .Select(issue => issue.TransactionId)
            .ToHashSet();

        AddSpotResults(spot, fillTypes, invalidSpotTransactions, results);
        AddFuturesResults(futures, results);

        return new TransactionPnlCalculation(
            results,
            spot.Issues.Concat(futures.Issues).ToList());
    }

    private static void AddSpotResults(
        CostBasisResult                 spot,
        IReadOnlyDictionary<Guid, TxType> fillTypes,
        IReadOnlySet<Guid>               invalidTransactions,
        IDictionary<Guid, TransactionPnl> results)
    {
        var sellMatches = spot.Matches
            .Where(match => fillTypes.GetValueOrDefault(match.ConsumeFillId) == TxType.Sell)
            .GroupBy(match => match.ConsumeFillId);

        foreach (var matches in sellMatches)
        {
            if (invalidTransactions.Contains(matches.Key))
                continue;

            var realizedPnl = matches.Sum(match => match.RealizedPnlQuote);
            var costBasis   = matches.Sum(match =>
                match.BuyPrice * match.Qty + match.FeeBuyPartQuote);

            results[matches.Key] = new TransactionPnl(
                realizedPnl,
                costBasis > 0m ? realizedPnl / costBasis : null);
        }
    }

    private static void AddFuturesResults(
        FuturesResult                    futures,
        IDictionary<Guid, TransactionPnl> results)
    {
        foreach (var matches in futures.Matches.GroupBy(match => match.FillId))
        {
            var realizedPnl = matches.Sum(match => match.RealizedPnl);
            var margin      = matches.Sum(match =>
                match.Qty * match.EntryPrice / match.Leverage);

            results[matches.Key] = new TransactionPnl(
                realizedPnl,
                margin > 0m ? realizedPnl / margin : null);
        }
    }
}

public sealed record TransactionPnl(
    decimal  RealizedPnlQuote,
    decimal? RealizedPnlPct);

public sealed record TransactionPnlCalculation(
    IReadOnlyDictionary<Guid, TransactionPnl> ByTransactionId,
    IReadOnlyList<LedgerIssue>                 Issues);
