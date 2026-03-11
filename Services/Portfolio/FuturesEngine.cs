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
        public decimal  WeightedCost => Qty * EntryPrice;
    }

    public FuturesResult Build(IReadOnlyList<TradeFill> fillsUtcAnyOrder,
                               IReadOnlyDictionary<string, decimal>? markPrices = null)
    {
        var fills = fillsUtcAnyOrder
            .Where(f => f.Type is TxType.OpenLong or TxType.CloseLong
                                or TxType.OpenShort or TxType.CloseShort)
            .OrderBy(f => f.TimeUtc)
            .ThenBy(f => f.Id)
            .ToList();

        // Key composition: (SYMBOL, position side)
        var queues  = new Dictionary<(string, PositionSide), Queue<OpenLot>>(SymbolSideComparer.Instance);
        decimal realizedPnl = 0m;
        var realizedRows = new List<FuturesMatch>();

        foreach (var f in fills)
        {
            var sym  = f.Symbol.Trim().ToUpperInvariant();
            var side = f.Type is TxType.OpenLong or TxType.CloseLong ? PositionSide.Long : PositionSide.Short;
            var key  = (sym, side);

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
            else // Handle CloseLong or CloseShort transactions
            {
                var qtyToClose = f.Quantity;
                var closePrice = f.Price;

                while (qtyToClose > 0m && q.Count > 0)
                {
                    var lot  = q.Peek();
                    var take = Math.Min(lot.Qty, qtyToClose);

                    // PnL per 1 unit * take * leverage
                    decimal pnlPerUnit = side == PositionSide.Long
                        ? closePrice - lot.EntryPrice   // long: profit when price goes up
                        : lot.EntryPrice - closePrice;  // short: profit when price goes down

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

        // Synthesize open futures positions from the aggregated queue remnants
        var positions = new List<FuturesPosition>();
        foreach (var ((sym, side), q) in queues)
        {
            if (q.Count == 0) continue;

            // Compute volume-weighted average entry price
            decimal totalQty  = 0m;
            decimal totalCost = 0m;
            decimal totalMarginWeighted = 0m;
            decimal leverageWeighted    = 0m;
            decimal? firstTp = null;
            decimal? firstSl = null;

            foreach (var lot in q)
            {
                totalQty  += lot.Qty;
                totalCost += lot.Qty * lot.EntryPrice;
                totalMarginWeighted += (lot.Qty * lot.EntryPrice / lot.Leverage);
                leverageWeighted    += lot.Qty * lot.Leverage;

                // Inherit the Take-Profit/Stop-Loss targets from the oldest active lot
                firstTp ??= lot.TakeProfit;
                firstSl ??= lot.StopLoss;
            }

            if (totalQty <= 0m) continue;

            var avgEntry  = totalCost / totalQty;
            var avgLevg   = leverageWeighted / totalQty;
            var margin    = totalMarginWeighted; // sum of (qty * price / lev) per lot

            // Estimated liquidation price (simplified, isolated margin model):
            // Long:  liq ≈ avgEntry * (1 - 1/leverage)
            // Short: liq ≈ avgEntry * (1 + 1/leverage)
            decimal? liqPrice = avgLevg > 1m
                ? side == PositionSide.Long
                    ? avgEntry * (1m - 1m / avgLevg)
                    : avgEntry * (1m + 1m / avgLevg)
                : null;

            markPrices ??= new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
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
                Leverage:         avgLevg,
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
            realizedPnl);
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
    decimal                        RealizedPnlQuote
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
