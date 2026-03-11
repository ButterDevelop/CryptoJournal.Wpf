using System.Globalization;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

public sealed class ResponsiveFontConverter : IValueConverter
{
    // ConverterParameter: "Title" | "Section" | "Body"
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Window.ActualWidth
        var w = value is double d ? d : 1200d;

        // clamp so fonts are stable
        if (w < 900)  w = 900;
        if (w > 1800) w = 1800;

        // scale 0..1 across [900..1800]
        var t = (w - 900d) / 900d;

        var kind = (parameter as string ?? "Body").Trim();

        return kind switch
        {
            "Title"   => Lerp(20, 28, t), // AppName
            "Section" => Lerp(14, 18, t), // headings
            _         => Lerp(13, 16, t), // body
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}