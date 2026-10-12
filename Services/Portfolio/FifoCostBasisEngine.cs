using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;

namespace CryptoJournal.Wpf.Services.Portfolio;

public sealed class FifoCostBasisEngine : ICostBasisEngine
{
    private sealed class OpenLot
    {
        public required Guid           LotId;
        public required Guid           SourceFillId;
        public required DateTimeOffset TimeUtc;
        public required string         Symbol;
        public required decimal        QtyRemaining;
        public required decimal        Price;
        public required decimal        FeeQuoteAllocated;

        public Lot ToSnapshot()
        {
            return new Lot(
                LotId,
                SourceFillId,
                TimeUtc,
                Symbol,
                QtyRemaining,
                Price,
                FeeQuoteAllocated);
        }
    }

    private readonly IEnvironmentService _env;

    public FifoCostBasisEngine(IEnvironmentService env)
    {
        _env = env;
    }

    public CostBasisResult Build(IReadOnlyList<TradeFill> fillsUtcAnyOrder)
    {
        var fills = fillsUtcAnyOrder
            .OrderBy(fill => fill, TradeFillChronologicalComparer.Instance)
            .ToList();

        var queues  = new Dictionary<string, Queue<OpenLot>>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<Match>();
        var issues  = new List<LedgerIssue>();
        var invalidSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal realized = 0m;

        foreach (var f in fills)
        {
            var symbol = f.Symbol.Trim().ToUpperInvariant();

            if (invalidSymbols.Contains(symbol))
                continue;

            if (!queues.TryGetValue(symbol, out var q))
            {
                q = new Queue<OpenLot>();
                queues[symbol] = q;
            }

            switch (f.Type)
            {
                case TxType.Buy:
                case TxType.Deposit:
                    {
                        if (string.Equals(symbol, _env.Current.QuoteCurrency, StringComparison.OrdinalIgnoreCase))
                            break;

                        var lot = new OpenLot
                        {
                            LotId             = Guid.NewGuid(),
                            SourceFillId      = f.Id,
                            TimeUtc           = f.TimeUtc,
                            Symbol            = symbol,
                            QtyRemaining      = f.Quantity,
                            Price             = f.Price,
                            FeeQuoteAllocated = f.FeeQuote
                        };
                        q.Enqueue(lot);
                        break;
                    }

                case TxType.Sell:
                case TxType.Withdraw:
                    {
                        var qtyToConsume = f.Quantity;
                        if (qtyToConsume <= 0) break;

                        var sellPrice = (f.Type == TxType.Sell) ? f.Price : 0m;
                        var available = q.Sum(lot => lot.QtyRemaining);

                        if (qtyToConsume > available)
                        {
                            issues.Add(new LedgerIssue(
                                f.Id,
                                symbol,
                                LedgerIssueKind.InsufficientSpotQuantity,
                                qtyToConsume,
                                available,
                                $"{f.Type} requests {qtyToConsume} {symbol}, but only {available} is available."));
                            invalidSymbols.Add(symbol);
                            break;
                        }

                        var totalSellQty = qtyToConsume;
                        var sellFeeTotal = f.FeeQuote;

                        while (qtyToConsume > 0m)
                        {
                            var lot  = q.Peek();
                            if (lot.QtyRemaining <= 0m)
                            {
                                q.Dequeue();
                                continue;
                            }

                            var take = Math.Min(qtyToConsume, lot.QtyRemaining);
                            if (take <= 0m)
                                break;

                            // Split the lot's remaining fee in the same proportion as its quantity.
                            var buyFeeRemaining = lot.FeeQuoteAllocated;
                            var buyFeePart      = (lot.QtyRemaining <= 0m) ? 0m : buyFeeRemaining * (take / lot.QtyRemaining);

                            var sellFeePart = (totalSellQty <= 0m) ? 0m : sellFeeTotal * (take / totalSellQty);

                            var proceeds = sellPrice * take;
                            var cost = lot.Price * take;

                            decimal pnl;
                            if (f.Type == TxType.Sell)
                                pnl = (proceeds - sellFeePart) - (cost + buyFeePart);
                            else
                                pnl = 0m;

                            var m = new Match(
                                MatchId:          Guid.NewGuid(),
                                ConsumeFillId:    f.Id,
                                SourceFillId:     lot.SourceFillId,
                                LotId:            lot.LotId,
                                Symbol:           symbol,
                                Qty:              take,
                                BuyPrice:         lot.Price,
                                SellPrice:        sellPrice,
                                FeeBuyPartQuote:  buyFeePart,
                                FeeSellPartQuote: sellFeePart,
                                RealizedPnlQuote: pnl
                            );
                            matches.Add(m);
                            realized += pnl;

                            lot.QtyRemaining      -= take;
                            lot.FeeQuoteAllocated -= buyFeePart;

                            if (lot.QtyRemaining <= 0m)
                            {
                                q.Dequeue();
                            }

                            qtyToConsume -= take;
                        }
                        break;
                    }

                case TxType.Fee:
                    // Standalone fees affect quote cash, not cost-basis lots.
                    break;

                case TxType.OpenLong:
                case TxType.CloseLong:
                case TxType.OpenShort:
                case TxType.CloseShort:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(fillsUtcAnyOrder), f.Type, "Unsupported f.Type value");
            }
        }

        var remaining = queues.Values
            .SelectMany(queue => queue)
            .Select(lot => lot.ToSnapshot())
            .ToList();

        return new CostBasisResult(remaining, matches, realized, issues);
    }
}
