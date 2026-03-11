using CommunityToolkit.Mvvm.ComponentModel;
using CryptoJournal.Wpf.Domain.Models;
using System.Windows.Media;

namespace CryptoJournal.Wpf.ViewModels;

public partial class PositionRowVm : ObservableObject
{
    public PositionRowVm(PositionSnapshot snapshot)
    {
        Snapshot = snapshot;

        Symbol         = (snapshot.Symbol ?? "").Trim().ToUpperInvariant();
        Quantity       = snapshot.Quantity;
        CostBasisQuote = snapshot.CostBasisQuote;

        // Initial values (may be null / stale, but good as a starting point)
        MarkPriceQuote = snapshot.MarkPriceQuote;
    }

    public PositionSnapshot Snapshot { get; }

    public string  Symbol         { get; } // stable key for matching updates
    public decimal Quantity       { get; } // position qty (base)
    public decimal CostBasisQuote { get; } // in quote currency (e.g., USDT)

    // Source value that changes over time
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarketValueQuote))]
    [NotifyPropertyChangedFor(nameof(UnrealizedPnlQuote))]
    [NotifyPropertyChangedFor(nameof(UnrealizedPnlPct))]
    private decimal? markPriceQuote;

    [ObservableProperty]
    private ImageSource? symbolIcon;

    // Derived values (always consistent)
    public decimal? MarketValueQuote =>
        MarkPriceQuote is null ? null : Quantity * MarkPriceQuote.Value;

    public decimal? UnrealizedPnlQuote =>
        MarketValueQuote is null ? null : MarketValueQuote.Value - CostBasisQuote;

    public decimal? UnrealizedPnlPct =>
        (MarketValueQuote is null || CostBasisQuote <= 0m)
            ? null
            : (MarketValueQuote.Value / CostBasisQuote) - 1m;

    // Helper used by polling updates
    public void ApplyLastPrice(decimal price)
    {
        if (price <= 0m) return;

        // skip micro-updates if you want less UI churn
        if (MarkPriceQuote is not null && Math.Abs(MarkPriceQuote.Value - price) < 0.00000001m) return;

        MarkPriceQuote = price;
    }
}