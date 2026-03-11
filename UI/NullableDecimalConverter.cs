using System.Globalization;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

/// <summary>
/// Same as FlexibleDecimalConverter but handles nullable decimal:
/// - Convert: null -> "", decimal -> formatted string
/// - ConvertBack: "" -> null, valid number -> decimal?
/// </summary>
public sealed class NullableDecimalConverter : IValueConverter
{
    private const int DefaultMaxDecimals = 28;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "";

        decimal d;
        if      (value is decimal dec) d = dec;
        else if (value is double dbl)  d = (decimal)dbl;
        else return value.ToString();

        var max = TryGetMax(parameter, DefaultMaxDecimals);
        var fmt = max == 0 ? "0" : "0." + new string('#', max);
        return d.ToString(fmt, CultureInfo.InvariantCulture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = ((value as string) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s)) return (decimal?)null;

        s = s.Replace(" ", "").Replace("\u00A0", "").Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? (decimal?)d
            : Binding.DoNothing;
    }

    private static int TryGetMax(object? p, int fallback)
    {
        if (p is null)                                   return fallback;
        if (p is int i)                                  return i;
        if (p is string s && int.TryParse(s, out var n)) return n;
        return fallback;
    }
}
