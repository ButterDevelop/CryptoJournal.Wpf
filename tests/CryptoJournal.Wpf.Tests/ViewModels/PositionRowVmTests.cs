using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.ViewModels;

namespace CryptoJournal.Wpf.Tests.ViewModels;

[TestFixture]
internal sealed class PositionRowVmTests
{
    [Test]
    public void UnrealizedReturn_UsesRemainingCostBasis()
    {
        var snapshot = new PositionSnapshot(
            Symbol:             "STRK",
            Quantity:           7_500m,
            CostBasisQuote:     300m,
            MarkPriceQuote:     0.12m,
            MarketValueQuote:   900m,
            UnrealizedPnlQuote: 600m,
            UnrealizedPnlPct:   2m);

        var row = new PositionRowVm(snapshot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.AvgBuyPrice, Is.EqualTo(0.04m));
            Assert.That(row.MarketValueQuote, Is.EqualTo(900m));
            Assert.That(row.UnrealizedPnlQuote, Is.EqualTo(600m));
            Assert.That(row.UnrealizedPnlPct, Is.EqualTo(2m));
        }
    }
}
