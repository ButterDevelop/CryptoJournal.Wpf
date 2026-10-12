using System.IO;
using System.Text.Json;
using CryptoJournal.Wpf.Domain.Enums;
using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Infrastructure.Serialization;
using CryptoJournal.Wpf.Storage.Common;
using CryptoJournal.Wpf.Tests.Support;

namespace CryptoJournal.Wpf.Tests.Storage;

[TestFixture]
internal sealed class AppDataMigrationTests
{
    [Test]
    public void Migrate_FreshDestination_CombinesPortableAndRoamingData()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var portable   = temporaryDirectory.GetPath("portable");
        var roaming    = temporaryDirectory.GetPath("roaming");
        var destination = temporaryDirectory.GetPath("local");

        WriteText(Path.Combine(portable, "Portfolio", "envs", "main", "portfolio.json"), "[]");
        WriteText(Path.Combine(roaming, "environments.json"), "{}");
        WriteText(Path.Combine(roaming, "icons", "strk.png"), "image");

        var result = new AppDataMigration([portable, roaming], destination).Migrate();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CopiedFileCount, Is.EqualTo(3));
            Assert.That(File.Exists(Path.Combine(destination, "Portfolio", "envs", "main", "portfolio.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "environments.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, "icons", "strk.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(destination, ".migration-v1.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(portable, "Portfolio", "envs", "main", "portfolio.json")), Is.True);
        }
    }

    [Test]
    public void Migrate_ExistingDestination_PreservesExistingFilesAndCopiesMissingFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source      = temporaryDirectory.GetPath("source");
        var destination = temporaryDirectory.GetPath("destination");
        var settings    = Path.Combine(destination, "environments.json");

        WriteText(Path.Combine(source, "environments.json"), "{\"source\":true}");
        WriteText(Path.Combine(source, "Scenarios", "main", "active.json"), "{}");
        WriteText(settings, "{\"destination\":true}");

        var migration = new AppDataMigration([source], destination);
        var firstRun  = migration.Migrate();
        var secondRun = migration.Migrate();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstRun.CopiedFileCount, Is.EqualTo(1));
            Assert.That(secondRun.CopiedFileCount, Is.Zero);
            Assert.That(File.ReadAllText(settings), Is.EqualTo("{\"destination\":true}"));
            Assert.That(File.Exists(Path.Combine(destination, "Scenarios", "main", "active.json")), Is.True);
        }
    }

    [Test]
    public void Migrate_InvalidLegacyJson_LeavesDestinationUncreated()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source      = temporaryDirectory.GetPath("source");
        var destination = temporaryDirectory.GetPath("destination");
        var invalidFile = Path.Combine(source, "Portfolio", "portfolio.json");

        WriteText(invalidFile, "not-json");

        var migration = new AppDataMigration([source], destination);

        Assert.That(() => migration.Migrate(), Throws.TypeOf<InvalidDataException>());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(destination), Is.False);
            Assert.That(File.Exists(invalidFile), Is.True);
        }
    }

    [Test]
    public void Migrate_NewPortableSource_MergesPortfolioByTransactionId()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstSource  = temporaryDirectory.GetPath("debug");
        var secondSource = temporaryDirectory.GetPath("installed");
        var destination  = temporaryDirectory.GetPath("local");
        var relativePath = Path.Combine("Portfolio", "envs", "main", "portfolio.json");
        var shared = CreateFill("BTC", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sourceDuplicate = shared with { Note = "must not overwrite destination" };
        var installedOnly = CreateFill("STRK", shared.TimeUtc.AddDays(1));

        WritePortfolio(Path.Combine(firstSource, relativePath), [shared]);
        new AppDataMigration([firstSource], destination).Migrate();

        WritePortfolio(Path.Combine(secondSource, relativePath), [sourceDuplicate, installedOnly]);
        var result = new AppDataMigration([secondSource], destination).Migrate();
        var merged = ReadPortfolio(Path.Combine(destination, relativePath));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.CopiedFileCount, Is.EqualTo(1));
            Assert.That(merged.Select(fill => fill.Id),
                Is.EquivalentTo(new[] { shared.Id, installedOnly.Id }));
            Assert.That(merged.Single(fill => fill.Id == shared.Id).Note, Is.Null);
            Assert.That(File.Exists(Path.Combine(destination, relativePath) + ".bak"), Is.True);
        }
    }

    private static TradeFill CreateFill(string symbol, DateTimeOffset time)
        => new(
            Guid.NewGuid(),
            time,
            TxType.Deposit,
            symbol,
            1m,
            1m,
            0m);

    private static void WritePortfolio(string path, IReadOnlyList<TradeFill> fills)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(fills, JsonUtil.Options));
    }

    private static IReadOnlyList<TradeFill> ReadPortfolio(string path)
        => JsonSerializer.Deserialize<List<TradeFill>>(File.ReadAllText(path), JsonUtil.Options) ?? [];

    private static void WriteText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
