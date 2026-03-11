using CommunityToolkit.Mvvm.ComponentModel;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using System.Windows.Media;

namespace CryptoJournal.Wpf.ViewModels;

public partial class FuturesPositionRowVm : ObservableObject
{
    private FuturesPosition _pos;

    public FuturesPositionRowVm(FuturesPosition pos) => _pos = pos;

    public string       Symbol           => _pos.Symbol;
    public PositionSide Side             => _pos.Side;
    public string       SideLabel        => _pos.Side == PositionSide.Long ? "LONG" : "SHORT";
    public decimal      Quantity         => _pos.Quantity;
    public decimal      AvgEntryPrice    => _pos.AvgEntryPrice;
    public decimal      Leverage         => _pos.Leverage;
    public decimal      Margin           => _pos.Margin;
    public decimal?     LiquidationPrice => _pos.LiquidationPrice;
    public decimal?     TakeProfit       => _pos.TakeProfit;
    public decimal?     StopLoss         => _pos.StopLoss;
    public decimal?     MarkPrice        => _pos.MarkPrice;
    public decimal?     UnrealizedPnl    => _pos.UnrealizedPnl;
    public decimal?     UnrealizedPnlPct => _pos.UnrealizedPnlPct;
    public decimal?     RoePct           => _pos.RoePct;

    [ObservableProperty] private ImageSource? symbolIcon;

    /// <summary>Composite scenario store key used for this position's TP plan.</summary>
    public string ScenarioKey => $"{Symbol}:{SideLabel}";

    public void ApplyMarkPrice(decimal mark)
    {
        // Recompute live fields
        decimal pnlPerUnit = Side == PositionSide.Long
            ? mark - AvgEntryPrice
            : AvgEntryPrice - mark;

        decimal upnl     = pnlPerUnit * Quantity * Leverage;
        decimal cost     = Quantity   * AvgEntryPrice;
        decimal? upnlPct = cost   > 0m ? upnl / cost   : null;
        decimal? roe     = Margin > 0m ? upnl / Margin : null;

        _pos = _pos with
        {
            MarkPrice        = mark,
            UnrealizedPnl    = upnl,
            UnrealizedPnlPct = upnlPct,
            RoePct           = roe
        };

        OnPropertyChanged(nameof(MarkPrice));
        OnPropertyChanged(nameof(UnrealizedPnl));
        OnPropertyChanged(nameof(UnrealizedPnlPct));
        OnPropertyChanged(nameof(RoePct));
    }
}
