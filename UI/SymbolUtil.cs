namespace CryptoJournal.Wpf.UI;

public static class SymbolUtil
{
    public static string NormalizeBase(string raw, string quote)
    {
        raw = (raw ?? "").Trim().ToUpperInvariant();

        // Permit delimiters "BTC/USDT", "BTC-USDT", and "BTC_USDT"
        raw = raw.Replace("/", "").Replace("-", "").Replace("_", "");

        // Extract the base asset if the user omits delimiters (e.g., "BTCUSDT" returns "BTC")
        if (raw.EndsWith(quote, StringComparison.OrdinalIgnoreCase) && raw.Length > quote.Length)
            raw = raw[..^quote.Length];

        return raw;
    }

    public static string ToPair(string baseSymbol, string quote)
    {
        baseSymbol = (baseSymbol ?? "").Trim().ToUpperInvariant();
        quote = (quote ?? "").Trim().ToUpperInvariant();
        return baseSymbol == quote ? quote : baseSymbol + quote;
    }

    public static bool IsQuote(string baseSymbol, string quote)
        => string.Equals(baseSymbol?.Trim(), quote, StringComparison.OrdinalIgnoreCase);
}