using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Services.Validation;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Validation;

[TestFixture]
internal sealed class TradePrecheckTests
{
    [Test]
    public void Validate_BaseAssetDepositWithUnaffordableQuoteFee_Fails()
    {
        var precheck = new TradePrecheck();

        var result = precheck.Validate(
            existingFills: [],
            timeUtc: DateTime.UtcNow,
            type: TxType.Deposit,
            symbol: "STRK",
            qty: 100m,
            price: 0.04m,
            feeQuote: 1m,
            leverage: 1m,
            quoteSymbol: "USDT");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Ok, Is.False);
            Assert.That(result.Message, Does.Contain("deposit fee"));
        }
    }

    [Test]
    public void Validate_BaseAssetDeposit_DoesNotSubtractQuoteFeeFromBaseQuantity()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fills = new[]
        {
            new TradeFillBuilder()
                .At(start)
                .As(TxType.Deposit)
                .For("USDT")
                .WithQuantity(10m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(1))
                .As(TxType.Deposit)
                .For("STRK")
                .WithQuantity(100m)
                .AtPrice(0.04m)
                .WithFee(1m)
                .Build()
        };
        var precheck = new TradePrecheck();

        var result = precheck.Validate(
            fills,
            start.AddMinutes(2).UtcDateTime,
            TxType.Sell,
            "STRK",
            100m,
            0.12m,
            0m,
            1m,
            "USDT");

        Assert.That(result.Ok, Is.True);
    }

    [Test]
    public void Validate_AfterProfitableFuturesClose_UsesRealizedPnlAndReleasedEntryMargin()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fills = new[]
        {
            new TradeFillBuilder()
                .At(start)
                .As(TxType.Deposit)
                .For("USDT")
                .WithQuantity(100m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(1))
                .As(TxType.OpenLong)
                .For("BTC")
                .WithQuantity(10m)
                .AtPrice(100m)
                .WithLeverage(10m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(2))
                .As(TxType.CloseLong)
                .For("BTC")
                .WithQuantity(10m)
                .AtPrice(110m)
                .WithLeverage(10m)
                .Build()
        };

        var result = new TradePrecheck().Validate(
            fills,
            start.AddMinutes(3).UtcDateTime,
            TxType.Buy,
            "ETH",
            1m,
            150m,
            0m,
            1m,
            "USDT");

        Assert.That(result.Ok, Is.True);
    }
}
