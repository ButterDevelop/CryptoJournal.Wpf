using CryptoJournal.Wpf.Domain.Enums;
using System.Globalization;
using System.Windows.Data;

namespace CryptoJournal.Wpf.UI;

public sealed class TxTypeDisplayConverter : IValueConverter
{
    public static string Format(TxType type)
    {
        return type switch
        {
            TxType.Buy        => "Buy",
            TxType.Sell       => "Sell",
            TxType.Deposit    => "Deposit",
            TxType.Withdraw   => "Withdraw",
            TxType.OpenLong   => "Open Long",
            TxType.CloseLong  => "Close Long",
            TxType.OpenShort  => "Open Short",
            TxType.CloseShort => "Close Short",
            _                 => type.ToString()
        };
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TxType type)
            return Format(type);
        
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
