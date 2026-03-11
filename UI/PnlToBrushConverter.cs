using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CryptoJournal.Wpf.UI;

public sealed class PnlToBrushConverter : IValueConverter
{
    public Brush ZeroBrush { get; set; } = Brushes.White;
    public Brush PosBrush  { get; set; } = Brushes.LimeGreen;
    public Brush NegBrush  { get; set; } = Brushes.IndianRed;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return ZeroBrush;

        if (value is decimal d)
        {
            if (d > 0m) return PosBrush;
            if (d < 0m) return NegBrush;
            return ZeroBrush;
        }

        return ZeroBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}