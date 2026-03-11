using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace CryptoJournal.Wpf.ViewModels;

public partial class AthRowVm : ObservableObject
{
    public AthRowVm(string symbol, decimal qty, decimal? mark, decimal? ath)
    {
        Symbol = symbol;
        Qty    = qty;
        Mark   = mark;
        Ath    = ath;
    }

    public string  Symbol { get; }
    public decimal Qty    { get; }

    // Source values (updated asynchronously)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentValue))]
    [NotifyPropertyChangedFor(nameof(UpsideToAth))]
    [NotifyPropertyChangedFor(nameof(ToAthPct))]
    private decimal? mark;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueAtAth))]
    [NotifyPropertyChangedFor(nameof(UpsideToAth))]
    [NotifyPropertyChangedFor(nameof(ToAthPct))]
    private decimal? ath;

    [ObservableProperty] private ImageSource? symbolIcon;

    // Derived values (always consistent)
    public decimal? CurrentValue => Mark is null ? null : Qty * Mark.Value;
    public decimal? ValueAtAth   => Ath  is null ? null : Qty * Ath.Value;

    public decimal? UpsideToAth =>
        (Ath is null || Mark is null) ? null : Qty * (Ath.Value - Mark.Value);

    public decimal? ToAthPct =>
        (Ath is null || Mark is null || Mark.Value <= 0m) ? null : (Ath.Value / Mark.Value) - 1m;
}