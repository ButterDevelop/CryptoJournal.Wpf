using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Portfolio;

[TestFixture]
internal sealed class PortfolioCalculatorTests
{
    [Test]
    public void Calculate_StrkPartialSale_UsesRemainingCostBasisForUnrealizedReturn()
    {
        var environment = new TestEnvironmentService();
        var calculator  = new PortfolioCalculator(
            environment,
            new FifoCostBasisEngine(environment),
            new FuturesEngine());

        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var buy   = new TradeFillBuilder()
            .At(start)
            .As(TxType.Buy)
            .For("STRK")
            .WithQuantity(11_250m)
            .AtPrice(0.04m)
            .Build();
        var sell = new TradeFillBuilder()
            .At(start.AddMinutes(1))
            .As(TxType.Sell)
            .For("STRK")
            .WithQuantity(3_750m)
            .AtPrice(0.12m)
            .Build();

        var snapshot = calculator.Calculate(
            [buy, sell],
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["STRK"] = 0.12m
            });

        var position = snapshot.Positions.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.RealizedPnlQuote, Is.EqualTo(300m));
            Assert.That(position.Quantity, Is.EqualTo(7_500m));
            Assert.That(position.CostBasisQuote, Is.EqualTo(300m));
            Assert.That(position.MarketValueQuote, Is.EqualTo(900m));
            Assert.That(position.UnrealizedPnlQuote, Is.EqualTo(600m));
            Assert.That(position.UnrealizedPnlPct, Is.EqualTo(2m));
        }
    }
}
