using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Services.Portfolio;
using CryptoJournal.Wpf.Tests.Support;
using CryptoJournal.Wpf.ViewModels;

namespace CryptoJournal.Wpf.Tests.Portfolio;

[TestFixture]
internal sealed class CashReconciliationTests
{
    [Test]
    public void FuturesPartialClose_AccountsForOpeningAndClosingFeesExactlyOnce()
    {
        var environment = new TestEnvironmentService();
        var calculator  = new PortfolioCalculator(
            environment,
            new FifoCostBasisEngine(environment),
            new FuturesEngine());
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fills = new[]
        {
            new TradeFillBuilder()
                .At(start)
                .As(TxType.Deposit)
                .For("USDT")
                .WithQuantity(1_000m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(1))
                .As(TxType.OpenLong)
                .For("BTC")
                .WithQuantity(10m)
                .AtPrice(100m)
                .WithFee(2m)
                .WithLeverage(5m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(2))
                .As(TxType.CloseLong)
                .For("BTC")
                .WithQuantity(4m)
                .AtPrice(110m)
                .WithFee(4m)
                .WithLeverage(5m)
                .Build()
        };

        var snapshot = calculator.Calculate(
            fills,
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["BTC"] = 110m
            });
        var position     = snapshot.FuturesPositions.Single();
        var baseCash     = CashCalculator.ComputeQuoteCash(fills, "USDT");
        var freeCash     = baseCash + snapshot.FuturesRealizedPnlQuote - position.Margin;
        var positionValue = position.Margin + position.UnrealizedPnl!.Value;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(baseCash, Is.EqualTo(998m));
            Assert.That(snapshot.FuturesRealizedPnlQuote, Is.EqualTo(36m));
            Assert.That(freeCash, Is.EqualTo(914m));
            Assert.That(positionValue, Is.EqualTo(180m));
            Assert.That(freeCash + positionValue, Is.EqualTo(1_094m));
        }
    }
}
