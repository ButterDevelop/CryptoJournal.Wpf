using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.UI;

namespace CryptoJournal.Wpf.Services.Portfolio;

public sealed class PortfolioCalculator : IPortfolioCalculator
{
    private readonly IEnvironmentService _env;
    private readonly ICostBasisEngine    _engine;
    private readonly FuturesEngine       _futures;

    public PortfolioCalculator(IEnvironmentService env, ICostBasisEngine engine, FuturesEngine futures)
    {
        _env     = env;
        _engine  = engine;
        _futures = futures;
    }

    public PortfolioSnapshot Calculate(IReadOnlyList<TradeFill> fillsUtcAnyOrder, IReadOnlyDictionary<string, decimal>? markPrices = null)
    {
        string quote = _env.Current.QuoteCurrency;

        // Execute Spot FIFO processing
        var result = _engine.Build(fillsUtcAnyOrder);

        var positions = result.LotsRemaining
            .Where(l => !SymbolUtil.IsQuote(l.Symbol, quote))
            .GroupBy(l => l.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var qty  = g.Sum(x => x.QtyRemaining);
                var cost = g.Sum(x => (x.Price * x.QtyRemaining) + x.FeeQuoteAllocated);

                markPrices ??= new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                markPrices.TryGetValue(g.Key, out var mp);
                decimal? mark = markPrices.ContainsKey(g.Key) ? mp : null;

                decimal? mv   = mark is null ? null : qty * mark.Value;
                decimal? upnl = (mv is null) ? null : mv.Value - cost;

                decimal? upnlPct = (upnl is null || cost <= 0m) ? null : upnl.Value / cost;

                return new PositionSnapshot(
                    Symbol:             g.Key,
                    Quantity:           qty,
                    CostBasisQuote:     cost,
                    MarkPriceQuote:     mark,
                    MarketValueQuote:   mv,
                    UnrealizedPnlQuote: upnl,
                    UnrealizedPnlPct:   upnlPct
                );
            })
            .OrderBy(p => p.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        decimal? equity = null;
        if (positions.All(p => p.MarketValueQuote is not null))
            equity = positions.Sum(p => p.MarketValueQuote!.Value);

        // Execute Futures margin/position processing
        var futuresResult = _futures.Build(fillsUtcAnyOrder, markPrices);

        return new PortfolioSnapshot(
            Positions:              positions,
            LotsRemaining:          result.LotsRemaining,
            Matches:                result.Matches,
            RealizedPnlQuote:       result.RealizedPnlQuote,
            EquityQuote:            equity,
            FuturesPositions:       futuresResult.Positions,
            FuturesRealizedPnlQuote: futuresResult.RealizedPnlQuote
        );
    }
}