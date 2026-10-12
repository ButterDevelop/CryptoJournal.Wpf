using System.Globalization;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

public sealed class ResponsiveFontConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var w = value is double d ? d : 1200d;

        if (w < 900)  w = 900;
        if (w > 1800) w = 1800;

        var t = (w - 900d) / 900d;

        var kind = (parameter as string ?? "Body").Trim();

        return kind switch
        {
            "Title"   => Lerp(20, 28, t),
            "Section" => Lerp(14, 18, t),
            _         => Lerp(13, 16, t),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
