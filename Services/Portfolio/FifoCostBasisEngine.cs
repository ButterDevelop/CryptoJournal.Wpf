using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;

namespace CryptoJournal.Wpf.Services.Portfolio;

public sealed class FifoCostBasisEngine : ICostBasisEngine
{
    private readonly IEnvironmentService _env;

    public FifoCostBasisEngine(IEnvironmentService env)
    {
        _env = env;
    }

    public CostBasisResult Build(IReadOnlyList<TradeFill> fillsUtcAnyOrder)
    {
        var fills = fillsUtcAnyOrder
            .OrderBy(f => f.TimeUtc)
            .ThenBy(f => f.Id)
            .ToList();

        // Maintain chronologically ordered (FIFO) lots per symbol
        var queues  = new Dictionary<string, Queue<Lot>>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<Match>();
        decimal realized = 0m;

        foreach (var f in fills)
        {
            var symbol = f.Symbol.Trim().ToUpperInvariant();
            if (!queues.TryGetValue(symbol, out var q))
            {
                q = new Queue<Lot>();
                queues[symbol] = q;
            }

            switch (f.Type)
            {
                case TxType.Buy:
                case TxType.Deposit:
                    {
                        // Standard Buy or Deposit operations instantiate new lots.
                        // Commensurate Quote fees are allocated entirely into the lot, establishing total cost basis.

                        if (string.Equals(symbol, _env.Current.QuoteCurrency, StringComparison.OrdinalIgnoreCase))
                            break;

                        var lot = new Lot(
                            LotId:             Guid.NewGuid(),
                            SourceFillId:      f.Id,
                            TimeUtc:           f.TimeUtc,
                            Symbol:            symbol,
                            QtyRemaining:      f.Quantity,
                            Price:             f.Price,
                            FeeQuoteAllocated: f.FeeQuote
                        );
                        q.Enqueue(lot);
                        break;
                    }

                case TxType.Sell:
                case TxType.Withdraw:
                    {
                        // Sell consumes lots and creates Matches.
                        // Withdraw consumes lots too, but SellPrice=0 and PnL=0 (transfer out)
                        var qtyToConsume = f.Quantity;
                        if (qtyToConsume <= 0) break;

                        var sellPrice = (f.Type == TxType.Sell) ? f.Price : 0m;

                        // Fee on the "consume fill" is distributed proportionally by matches
                        // (if the Sell is partially closed by several lots - fee split by qty)
                        var totalSellQty = qtyToConsume;
                        var sellFeeTotal = f.FeeQuote;

                        while (qtyToConsume > 0m)
                        {
                            if (q.Count == 0)
                            {
                                // synthetic lot to prevent infinite loop + keep accounting consistent
                                // Price = sellPrice so PnL ~ -sellFeePart (fees still counted), not some crazy value
                                q.Enqueue(new Lot(
                                    LotId:             Guid.NewGuid(),
                                    SourceFillId:      f.Id,
                                    TimeUtc:           f.TimeUtc,
                                    Symbol:            symbol,
                                    QtyRemaining:      qtyToConsume,
                                    Price:             sellPrice,
                                    FeeQuoteAllocated: 0m
                                ));
                            }

                            var lot  = q.Peek();
                            if (lot.QtyRemaining <= 0m)
                            {
                                q.Dequeue();
                                continue;
                            }

                            var take = Math.Min(qtyToConsume, lot.QtyRemaining);
                            if (take <= 0m)
                                break;

                            // Buy fee portion: proportional to the "take" of the lot's remaining amount
                            // (we store the fee in the lot as the total fee of the original buy; it's more correct to store fee-per-qty)
                            // For MVP: recalculating based on the original qty is impossible (we don't store the original qty).
                            // Therefore: calculating fee-per-qty based on the current lot.QtyRemaining + the amount already consumed is not possible.
                            // Solution: store fee-per-qty when creating the lot:
                            // This is simpler: store FeeQuoteAllocated as the "remaining fee" and withdraw proportionally.
                            var buyFeeRemaining = lot.FeeQuoteAllocated;
                            var buyFeePart      = (lot.QtyRemaining <= 0m) ? 0m : buyFeeRemaining * (take / lot.QtyRemaining);

                            // sell fee part by qty
                            var sellFeePart = (totalSellQty <= 0m) ? 0m : sellFeeTotal * (take / totalSellQty);

                            // Realized PnL (in USDT): (sell proceeds - sell fee part) - (buy cost + buy fee part)
                            var proceeds = sellPrice * take;
                            var cost = lot.Price * take;

                            decimal pnl;
                            if (f.Type == TxType.Sell)
                                pnl = (proceeds - sellFeePart) - (cost + buyFeePart);
                            else
                                pnl = 0m; // withdraw/transfer out: calculate pnl 0

                            if (take <= 0m)
                                continue;

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

                            // update lot
                            var newQty = lot.QtyRemaining - take;
                            var newFee = lot.FeeQuoteAllocated - buyFeePart;

                            q.Dequeue();
                            if (newQty > 0m)
                            {
                                q.Enqueue(lot with { QtyRemaining = newQty, FeeQuoteAllocated = newFee });
                            }

                            qtyToConsume -= take;
                        }
                        break;
                    }

                case TxType.Fee:
                    // For MVP: Fee as a separate transaction, we do not touch the lots
                    // It can then be taken into account as a cash expense
                    break;

                // Futures transactions are handled by FuturesEngine — skip them here
                case TxType.OpenLong:
                case TxType.CloseLong:
                case TxType.OpenShort:
                case TxType.CloseShort:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(fillsUtcAnyOrder), f.Type, "Unsupported f.Type value");
            }
        }

        var remaining = queues.Values.SelectMany(x => x).ToList();
        return new CostBasisResult(remaining, matches, realized);
    }
}