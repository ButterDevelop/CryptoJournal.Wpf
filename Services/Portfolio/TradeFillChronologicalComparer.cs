using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Portfolio;

internal sealed class TradeFillChronologicalComparer : IComparer<TradeFill>
{
    public static readonly TradeFillChronologicalComparer Instance = new();

    public int Compare(TradeFill? x, TradeFill? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;

        var timeComparison = x.TimeUtc.CompareTo(y.TimeUtc);
        return timeComparison != 0
            ? timeComparison
            : x.Id.CompareTo(y.Id);
    }
}
