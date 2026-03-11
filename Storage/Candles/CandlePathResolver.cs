using CryptoJournal.Wpf.Storage.Common;
using System.Globalization;
using System.IO;

namespace CryptoJournal.Wpf.Storage.Candles;

public static class CandlePathResolver
{
    public static string GetSeriesDir(string exchange, string symbol, string interval)
    {
        var dir = Path.Combine(
            AppDataPaths.RootDir,
            "Candles",
            exchange.Trim().ToLowerInvariant(),
            symbol.Trim().ToUpperInvariant(),
            interval.Trim().ToLowerInvariant());

        return AppDataPaths.EnsureDir(dir);
    }

    public static string GetIndexPath(string exchange, string symbol, string interval)
        => Path.Combine(GetSeriesDir(exchange, symbol, interval), "index.json");

    public static string GetChunkFilePath(string exchange, string symbol, string interval, DateTimeOffset openTimeUtc)
    {
        // Files are stored as monthly chunks in the format yyyy-MM.jsonl.gz
        var file = openTimeUtc.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".jsonl.gz";
        return Path.Combine(GetSeriesDir(exchange, symbol, interval), file);
    }
}