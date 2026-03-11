using CommunityToolkit.Mvvm.ComponentModel;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Candles;
using CryptoJournal.Wpf.Services.CryptoIcon;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Prices;
using CryptoJournal.Wpf.Storage.Scenarios;
using CryptoJournal.Wpf.UI;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CryptoJournal.Wpf.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IEnvironmentService      _env;
    private readonly ICryptoIconCache         _icons;
    private readonly Dispatcher               _ui;
    private readonly MarketDataPollingService _poll;
    private readonly IScenarioStore           _store;
    private readonly AthService               _ath;

    // noisy tick dedup
    private readonly ConcurrentDictionary<string, decimal> _lastMarkBySymbol
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, (PieSeries<double> Series, ObservableCollection<double> Values)> _allocBySymbol
        = new(StringComparer.OrdinalIgnoreCase);

    // noisy tick dedup for ATH updates
    private readonly ConcurrentDictionary<string, decimal> _lastAthBySymbol
        = new(StringComparer.OrdinalIgnoreCase);

    public DashboardViewModel(
        IEnvironmentService      env,
        ICryptoIconCache         icons,
        MarketDataPollingService poll,
        IScenarioStore           store,
        AthService               athService)
    {
        _env   = env;
        _icons = icons;
        _ui    = Application.Current.Dispatcher;
        _poll  = poll;
        _store = store;
        _ath   = athService;

        _poll.LastPriceUpdated  += OnLastPriceUpdated;
        _store.ScenariosChanged += OnScenariosChanged;
        _ath.AthUpdated         += OnAthUpdated;

        // axes + series
        _pnlValueAxis = new Axis
        {
            IsVisible       = false,
            SeparatorsPaint = new SolidColorPaint(new SKColor(255, 255, 255, 35)),
            MinStep         = 1
        };

        _pnlCategoryAxis = new Axis
        {
            IsVisible = false,
            MinLimit  = -0.5,
            MaxLimit  = 0.5
        };

        PnlXAxes = [_pnlValueAxis];
        PnlYAxes = [_pnlCategoryAxis];

        _unrealizedRowSeries = new StackedRowSeries<double>
        {
            Name                   = "Unrealized",
            Values                 = _unrealizedRowValues,
            Stroke                 = null,
            MaxBarWidth            = 70,
            DataLabelsSize         = 16,
            DataLabelsPaint        = new SolidColorPaint(SKColors.White),
            DataLabelsPosition     = DataLabelsPosition.Middle,
            DataLabelsFormatter    = p =>
            {
                var v = p.Coordinate.PrimaryValue;
                return Math.Abs(v) < 1e-9 ? "" : $"{v.ToString("C2", UsCulture)}";
            },
            YToolTipLabelFormatter = p => p.Coordinate.PrimaryValue.ToString("C2", UsCulture),
        };

        _realizedRowSeries = new StackedRowSeries<double>
        {
            Name                   = "Realized",
            Values                 = _realizedRowValues,
            Stroke                 = null,
            MaxBarWidth            = 70,
            DataLabelsSize         = 16,
            DataLabelsPaint        = new SolidColorPaint(SKColors.White),
            DataLabelsPosition     = DataLabelsPosition.Middle,
            DataLabelsFormatter    = p =>
            {
                var v = p.Coordinate.PrimaryValue;
                return Math.Abs(v) < 1e-9 ? "" : $"{v.ToString("C2", UsCulture)}";
            },
            YToolTipLabelFormatter = p => p.Coordinate.PrimaryValue.ToString("C2", UsCulture),
        };

        _realizedRowSeries.DataLabelsPaint   = _pnlLabelPaint;
        _unrealizedRowSeries.DataLabelsPaint = _pnlLabelPaint;

        PnlSeries.Add(_unrealizedRowSeries);
        PnlSeries.Add(_realizedRowSeries);
    }

    // ======= scenario rows =======
    public ObservableCollection<ScenarioRowVm> ScenarioRows { get; } = [];

    [ObservableProperty] private decimal  realizedPnl;
    [ObservableProperty] private decimal? unrealizedPnl;
    [ObservableProperty] private decimal  cashQuote;
    [ObservableProperty] private decimal? positionsValue;
    [ObservableProperty] private decimal? totalEquity;
    [ObservableProperty] private decimal? equityAtScenarios;

    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    private readonly ObservableCollection<double> _realizedRowValues   = [0d];
    private readonly ObservableCollection<double> _unrealizedRowValues = [0d];

    private readonly StackedRowSeries<double> _realizedRowSeries;
    private readonly StackedRowSeries<double> _unrealizedRowSeries;

    private readonly Axis _pnlValueAxis;
    private readonly Axis _pnlCategoryAxis;

    private readonly SolidColorPaint _pnlLabelPaint = ChartColors.CreatePnlLabelPaint();
    private double _allocTotal;

    public ObservableCollection<ISeries> AllocationSeries { get; } = [];
    public ObservableCollection<ISeries> PnlSeries        { get; } = [];
    public Axis[] PnlXAxes { get; }
    public Axis[] PnlYAxes { get; }

    // Called by MainViewModel calc pipeline
    public async Task UpdateAsync(
        PortfolioSnapshot                     snap,
        IReadOnlyList<TradeFill>              fills,
        IReadOnlyDictionary<string, decimal>? marks,
        string                                quote = "USDT",
        CancellationToken                     ct    = default)
    {
        ArgumentNullException.ThrowIfNull(snap);
        ArgumentNullException.ThrowIfNull(fills);

        RealizedPnl = snap.RealizedPnlQuote + snap.FuturesRealizedPnlQuote;

        bool allUnrealSpotKnown = snap.Positions.All(p => p.UnrealizedPnlQuote is not null);
        bool allUnrealFutKnown  = snap.FuturesPositions.All(p => p.UnrealizedPnl is not null);
        
        UnrealizedPnl = (allUnrealSpotKnown && allUnrealFutKnown)
            ? snap.Positions.Sum(p => p.UnrealizedPnlQuote!.Value) + snap.FuturesPositions.Sum(p => p.UnrealizedPnl!.Value)
            : null;

        var baseCash     = CashCalculator.ComputeQuoteCash(fills, quote);
        var lockedMargin = snap.FuturesPositions.Sum(p => p.Margin);
        
        CashQuote = baseCash + snap.FuturesRealizedPnlQuote - lockedMargin;

        bool allMvKnown = snap.Positions.All(p => p.MarketValueQuote is not null);
        
        PositionsValue = (allMvKnown && allUnrealFutKnown)
            ? snap.Positions.Sum(p => p.MarketValueQuote!.Value) + snap.FuturesPositions.Sum(p => p.Margin + p.UnrealizedPnl!.Value)
            : null;

        TotalEquity = PositionsValue is null ? null : PositionsValue.Value + CashQuote;

        await BuildScenarioTableAsync(snap, marks, ct).ConfigureAwait(false);

        await _ui.InvokeAsync(() =>
        {
            BuildCharts(snap);
            RecalcEquityAtScenarios();
        }, DispatcherPriority.Background, ct);
    }

    // ======= table builder =======
    private async Task BuildScenarioTableAsync(
        PortfolioSnapshot                     snap,
        IReadOnlyDictionary<string, decimal>? marks,
        CancellationToken                     ct)
    {
        var rows = new List<ScenarioRowVm>();

        foreach (var p in snap.Positions.Where(p => !SymbolUtil.IsQuote(p.Symbol, _env.Current.QuoteCurrency)))
        {
            ct.ThrowIfCancellationRequested();

            var sym = (p.Symbol ?? "").Trim().ToUpperInvariant();
            if (sym.Length == 0) continue;

            decimal? mark = null;
            if (marks is not null && marks.TryGetValue(sym, out var mp)) mark = mp;

            // Build scenario totals from store (SPOT)
            var (plannedQty, planValue) = ComputePlanTotalsSpot(sym, p.Quantity);

            var vm = new ScenarioRowVm(sym, p.Quantity)
            {
                Mark       = mark,
                PlannedQty = plannedQty,
                PlanValue  = planValue,
                Ath        = _ath.TryGetCachedAth(sym),
            };

            _ath.PrefetchAth(sym, CancellationToken.None);

            rows.Add(vm);
        }

        foreach (var p in snap.FuturesPositions)
        {
            ct.ThrowIfCancellationRequested();

            var sym = (p.Symbol ?? "").Trim().ToUpperInvariant();
            if (sym.Length == 0) continue;

            decimal? mark = null;
            if (marks is not null && marks.TryGetValue(sym, out var mp)) mark = mp;

            var keyStr = $"{sym}:{p.Side}";
            
            // Build scenario totals from store (FUTURES)
            var (plannedQty, planValue) = ComputePlanTotalsFutures(sym, p.Side, p.Quantity, p.AvgEntryPrice, p.Leverage);

            var vm = new ScenarioRowVm(sym, p.Quantity)
            {
                IsFutures  = true,
                Side       = p.Side,
                EntryPrice = p.AvgEntryPrice,
                Leverage   = p.Leverage,
                Margin     = p.Margin,
                Mark       = mark,
                PlannedQty = plannedQty,
                PlanValue  = planValue, // Now correctly representing margin + pnl
                Ath        = _ath.TryGetCachedAth(sym),
            };

            _ath.PrefetchAth(sym, CancellationToken.None);

            rows.Add(vm);
        }

        // Replace UI collection on UI thread
        await _ui.InvokeAsync(() =>
        {
            ScenarioRows.Clear();
            foreach (var r in rows)
                ScenarioRows.Add(r);

            RecalcEquityAtScenarios();
        }, DispatcherPriority.DataBind, ct);

        await LoadScenarioIconsAsync(rows, ct).ConfigureAwait(false);
    }

    private (decimal PlannedQty, decimal PlanValue) ComputePlanTotalsSpot(string symbol, decimal positionQty)
    {
        var plan = GetPlan(symbol);
        if (plan is null || plan.Legs is null || plan.Legs.Count == 0 || positionQty <= 0m)
            return (0m, 0m);

        decimal plannedQty = 0m;
        decimal planValue  = 0m;

        foreach (var leg in plan.Legs)
        {
            if (leg.InputAmount <= 0m) continue;
            if (leg.TargetPrice <= 0m) continue;

            var legQty = plan.IsPercentMode
                ? positionQty * (leg.InputAmount / 100m)
                : leg.InputAmount;

            if (legQty <= 0m) continue;

            plannedQty += legQty;
            planValue  += legQty * leg.TargetPrice;
        }

        if (plannedQty > positionQty) plannedQty = positionQty;

        return (plannedQty, planValue);
    }

    private (decimal PlannedQty, decimal PlanValue) ComputePlanTotalsFutures(string symbol, PositionSide side, decimal positionQty, decimal entryPrice, decimal leverage)
    {
        var planKey = $"{symbol}:{side}";
        var plan = GetPlan(planKey);
        if (plan is null || plan.Legs is null || plan.Legs.Count == 0 || positionQty <= 0m)
            return (0m, 0m);

        decimal plannedQty = 0m;
        decimal planValue  = 0m;

        foreach (var leg in plan.Legs)
        {
            if (leg.InputAmount <= 0m) continue;
            if (leg.TargetPrice <= 0m) continue;

            var legQty = plan.IsPercentMode
                ? positionQty * (leg.InputAmount / 100m)
                : leg.InputAmount;

            if (legQty <= 0m) continue;

            var pnlPerUnit = side == PositionSide.Long ? leg.TargetPrice - entryPrice : entryPrice - leg.TargetPrice;
            var upnl = pnlPerUnit * legQty;
            var marginReturn = (legQty * entryPrice) / leverage;

            plannedQty += legQty;
            planValue  += marginReturn + upnl;
        }

        if (plannedQty > positionQty) plannedQty = positionQty;

        return (plannedQty, planValue);
    }

    private ScenarioPlanDto? GetPlan(string symbol)
        => _store.TryGetPlan(symbol);

    private async Task LoadScenarioIconsAsync(List<ScenarioRowVm> rows, CancellationToken ct)
    {
        var distinctSymbols = rows.Select(r => r.Symbol)
                                  .Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .ToList();

        var tasks = distinctSymbols.ToDictionary(s => s, s => _icons.GetAsync(s, ct), StringComparer.OrdinalIgnoreCase);

        foreach (var kv in tasks)
        {
            ct.ThrowIfCancellationRequested();

            ImageSource? icon = null;
            try { icon = await kv.Value; }
            catch { /* ignore */ }

            if (icon is null) continue;

            var sym = kv.Key;

            await _ui.InvokeAsync(() =>
            {
                foreach (var r in ScenarioRows.Where(r => sym.Equals(r.Symbol, StringComparison.OrdinalIgnoreCase)))
                    r.SymbolIcon = icon;
            }, DispatcherPriority.Background, ct);
        }
    }

    // ======= scenario change handler =======
    private async void OnScenariosChanged(string symbol)
    {
        try
        {
            symbol = (symbol ?? "").Trim().ToUpperInvariant();
            if (symbol.Length == 0) return;

            // planKey could be "BTC" or "BTC:Long"
            // We just update all ScenarioRows that match the symbol or symbol:side
            await _ui.InvokeAsync(() =>
            {
                bool any = false;
                foreach (var row in ScenarioRows)
                {
                    var rowKey = row.IsFutures ? $"{row.Symbol}:{row.Side}" : row.Symbol;
                    var rowSym = (row.Symbol ?? "").Trim().ToUpperInvariant();
                    rowKey = rowKey?.Trim().ToUpperInvariant();

                    if (symbol.Equals(rowKey, StringComparison.OrdinalIgnoreCase) || symbol.Equals(rowSym, StringComparison.OrdinalIgnoreCase))
                    {
                        if (row.IsFutures)
                        {
                            var (plannedQty, planValue) = ComputePlanTotalsFutures(rowSym, row.Side, row.Qty, row.EntryPrice, row.Leverage);
                            row.PlannedQty = plannedQty;
                            row.PlanValue  = planValue;
                        }
                        else
                        {
                            var (plannedQty, planValue) = ComputePlanTotalsSpot(rowSym, row.Qty);
                            row.PlannedQty = plannedQty;
                            row.PlanValue  = planValue;
                        }
                        any = true;
                    }
                }

                if (any) RecalcEquityAtScenarios();
            }, DispatcherPriority.Background);
        }
        catch
        {
            // never throw
        }
    }

    // ======= polling updates =======
    private async void OnLastPriceUpdated(string symbol, decimal price)
    {
        try
        {
            symbol = (symbol ?? "").Trim().ToUpperInvariant();
            if (symbol.Length == 0) return;
            if (price <= 0m) return;

            if (SymbolUtil.IsQuote(symbol, _env.Current.QuoteCurrency))
                return;

            if (_lastMarkBySymbol.TryGetValue(symbol, out var prev) && Math.Abs(prev - price) < 0.00000001m)
                return;

            _lastMarkBySymbol[symbol] = price;

            await _ui.InvokeAsync(() =>
            {
                bool any = false;
                foreach (var row in ScenarioRows.Where(r => symbol.Equals((r.Symbol ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)))
                {
                    row.Mark = price;
                    any = true;
                }

                if (!any) return;

                // These can now be recomputed live
                RecalcEquityAtScenarios();
                RecalcPositionsValueAndTotalEquityFromScenarioRows();
            }, DispatcherPriority.Background);
        }
        catch { /* ignore */ }
    }

    private void RecalcEquityAtScenarios()
    {
        // require Mark for all rows to show an exact number
        // (same honesty model as UnrealizedPnl property)
        decimal? sum = 0m;

        foreach (var contrib in ScenarioRows.Select(sr => sr.EquityContributionAtScenario))
        {
            if (contrib is null)
            {
                sum = null;
                break;
            }

            sum += contrib.Value;
        }

        EquityAtScenarios = sum is null ? null : sum.Value + CashQuote;
    }

    private void RecalcPositionsValueAndTotalEquityFromScenarioRows()
    {
        decimal? total = 0m;

        foreach (var curVal in ScenarioRows.Select(sr => sr.CurrentValue))
        {
            if (curVal is null)
            {
                total = null;
                break;
            }

            total += curVal.Value;
        }

        PositionsValue = total;
        TotalEquity    = total is null ? null : total.Value + CashQuote;
    }

    private async void OnAthUpdated(string symbol, decimal ath)
    {
        try
        {
            symbol = (symbol ?? "").Trim().ToUpperInvariant();
            if (symbol.Length == 0)  return;
            if (ath           <= 0m) return;

            if (_lastAthBySymbol.TryGetValue(symbol, out var prev) && Math.Abs(prev - ath) < 0.00000001m)
                return;

            _lastAthBySymbol[symbol] = ath;

            await _ui.InvokeAsync(() =>
            {
                var row = ScenarioRows.FirstOrDefault(r =>
                symbol.Equals((r.Symbol ?? "").Trim().ToUpperInvariant(), StringComparison.OrdinalIgnoreCase));

                if (row is null) return;

                row.Ath = ath;
            }, DispatcherPriority.Background);
        }
        catch { /* never throw */ }
    }

    // ======= charts =======
    private void BuildCharts(PortfolioSnapshot snap)
    {
        var useMarket = snap.Positions.All(p => p.MarketValueQuote is not null);

        var spotItems = snap.Positions
            .Select(p => new
            {
                Symbol = (p.Symbol ?? "").Trim().ToUpperInvariant(),
                Value  = useMarket ? (double)(p.MarketValueQuote ?? 0m) : (double)p.CostBasisQuote
            });

        var futureItems = snap.FuturesPositions
            .Select(p => new
            {
                Symbol = (p.Symbol ?? "").Trim().ToUpperInvariant(),
                Value  = (double)(p.Margin + (p.UnrealizedPnl ?? 0m))
            });

        var items = spotItems.Concat(futureItems)
            .Where(x => x.Value > 0d && !string.IsNullOrWhiteSpace(x.Symbol))
            .GroupBy(x => x.Symbol)
            .Select(g => new
            {
                Symbol = g.Key,
                Value  = g.Sum(x => x.Value)
            })
            .OrderByDescending(x => x.Value)
            .ToList();

        _allocTotal = items.Sum(x => x.Value);

        var active = new HashSet<string>(items.Select(x => x.Symbol), StringComparer.OrdinalIgnoreCase);

        foreach (var it in items)
        {
            var sym = it.Symbol;

            if (!_allocBySymbol.TryGetValue(sym, out var entry))
            {
                entry = CreateAllocSeries(sym, it.Value);
                _allocBySymbol[sym] = entry;
            }

            entry.Values[0] = it.Value;
        }

        foreach (var sym in _allocBySymbol.Keys.Where(s => !active.Contains(s)).ToList())
            _allocBySymbol.Remove(sym);

        AllocationSeries.Clear();
        foreach (var it in items)
            AllocationSeries.Add(_allocBySymbol[it.Symbol].Series);

        var realized = RealizedPnl;
        var unrealForChart = snap.Positions.Sum(p => p.UnrealizedPnlQuote ?? 0m)
                           + snap.FuturesPositions.Sum(p => p.UnrealizedPnl ?? 0m);

        UpdatePnlBar(realized, unrealForChart);

        _unrealizedRowSeries.Fill = new SolidColorPaint(ChartColors.PnlColor(unrealForChart));
        _realizedRowSeries.Fill   = new SolidColorPaint(ChartColors.PnlColor(realized));
    }

    private (PieSeries<double> Series, ObservableCollection<double> Values) CreateAllocSeries(string symbol, double initialValue)
    {
        var values = new ObservableCollection<double> { initialValue };
        var c      = ChartColors.StableColorForSymbol(symbol);

        var series = new PieSeries<double>
        {
            Name         = symbol,
            Values       = values,
            Fill         = new SolidColorPaint(c),
            Stroke       = new SolidColorPaint(new SKColor(c.Red, c.Green, c.Blue, 180), strokeWidth: 1),
            HoverPushout = 6,
            ToolTipLabelFormatter = p =>
            {
                var v   = p.Coordinate.PrimaryValue;
                var pct = _allocTotal > 0 ? (v / _allocTotal) * 100d : 0d;
                return $"${v.ToString("N2", UsCulture)} ({pct.ToString("N1", UsCulture)}%)";
            }
        };

        return (series, values);
    }

    private void UpdatePnlBar(decimal realized, decimal unrealized)
    {
        _realizedRowValues[0]   = (double)realized;
        _unrealizedRowValues[0] = (double)unrealized;

        _realizedRowSeries.IsVisible   = realized   != 0m;
        _unrealizedRowSeries.IsVisible = unrealized != 0m;

        var pos = (realized > 0m ? realized : 0m) + (unrealized > 0m ? unrealized : 0m);
        var neg = (realized < 0m ? realized : 0m) + (unrealized < 0m ? unrealized : 0m);

        var min = Math.Min(0m, neg);
        var max = Math.Max(0m, pos);

        if (min == max) { min -= 1m; max += 1m; }

        var span = max - min;
        var pad  = span * 0.15m;

        _pnlValueAxis.MinLimit = (double)(min - pad);
        _pnlValueAxis.MaxLimit = (double)(max + pad);
    }
}

internal static class CashCalculator
{
    // Simple USDT Cash: Buy/Sell + Deposit/Withdraw by quote symbol + Fee
    public static decimal ComputeQuoteCash(IReadOnlyList<TradeFill> fills, string quote = "USDT")
    {
        quote = quote.Trim().ToUpperInvariant();
        decimal cash = 0m;

        foreach (var f in fills.OrderBy(x => x.TimeUtc))
        {
            var sym = f.Symbol.Trim().ToUpperInvariant();

            switch (f.Type)
            {
                case TxType.Buy:
                    cash -= (f.Quantity * f.Price) + f.FeeQuote;
                    break;

                case TxType.Sell:
                    cash += (f.Quantity * f.Price) - f.FeeQuote;
                    break;

                case TxType.Deposit:
                    if (sym == quote) cash += f.Quantity - f.FeeQuote;
                    break;

                case TxType.Withdraw:
                    if (sym == quote) cash -= f.Quantity + f.FeeQuote;
                    break;

                case TxType.Fee:
                case TxType.OpenLong:
                case TxType.OpenShort:
                    cash -= f.FeeQuote;
                    break;
            }
        }

        return cash;
    }
}