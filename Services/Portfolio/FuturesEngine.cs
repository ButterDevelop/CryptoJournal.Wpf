using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Portfolio;

/// <summary>
/// Aggregates OpenLong/CloseLong/OpenShort/CloseShort transactions into active FuturesPositions
/// and computes realized profit and loss for closed contracts.
/// </summary>
public sealed class FuturesEngine
{
    private sealed class OpenLot
    {
        public decimal  Qty;
        public decimal  EntryPrice;
        public decimal  Leverage;
        public decimal? TakeProfit;
        public decimal? StopLoss;
    }

    public FuturesResult Build(IReadOnlyList<TradeFill> fillsUtcAnyOrder,
                               IReadOnlyDictionary<string, decimal>? markPrices = null)
    {
        var fills = fillsUtcAnyOrder
            .Where(f => f.Type is TxType.OpenLong or TxType.CloseLong
                                or TxType.OpenShort or TxType.CloseShort)
            .OrderBy(fill => fill, TradeFillChronologicalComparer.Instance)
            .ToList();

        var queues  = new Dictionary<(string, PositionSide), Queue<OpenLot>>(SymbolSideComparer.Instance);
        decimal realizedPnl = 0m;
        var realizedRows = new List<FuturesMatch>();
        var issues       = new List<LedgerIssue>();
        var invalidKeys  = new HashSet<(string, PositionSide)>(SymbolSideComparer.Instance);

        foreach (var f in fills)
        {
            var sym  = f.Symbol.Trim().ToUpperInvariant();
            var side = f.Type is TxType.OpenLong or TxType.CloseLong ? PositionSide.Long : PositionSide.Short;
            var key  = (sym, side);

            if (invalidKeys.Contains(key))
                continue;

            if (!queues.TryGetValue(key, out var q))
                queues[key] = q = new Queue<OpenLot>();

            if (f.Type is TxType.OpenLong or TxType.OpenShort)
            {
                q.Enqueue(new OpenLot
                {
                    Qty        = f.Quantity,
                    EntryPrice = f.Price,
                    Leverage   = Math.Max(1m, f.Leverage),
                    TakeProfit = f.TakeProfit,
                    StopLoss   = f.StopLoss
                });
            }
            else
            {
                var qtyToClose = f.Quantity;
                var closePrice = f.Price;
                var available  = q.Sum(lot => lot.Qty);

                if (qtyToClose > available)
                {
                    issues.Add(new LedgerIssue(
                        f.Id,
                        sym,
                        LedgerIssueKind.InsufficientFuturesQuantity,
                        qtyToClose,
                        available,
                        $"{f.Type} requests {qtyToClose} {sym}, but only {available} is open."));
                    invalidKeys.Add(key);
                    continue;
                }

                while (qtyToClose > 0m && q.Count > 0)
                {
                    var lot  = q.Peek();
                    var take = Math.Min(lot.Qty, qtyToClose);

                    decimal pnlPerUnit = side == PositionSide.Long
                        ? closePrice - lot.EntryPrice
                        : lot.EntryPrice - closePrice;

                    decimal pnl = pnlPerUnit * take - f.FeeQuote * (take / f.Quantity);
                    realizedPnl += pnl;

                    realizedRows.Add(new FuturesMatch(
                        FillId:       f.Id,
                        Symbol:       sym,
                        Side:         side,
                        Qty:          take,
                        EntryPrice:   lot.EntryPrice,
                        ClosePrice:   closePrice,
                        Leverage:     lot.Leverage,
                        RealizedPnl:  pnl
                    ));

                    lot.Qty -= take;
                    if (lot.Qty <= 0m) q.Dequeue();

                    qtyToClose -= take;
                }
            }
        }

        markPrices ??= new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var positions = new List<FuturesPosition>();
        foreach (var ((sym, side), q) in queues)
        {
            if (q.Count == 0) continue;

            decimal totalQty  = 0m;
            decimal totalCost = 0m;
            decimal totalMarginWeighted = 0m;
            decimal? firstTp = null;
            decimal? firstSl = null;

            foreach (var lot in q)
            {
                totalQty  += lot.Qty;
                totalCost += lot.Qty * lot.EntryPrice;
                totalMarginWeighted += (lot.Qty * lot.EntryPrice / lot.Leverage);

                firstTp ??= lot.TakeProfit;
                firstSl ??= lot.StopLoss;
            }

            if (totalQty <= 0m) continue;

            var avgEntry          = totalCost / totalQty;
            var margin            = totalMarginWeighted;
            var effectiveLeverage = margin > 0m
                ? Math.Max(1m, totalCost / margin)
                : 1m;

            decimal? liqPrice = effectiveLeverage > 1m
                ? side == PositionSide.Long
                    ? avgEntry * (1m - 1m / effectiveLeverage)
                    : avgEntry * (1m + 1m / effectiveLeverage)
                : null;

            markPrices.TryGetValue(sym, out var mp);
            decimal? mark = markPrices.ContainsKey(sym) ? mp : (decimal?)null;

            decimal? upnl = null;
            decimal? upnlPct = null;
            decimal? roe = null;

            if (mark is not null)
            {
                var pnlPerUnit = side == PositionSide.Long
                    ? mark.Value - avgEntry
                    : avgEntry   - mark.Value;

                upnl    = pnlPerUnit * totalQty;
                upnlPct = totalCost > 0m ? upnl / totalCost : null;
                roe     = margin > 0m    ? upnl / margin    : null;
            }

            positions.Add(new FuturesPosition(
                Symbol:           sym,
                Side:             side,
                Quantity:         totalQty,
                AvgEntryPrice:    avgEntry,
                Leverage:         effectiveLeverage,
                Margin:           margin,
                LiquidationPrice: liqPrice,
                TakeProfit:       firstTp,
                StopLoss:         firstSl,
                MarkPrice:        mark,
                UnrealizedPnl:    upnl,
                UnrealizedPnlPct: upnlPct,
                RoePct:           roe
            ));
        }

        return new FuturesResult(
            positions.OrderBy(p => p.Symbol).ThenBy(p => p.Side).ToList(),
            realizedRows,
            realizedPnl,
            issues);
    }

    private sealed class SymbolSideComparer : IEqualityComparer<(string, PositionSide)>
    {
        public static readonly SymbolSideComparer Instance = new();
        public bool Equals((string, PositionSide) x, (string, PositionSide) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) && x.Item2 == y.Item2;
        public int GetHashCode((string, PositionSide) obj)
            => HashCode.Combine(obj.Item1.ToUpperInvariant(), obj.Item2);
    }
}

public sealed record FuturesResult(
    IReadOnlyList<FuturesPosition> Positions,
    IReadOnlyList<FuturesMatch>    Matches,
    decimal                        RealizedPnlQuote,
    IReadOnlyList<LedgerIssue>     Issues
);

public sealed record FuturesMatch(
    Guid         FillId,
    string       Symbol,
    PositionSide Side,
    decimal      Qty,
    decimal      EntryPrice,
    decimal      ClosePrice,
    decimal      Leverage,
    decimal      RealizedPnl
);
