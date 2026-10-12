using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Portfolio;

[TestFixture]
internal sealed class FifoCostBasisEngineTests
{
    private FifoCostBasisEngine _engine = null!;

    [SetUp]
    public void SetUp()
    {
        _engine = new FifoCostBasisEngine(new TestEnvironmentService());
    }

    [Test]
    public void Build_PartialSell_PreservesRemainingQuantityAndFee()
    {
        var buy = new TradeFillBuilder()
            .WithId(Guid.Parse("00000000-0000-0000-0000-000000000001"))
            .As(TxType.Buy)
            .For("ETH")
            .WithQuantity(10m)
            .AtPrice(2m)
            .WithFee(1m)
            .Build();

        var sell = new TradeFillBuilder()
            .WithId(Guid.Parse("00000000-0000-0000-0000-000000000002"))
            .At(buy.TimeUtc.AddMinutes(1))
            .As(TxType.Sell)
            .For("ETH")
            .WithQuantity(4m)
            .AtPrice(3m)
            .WithFee(0.4m)
            .Build();

        var result = _engine.Build([buy, sell]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RealizedPnlQuote, Is.EqualTo(3.2m));
            Assert.That(result.LotsRemaining, Has.Count.EqualTo(1));
            Assert.That(result.LotsRemaining[0].QtyRemaining, Is.EqualTo(6m));
            Assert.That(result.LotsRemaining[0].FeeQuoteAllocated, Is.EqualTo(0.6m));
        }
    }

    [Test]
    public void Build_RepeatedPartialSells_ContinueConsumingOldestLot()
    {
        var firstBuyId  = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondBuyId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var start       = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var fills = new[]
        {
            new TradeFillBuilder()
                .WithId(firstBuyId)
                .At(start)
                .As(TxType.Buy)
                .For("STRK")
                .WithQuantity(10m)
                .AtPrice(1m)
                .Build(),
            new TradeFillBuilder()
                .WithId(secondBuyId)
                .At(start.AddMinutes(1))
                .As(TxType.Buy)
                .For("STRK")
                .WithQuantity(10m)
                .AtPrice(2m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(2))
                .As(TxType.Sell)
                .For("STRK")
                .WithQuantity(5m)
                .AtPrice(3m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(3))
                .As(TxType.Sell)
                .For("STRK")
                .WithQuantity(5m)
                .AtPrice(4m)
                .Build()
        };

        var result = _engine.Build(fills);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Has.Count.EqualTo(2));
            Assert.That(result.Matches.Select(match => match.SourceFillId),
                Is.All.EqualTo(firstBuyId));
            Assert.That(result.RealizedPnlQuote, Is.EqualTo(25m));
            Assert.That(result.LotsRemaining, Has.Count.EqualTo(1));
            Assert.That(result.LotsRemaining[0].SourceFillId, Is.EqualTo(secondBuyId));
            Assert.That(result.LotsRemaining[0].QtyRemaining, Is.EqualTo(10m));
            Assert.That(result.LotsRemaining[0].Price, Is.EqualTo(2m));
        }
    }

    [Test]
    public void Build_Withdraw_ConsumesOldestLotWithoutRealizingPnl()
    {
        var deposit = new TradeFillBuilder()
            .As(TxType.Deposit)
            .For("SOL")
            .WithQuantity(10m)
            .AtPrice(20m)
            .WithFee(1m)
            .Build();

        var withdraw = new TradeFillBuilder()
            .At(deposit.TimeUtc.AddMinutes(1))
            .As(TxType.Withdraw)
            .For("SOL")
            .WithQuantity(4m)
            .WithFee(0.2m)
            .Build();

        var result = _engine.Build([deposit, withdraw]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RealizedPnlQuote, Is.Zero);
            Assert.That(result.LotsRemaining, Has.Count.EqualTo(1));
            Assert.That(result.LotsRemaining[0].QtyRemaining, Is.EqualTo(6m));
            Assert.That(result.LotsRemaining[0].FeeQuoteAllocated, Is.EqualTo(0.6m));
        }
    }

    [Test]
    public void Build_Oversell_PreservesLotsAndReportsIssue()
    {
        var buy = new TradeFillBuilder()
            .As(TxType.Buy)
            .For("ETH")
            .WithQuantity(5m)
            .AtPrice(2m)
            .Build();
        var sell = new TradeFillBuilder()
            .At(buy.TimeUtc.AddMinutes(1))
            .As(TxType.Sell)
            .For("ETH")
            .WithQuantity(6m)
            .AtPrice(3m)
            .Build();

        var result = _engine.Build([buy, sell]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.RealizedPnlQuote, Is.Zero);
            Assert.That(result.LotsRemaining.Single().QtyRemaining, Is.EqualTo(5m));
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].TransactionId, Is.EqualTo(sell.Id));
            Assert.That(result.Issues[0].AvailableQuantity, Is.EqualTo(5m));
        }
    }

    [Test]
    public void Build_IdenticalTimestamps_UsesIdAsDeterministicTieBreaker()
    {
        var time = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var buy  = new TradeFillBuilder()
            .WithId(Guid.Parse("00000000-0000-0000-0000-000000000001"))
            .At(time)
            .As(TxType.Buy)
            .For("ETH")
            .WithQuantity(1m)
            .AtPrice(100m)
            .Build();
        var sell = new TradeFillBuilder()
            .WithId(Guid.Parse("00000000-0000-0000-0000-000000000002"))
            .At(time)
            .As(TxType.Sell)
            .For("ETH")
            .WithQuantity(1m)
            .AtPrice(110m)
            .Build();

        var result = _engine.Build([sell, buy]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Issues, Is.Empty);
            Assert.That(result.RealizedPnlQuote, Is.EqualTo(10m));
            Assert.That(result.LotsRemaining, Is.Empty);
        }
    }
}
