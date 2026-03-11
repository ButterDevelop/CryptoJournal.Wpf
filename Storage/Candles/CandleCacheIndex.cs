namespace CryptoJournal.Wpf.Storage.Candles;

public sealed class CandleCacheIndex
{
    public string          Exchange         { get; set; } = "";
    public string          Symbol           { get; set; } = "";
    public string          Interval         { get; set; } = "";

    public long            Count            { get; set; }
    public int             Version          { get; set; } = 1;

    public DateTimeOffset? FirstOpenTimeUtc { get; set; }
    public DateTimeOffset? LastOpenTimeUtc  { get; set; }

    public decimal?        AthHigh          { get; set; }
    public DateTimeOffset? AthHighTimeUtc   { get; set; }
}