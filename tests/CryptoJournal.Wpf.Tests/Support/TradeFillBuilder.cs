using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Tests.Support;

internal sealed class TradeFillBuilder
{
    private static readonly DateTimeOffset DefaultTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private Guid           _id         = Guid.NewGuid();
    private DateTimeOffset _timeUtc    = DefaultTime;
    private TxType         _type       = TxType.Buy;
    private string         _symbol     = "BTC";
    private decimal        _quantity   = 1m;
    private decimal        _price      = 1m;
    private decimal        _feeQuote;
    private decimal        _leverage   = 1m;

    public TradeFillBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public TradeFillBuilder At(DateTimeOffset timeUtc)
    {
        _timeUtc = timeUtc;
        return this;
    }

    public TradeFillBuilder As(TxType type)
    {
        _type = type;
        return this;
    }

    public TradeFillBuilder For(string symbol)
    {
        _symbol = symbol;
        return this;
    }

    public TradeFillBuilder WithQuantity(decimal quantity)
    {
        _quantity = quantity;
        return this;
    }

    public TradeFillBuilder AtPrice(decimal price)
    {
        _price = price;
        return this;
    }

    public TradeFillBuilder WithFee(decimal feeQuote)
    {
        _feeQuote = feeQuote;
        return this;
    }

    public TradeFillBuilder WithLeverage(decimal leverage)
    {
        _leverage = leverage;
        return this;
    }

    public TradeFill Build()
    {
        return new TradeFill(
            Id:          _id,
            TimeUtc:     _timeUtc,
            Type:        _type,
            Symbol:      _symbol,
            Quantity:    _quantity,
            Price:       _price,
            FeeQuote:    _feeQuote,
            Leverage:    _leverage);
    }
}
