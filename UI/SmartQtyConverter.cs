using System.Globalization;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

/// <summary>
/// Truncates quantity strings if they are too long, unless expanded
/// Expects values: [0] quantity (decimal/string), [1] isExpanded (bool)
/// </summary>
public sealed class SmartQtyConverter : IMultiValueConverter
{
    private const int MaxLen = 8;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 1 || values[0] == null) return string.Empty;

        string qtyStr = values[0].ToString() ?? string.Empty;
        bool isExpanded = values.Length > 1 && values[1] is bool b && b;

        if (isExpanded || qtyStr.Length <= MaxLen) return qtyStr;

        int dotIndex = qtyStr.IndexOfAny(['.', ',']);
        if (dotIndex == -1)
        {
            // Case for very long integers (rare in crypto qty, but possible)
            return qtyStr.Length > MaxLen ? string.Concat(qtyStr.AsSpan(0, MaxLen), "...") : qtyStr;
        }

        // We have a decimal point.
        string intPart = qtyStr[..dotIndex];
        int intDigitsCount = intPart.TrimStart('-', '0').Length;
        if (intDigitsCount == 0 && intPart.Contains('0')) intDigitsCount = 1; // "0." counts as 1 digit budget

        int fractionalBudget = MaxLen - intDigitsCount;
        if (fractionalBudget < 0) fractionalBudget = 0;

        // Total length will be (dotIndex + 1 + fractionalBudget)
        int targetLength = dotIndex + 1 + fractionalBudget;

        if (qtyStr.Length > targetLength)
        {
            return qtyStr[..targetLength].TrimEnd('.', ',') + "...";
        }

        return qtyStr;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
