using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    // Converter parameters enable normal, inverted, or hidden visibility evaluation options
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;

        var p = (parameter as string)?.Trim().ToLowerInvariant();

        if (p == "invert")
            flag = !flag;

        if (flag)
            return Visibility.Visible;

        return p == "hidden" ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Visibility v) return false;

        var flag = v == Visibility.Visible;

        var p = (parameter as string)?.Trim().ToLowerInvariant();
        if (p == "invert")
            flag = !flag;

        return flag;
    }
}