using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Portfolio;

[TestFixture]
internal sealed class TransactionPnlCalculatorTests
{
    private TransactionPnlCalculator _calculator = null!;

    [SetUp]
    public void SetUp()
    {
        var environment = new TestEnvironmentService();

        _calculator = new TransactionPnlCalculator(
            new FifoCostBasisEngine(environment),
            new FuturesEngine());
    }

    [Test]
    public void Calculate_DepositFollowedBySell_ReturnsSpotPnl()
    {
        var start   = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var deposit = new TradeFillBuilder()
            .At(start)
            .As(TxType.Deposit)
            .For("SOL")
            .WithQuantity(10m)
            .AtPrice(2m)
            .Build();
        var sell = new TradeFillBuilder()
            .At(start.AddMinutes(1))
            .As(TxType.Sell)
            .For("SOL")
            .WithQuantity(4m)
            .AtPrice(3m)
            .Build();

        var result = _calculator.Calculate([deposit, sell]).ByTransactionId;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[sell.Id].RealizedPnlQuote, Is.EqualTo(4m));
            Assert.That(result[sell.Id].RealizedPnlPct, Is.EqualTo(0.5m));
        }
    }

    [Test]
    public void Calculate_WithdrawFollowedBySell_UsesRemainingLots()
    {
        var start    = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var buy      = new TradeFillBuilder()
            .At(start)
            .As(TxType.Buy)
            .For("ETH")
            .WithQuantity(10m)
            .AtPrice(1m)
            .Build();
        var withdraw = new TradeFillBuilder()
            .At(start.AddMinutes(1))
            .As(TxType.Withdraw)
            .For("ETH")
            .WithQuantity(4m)
            .Build();
        var sell = new TradeFillBuilder()
            .At(start.AddMinutes(2))
            .As(TxType.Sell)
            .For("ETH")
            .WithQuantity(6m)
            .AtPrice(2m)
            .Build();

        var result = _calculator.Calculate([buy, withdraw, sell]).ByTransactionId;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[sell.Id].RealizedPnlQuote, Is.EqualTo(6m));
            Assert.That(result[sell.Id].RealizedPnlPct, Is.EqualTo(1m));
        }
    }

    [Test]
    public void Calculate_LeveragedFutureClose_ReturnsRoeWithoutMultiplyingPnl()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var open  = new TradeFillBuilder()
            .At(start)
            .As(TxType.OpenLong)
            .For("BTC")
            .WithQuantity(10m)
            .AtPrice(100m)
            .WithLeverage(5m)
            .Build();
        var close = new TradeFillBuilder()
            .At(start.AddMinutes(1))
            .As(TxType.CloseLong)
            .For("BTC")
            .WithQuantity(4m)
            .AtPrice(110m)
            .WithFee(4m)
            .WithLeverage(5m)
            .Build();

        var result = _calculator.Calculate([open, close]).ByTransactionId;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result[close.Id].RealizedPnlQuote, Is.EqualTo(36m));
            Assert.That(result[close.Id].RealizedPnlPct, Is.EqualTo(0.45m));
        }
    }
}
