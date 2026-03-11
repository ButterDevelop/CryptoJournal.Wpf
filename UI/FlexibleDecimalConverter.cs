using System.Globalization;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

public sealed class FlexibleDecimalConverter : IValueConverter
{
    // Default behavior maintains absolute precision up to the maximum allowable decimal places.
    private const int DefaultMaxDecimals = 28;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return "";

        decimal d;

        if      (value is decimal dec) d = dec;
        else if (value is double dbl)  d = (decimal)dbl;
        else if (value is float flt)   d = (decimal)flt;
        else return value.ToString();

        var max = TryGetMaxDecimals(parameter, DefaultMaxDecimals);
        if (max < 0)  max = DefaultMaxDecimals;
        if (max > 28) max = 28;

        var fmt = max == 0 ? "0" : "0." + new string('#', max);

        // Enforce decimal point and suppress trailing zeros via the '#' format specifier.
        return d.ToString(fmt, CultureInfo.InvariantCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
            return 0m;

        // Accommodate locale-specific input (e.g., "1,23") and strip grouping characters.
        s = s.Replace(" ", "").Replace("\u00A0", "").Replace(',', '.');

        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : Binding.DoNothing; // Preserve the existing value to maintain typing continuity.
    }

    private static int TryGetMaxDecimals(object parameter, int fallback)
    {
        if (parameter is null)                                       return fallback;
        if (parameter is int i)                                      return i;
        if (parameter is string str && int.TryParse(str, out var n)) return n;
        return fallback;
    }
}