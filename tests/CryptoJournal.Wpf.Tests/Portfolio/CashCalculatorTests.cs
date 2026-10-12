using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Tests.Support;
using CryptoJournal.Wpf.ViewModels;

namespace CryptoJournal.Wpf.Tests.Portfolio;

[TestFixture]
internal sealed class CashCalculatorTests
{
    [Test]
    public void ComputeQuoteCash_BaseAssetDeposit_DeductsFeeFromQuoteCash()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fills = new[]
        {
            new TradeFillBuilder()
                .At(start)
                .As(TxType.Deposit)
                .For("USDT")
                .WithQuantity(100m)
                .WithFee(1m)
                .Build(),
            new TradeFillBuilder()
                .At(start.AddMinutes(1))
                .As(TxType.Deposit)
                .For("STRK")
                .WithQuantity(100m)
                .AtPrice(0.04m)
                .WithFee(2m)
                .Build()
        };

        var cash = CashCalculator.ComputeQuoteCash(fills, "USDT");

        Assert.That(cash, Is.EqualTo(97m));
    }
}
