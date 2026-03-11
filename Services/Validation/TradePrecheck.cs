using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using System.Globalization;

namespace CryptoJournal.Wpf.Services.Validation;

public sealed class TradePrecheck : ITradePrecheck
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public PrecheckResult Validate(
        IReadOnlyList<TradeFill> existingFills,
        DateTime                 timeUtc,
        TxType                   type,
        string                   symbol,
        decimal                  qty,
        decimal                  price,
        decimal                  feeQuote,
        decimal                  leverage,
        string                   quoteSymbol)
    {
        symbol = (symbol ?? "").Trim().ToUpperInvariant();
        quoteSymbol = (quoteSymbol ?? "").Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(symbol))
            return new PrecheckResult(false, "Symbol is required.");

        if (qty <= 0m && type is not TxType.Fee)
            return new PrecheckResult(false, "Quantity must be > 0.");

        if ((type is TxType.Buy or TxType.Sell) && price <= 0m && !string.Equals(symbol, quoteSymbol, StringComparison.OrdinalIgnoreCase))
            return new PrecheckResult(false, "Price must be > 0 for Buy/Sell.");

        if (feeQuote < 0m)
            return new PrecheckResult(false, "Fee must be >= 0.");

        // Calculate global balances chronologically prior to this transaction
        var bal = BuildBalances(existingFills, timeUtc, quoteSymbol);

        bal.TryGetValue(symbol,      out var symBal);
        bal.TryGetValue(quoteSymbol, out var quoteBal);

        switch (type)
        {
            case TxType.Sell:
            {
                if (string.Equals(symbol, quoteSymbol, StringComparison.OrdinalIgnoreCase))
                {
                    if (qty + feeQuote > quoteBal)
                        return Fail($"Not enough {quoteSymbol}",
                                    $"You have: {Amt(quoteBal, quoteSymbol, maxDec: 2)}",
                                    $"Need: {Amt(qty + feeQuote, quoteSymbol, maxDec: 2)} (incl. fee)");
                    return new PrecheckResult(true, "");
                }

                if (qty > symBal)
                    return Fail($"Not enough {symbol}",
                                $"You have: {Amt(symBal, symbol)}",
                                $"Trying to sell: {Amt(qty, symbol)}",
                                $"Shortage: {Amt(qty - symBal, symbol)}");

                // Transaction fees are deducted from quoted sale proceeds; validate quote balance post-sale
                var proceeds = qty * price; // Gross proceeds in quote currency
                var quoteAfter = quoteBal + (proceeds - feeQuote);

                if (quoteAfter < 0m)
                    return Fail($"Not enough {quoteSymbol} to cover fee from proceeds",
                                $"You have: {Amt(quoteBal, quoteSymbol, maxDec: 2)}",
                                $"Proceeds: {Amt(proceeds, quoteSymbol, maxDec: 2)}",
                                $"Fee: {Amt(feeQuote, quoteSymbol, maxDec: 2)}",
                                $"Quote after: {Amt(quoteAfter, quoteSymbol, maxDec: 2)}");

                return new PrecheckResult(true, "");
            }

            case TxType.Withdraw:
            {
                // Withdrawal operations still require fees deducted from the quote balance
                if (string.Equals(symbol, quoteSymbol, StringComparison.OrdinalIgnoreCase))
                {
                    if (qty + feeQuote > quoteBal)
                        return Fail($"Not enough {quoteSymbol}",
                                    $"You have: {Amt(quoteBal, quoteSymbol, maxDec: 2)}",
                                    $"Need: {Amt(qty + feeQuote, quoteSymbol, maxDec: 2)} (incl. fee)");
                    return new PrecheckResult(true, "");
                }

                if (qty > symBal)
                    return Fail($"Not enough {symbol}",
                                $"You have: {Amt(symBal, symbol)}",
                                $"Trying to withdraw: {Amt(qty, symbol)}",
                                $"Shortage: {Amt(qty - symBal, symbol)}");

                if (feeQuote > 0m && feeQuote > quoteBal)
                    return Fail($"Not enough {quoteSymbol} to pay fee",
                                $"You have: {Amt(quoteBal, quoteSymbol, maxDec: 2)}",
                                $"Fee: {Amt(feeQuote, quoteSymbol, maxDec: 2)}");

                return new PrecheckResult(true, "");
            }

            case TxType.Buy:
            {
                // Buying consumes quote liquidity: (Quantity * Price) + FeeQuote
                var needQuote = (qty * price) + feeQuote;
                if (needQuote > quoteBal)
                    return Fail($"Not enough {quoteSymbol}",
                                $"You have: {Amt(quoteBal, quoteSymbol)}",
                                $"Need: {Amt(needQuote, quoteSymbol)} (incl. fee)",
                                $"Breakdown: {Amt(qty, symbol)} × {PricePer(price, quoteSymbol, symbol)} + {Amt(feeQuote, quoteSymbol)}");

                return new PrecheckResult(true, "");
            }

            case TxType.OpenLong:
            case TxType.OpenShort:
            {
                var lev = Math.Max(1m, leverage);
                var margin = (qty * price) / lev;
                var needQuote = margin + feeQuote;

                if (needQuote > quoteBal)
                {
                    return Fail($"Not enough {quoteSymbol} margin for futures",
                                $"You have free: {Amt(quoteBal, quoteSymbol)}",
                                $"Need (Margin + Fee): {Amt(needQuote, quoteSymbol)}",
                                $"Breakdown: Margin {Amt(margin, quoteSymbol)} + Fee {Amt(feeQuote, quoteSymbol)}");
                }

                return new PrecheckResult(true, "");
            }

            case TxType.Fee:
            {
                // Fees are strictly deducted from the quote currency balance
                if (feeQuote <= 0m)
                    return new PrecheckResult(false, "Fee must be > 0 for Fee transaction.");

                if (feeQuote > quoteBal)
                    return Fail($"Not enough {quoteSymbol} to pay fee",
                        $"You have: {Amt(quoteBal, quoteSymbol)}",
                        $"Fee: {Amt(feeQuote, quoteSymbol)}");

                return new PrecheckResult(true, "");
            }

            // Deposits are unbounded as they originate externally
            case TxType.Deposit:
                return new PrecheckResult(true, "");

            default:
                return new PrecheckResult(true, "");
        }
    }

    private static Dictionary<string, decimal> BuildBalances(IReadOnlyList<TradeFill> fills, DateTime untilUtc, string quote)
    {
        var bal = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        decimal Get(string s) => bal.TryGetValue(s, out var v) ? v : 0m;
        void Add(string s, decimal d) => bal[s] = Get(s) + d;

        foreach (var f in fills
                     .Where(f => f.TimeUtc.UtcDateTime <= untilUtc)
                     .OrderBy(f => f.TimeUtc))
        {
            var sym = (f.Symbol ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(sym)) continue;

            switch (f.Type)
            {
                case TxType.Buy:
                    // Increment base asset, decrement quote asset (value + fee)
                    Add(sym, f.Quantity);
                    Add(quote, -((f.Quantity * f.Price) + f.FeeQuote));
                    break;

                case TxType.Sell:
                    // Decrement base asset, increment quote asset (value - fee)
                    Add(sym, -f.Quantity);
                    Add(quote, (f.Quantity * f.Price) - f.FeeQuote);
                    break;

                case TxType.Deposit:
                    // Deposits can target either the quote or base currency
                    Add(sym, f.Quantity - f.FeeQuote);
                    break;

                case TxType.Withdraw:
                    Add(sym, -(f.Quantity));
                    Add(quote, -(f.FeeQuote)); // Deduct the transaction fee from the quote balance
                    break;

                case TxType.Fee:
                    Add(quote, -(f.FeeQuote));
                    break;

                case TxType.OpenLong:
                case TxType.OpenShort:
                {
                    var margin = (f.Quantity * f.Price) / Math.Max(1m, f.Leverage);
                    Add(quote, -margin - f.FeeQuote); // Deduct required margin and transaction fee
                    break;
                }

                case TxType.CloseLong:
                case TxType.CloseShort:
                {
                    // Precheck lacks exact PnL without a complete FIFO simulation pass.
                    // Release the approximate locked margin and deduct closing fees.
                    // PnL is omitted here, resulting in a slightly conservative free balance estimate.
                    var marginReturn = (f.Quantity * f.Price) / Math.Max(1m, f.Leverage);
                    Add(quote, marginReturn - f.FeeQuote);
                    break;
                }
            }
        }

        return bal;
    }

    private static string Fmt(decimal v, int maxDec = 8)
    {
        // "#,0.#########" => thousands grouping + up to maxDec digits, without trailing zeros
        var fmt = maxDec <= 0 ? "#,0" : "#,0." + new string('#', maxDec);
        return v.ToString(fmt, Culture);
    }

    private static string Amt(decimal v, string sym, int maxDec = 8)
        => $"{Fmt(v, maxDec)} {sym}";

    private static string PricePer(decimal price, string quote, string baseSym, int maxDec = 8)
        => $"{Fmt(price, maxDec)} {quote}/{baseSym}";

    private static PrecheckResult Fail(string header, params string[] lines)
        => new(false, header + "\n\n" + string.Join("\n", lines));
}