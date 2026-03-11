namespace CryptoJournal.Wpf.Domain.Enums;

public enum TxType
{
    Buy,
    Sell,
    Deposit,
    Withdraw,
    Fee,

    // Margin and Futures trading transaction types
    OpenLong,
    CloseLong,
    OpenShort,
    CloseShort,
}