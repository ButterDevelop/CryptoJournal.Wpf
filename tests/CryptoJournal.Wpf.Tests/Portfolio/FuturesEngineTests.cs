using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Portfolio;

[TestFixture]
internal sealed class FuturesEngineTests
{
    [Test]
    public void Build_LeveragedPartialClose_LeverageAffectsMarginButNotAbsolutePnl()
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

        var result = new FuturesEngine().Build(
            [open, close],
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["BTC"] = 120m
            });

        var position = result.Positions.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RealizedPnlQuote, Is.EqualTo(36m));
            Assert.That(position.Quantity, Is.EqualTo(6m));
            Assert.That(position.Margin, Is.EqualTo(120m));
            Assert.That(position.UnrealizedPnl, Is.EqualTo(120m));
            Assert.That(position.RoePct, Is.EqualTo(1m));
        }
    }

    [Test]
    public void Build_Overclose_PreservesOpenPositionAndReportsIssue()
    {
        var open = new TradeFillBuilder()
            .As(TxType.OpenShort)
            .For("ETH")
            .WithQuantity(5m)
            .AtPrice(100m)
            .WithLeverage(2m)
            .Build();
        var close = new TradeFillBuilder()
            .At(open.TimeUtc.AddMinutes(1))
            .As(TxType.CloseShort)
            .For("ETH")
            .WithQuantity(6m)
            .AtPrice(90m)
            .WithLeverage(2m)
            .Build();

        var result = new FuturesEngine().Build([open, close]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Matches, Is.Empty);
            Assert.That(result.RealizedPnlQuote, Is.Zero);
            Assert.That(result.Positions.Single().Quantity, Is.EqualTo(5m));
            Assert.That(result.Issues, Has.Count.EqualTo(1));
            Assert.That(result.Issues[0].TransactionId, Is.EqualTo(close.Id));
        }
    }

    [Test]
    public void Build_LeveragedShortClose_DoesNotMultiplyAbsolutePnlByLeverage()
    {
        var open = new TradeFillBuilder()
            .As(TxType.OpenShort)
            .For("ETH")
            .WithQuantity(2m)
            .AtPrice(100m)
            .WithLeverage(10m)
            .Build();
        var close = new TradeFillBuilder()
            .At(open.TimeUtc.AddMinutes(1))
            .As(TxType.CloseShort)
            .For("ETH")
            .WithQuantity(1m)
            .AtPrice(90m)
            .WithFee(1m)
            .WithLeverage(10m)
            .Build();

        var result = new FuturesEngine().Build([open, close]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.RealizedPnlQuote, Is.EqualTo(9m));
            Assert.That(result.Matches.Single().RealizedPnl, Is.EqualTo(9m));
            Assert.That(result.Positions.Single().Quantity, Is.EqualTo(1m));
        }
    }

    [Test]
    public void Build_MixedLeverageLots_ReportsLeverageConsistentWithMargin()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fills = new[]
        {
            new TradeFillBuilder()
                .At(start)
                .As(TxType.OpenLong)
                .For("BTC")
                .WithQuantity(1m)
                .AtPrice(100m)
                .WithLeverage(2m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(1))
                .As(TxType.OpenLong)
                .For("BTC")
                .WithQuantity(1m)
                .AtPrice(100m)
                .WithLeverage(10m)
                .Build()
        };

        var result   = new FuturesEngine().Build(fills);
        var position = result.Positions.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position.Margin, Is.EqualTo(60m));
            Assert.That(position.Leverage, Is.EqualTo(10m / 3m));
            Assert.That(position.LiquidationPrice, Is.EqualTo(70m));
        }
    }

    [Test]
    public void Build_SplitClose_HasSamePnlAsSingleClose()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var open  = new TradeFillBuilder()
            .At(start)
            .As(TxType.OpenLong)
            .For("ETH")
            .WithQuantity(10m)
            .AtPrice(100m)
            .WithLeverage(5m)
            .Build();
        var singleClose = new TradeFillBuilder()
            .At(start.AddMinutes(1))
            .As(TxType.CloseLong)
            .For("ETH")
            .WithQuantity(10m)
            .AtPrice(110m)
            .WithFee(2m)
            .Build();
        var firstClose = new TradeFillBuilder()
            .At(start.AddMinutes(1))
            .As(TxType.CloseLong)
            .For("ETH")
            .WithQuantity(4m)
            .AtPrice(110m)
            .WithFee(0.8m)
            .Build();
        var secondClose = new TradeFillBuilder()
            .At(start.AddMinutes(2))
            .As(TxType.CloseLong)
            .For("ETH")
            .WithQuantity(6m)
            .AtPrice(110m)
            .WithFee(1.2m)
            .Build();

        var engine       = new FuturesEngine();
        var singleResult = engine.Build([open, singleClose]);
        var splitResult  = engine.Build([open, firstClose, secondClose]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(singleResult.RealizedPnlQuote, Is.EqualTo(98m));
            Assert.That(splitResult.RealizedPnlQuote, Is.EqualTo(singleResult.RealizedPnlQuote));
            Assert.That(singleResult.Positions, Is.Empty);
            Assert.That(splitResult.Positions, Is.Empty);
        }
    }
}
