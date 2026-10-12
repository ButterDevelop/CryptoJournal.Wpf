using System.IO;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Storage.Portfolio;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Storage;

[TestFixture]
internal sealed class JsonPortfolioStoreTests
{
    [Test]
    public async Task SaveAsync_RepeatedReplacement_KeepsPreviousFileAsBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new JsonPortfolioStore(new TestEnvironmentService(), temporaryDirectory.Path);
        var first = new TradeFillBuilder()
            .As(TxType.Buy)
            .For("STRK")
            .WithQuantity(10m)
            .AtPrice(0.04m)
            .Build();
        var second = new TradeFillBuilder()
            .As(TxType.Buy)
            .For("BTC")
            .WithQuantity(1m)
            .AtPrice(50_000m)
            .Build();
        var third = new TradeFillBuilder()
            .As(TxType.Buy)
            .For("ETH")
            .WithQuantity(2m)
            .AtPrice(2_000m)
            .Build();

        await store.SaveAsync([first]);
        await store.SaveAsync([second]);
        await store.SaveAsync([third]);

        var loaded    = await store.LoadAsync();
        var livePath  = temporaryDirectory.GetPath("Portfolio", "envs", "test", "portfolio.json");
        var backupPath = livePath + ".bak";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(loaded.Select(fill => fill.Id), Is.EqualTo(new[] { third.Id }));
            Assert.That(File.Exists(backupPath), Is.True);
            Assert.That(File.ReadAllText(backupPath), Does.Contain(second.Id.ToString()));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(livePath)!, "*.tmp"), Is.Empty);
        }
    }

    [Test]
    public void LoadAsync_InvalidJson_ReportsAffectedPath()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store    = new JsonPortfolioStore(new TestEnvironmentService(), temporaryDirectory.Path);
        var livePath = temporaryDirectory.GetPath("Portfolio", "envs", "test", "portfolio.json");

        Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);
        File.WriteAllText(livePath, "not-json");

        var exception = Assert.ThrowsAsync<InvalidDataException>(async () => await store.LoadAsync());

        Assert.That(exception!.Message, Does.Contain(livePath));
    }
}
