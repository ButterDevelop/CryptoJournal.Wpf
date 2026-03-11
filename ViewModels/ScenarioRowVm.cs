using CommunityToolkit.Mvvm.ComponentModel;
using CryptoJournal.Wpf.Domain.Enums;
using System.Windows.Media;

namespace CryptoJournal.Wpf.ViewModels;

public partial class ScenarioRowVm : ObservableObject
{
    public ScenarioRowVm(string symbol, decimal qty)
    {
        Symbol = symbol;
        Qty    = qty;
    }

    public string  Symbol { get; }
    public decimal Qty    { get; }

    // Futures configuration properties
    public bool         IsFutures  { get; init; }
    public PositionSide Side       { get; init; }
    public string       SideLabel  => IsFutures ? (Side == PositionSide.Long ? "LONG" : "SHORT") : string.Empty;
    public decimal      EntryPrice { get; init; }
    public decimal      Leverage   { get; init; }
    public decimal      Margin     { get; init; }

    // Live market price (continually updated via polling)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentValue))]
    [NotifyPropertyChangedFor(nameof(EquityContributionAtScenario))]
    private decimal? mark;

    // All-Time High price in quote currency (managed by AthService)
    [ObservableProperty]
    private decimal? ath;

    // Scenario-derived values (refreshed on scenario or table rebuild)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EquityContributionAtScenario))]
    [NotifyPropertyChangedFor(nameof(RemainingQty))]
    [NotifyPropertyChangedFor(nameof(RemainingValueAtMark))]
    private decimal plannedQty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemainingQty))]
    [NotifyPropertyChangedFor(nameof(RemainingValueAtMark))]
    [NotifyPropertyChangedFor(nameof(EquityContributionAtScenario))]
    private decimal planValue; // Projected value sum (legQty * TargetPrice)

    [ObservableProperty] private ImageSource? symbolIcon;

    // Calculated properties
    public decimal? CurrentValue
    {
        get
        {
            if (Mark is null) return null;
            if (!IsFutures) return Qty * Mark.Value;

            var pnlPerUnit = Side == PositionSide.Long ? Mark.Value - EntryPrice : EntryPrice - Mark.Value;
            var upnl = pnlPerUnit * Qty;
            return Margin + upnl;
        }
    }

    public decimal  RemainingQty => Math.Max(0m, Qty - PlannedQty);

    public decimal? RemainingValueAtMark
    {
        get 
        {
            if (Mark is null) return null;
            if (!IsFutures) return RemainingQty * Mark.Value;

            var currentTotal = CurrentValue!.Value;
            return Qty > 0m ? currentTotal * (RemainingQty / Qty) : 0m;
        }
    }

    public decimal? EquityContributionAtScenario
    {
        get
        {
            if (Mark is null) return null;
            return PlanValue + RemainingValueAtMark;
        }
    }
}