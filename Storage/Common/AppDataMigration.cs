using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Infrastructure.Serialization;
using System.IO;
using System.Text.Json;

namespace CryptoJournal.Wpf.Storage.Common;

public sealed class AppDataMigration
{
    private const string MarkerFileName = ".migration-v1.json";

    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip      = FileAttributes.ReparsePoint
    };

    private readonly string[] _sourceDirectories;
    private readonly string   _destinationDirectory;

    public AppDataMigration(IEnumerable<string> sourceDirectories, string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceDirectories);

        _sourceDirectories = sourceDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _destinationDirectory = Path.GetFullPath(destinationDirectory);
    }

    public static AppDataMigration CreateDefault()
    {
        return new AppDataMigration(
            [AppDataPaths.LegacyPortableRootDir, AppDataPaths.LegacyRoamingRootDir],
            AppDataPaths.RootDir);
    }

    public AppDataMigrationResult Migrate()
    {
        var migratedSources = ReadMigratedSources();
        var sources = _sourceDirectories
            .Where(Directory.Exists)
            .Where(path => !PathsEqual(path, _destinationDirectory))
            .Where(path => !migratedSources.Contains(path))
            .ToArray();

        if (sources.Length == 0)
            return new AppDataMigrationResult(0, []);

        ValidateJsonFiles(sources);

        if (Directory.Exists(_destinationDirectory))
            return MergeIntoExistingDestination(sources);

        return CreateDestination(sources);
    }

    private AppDataMigrationResult CreateDestination(IReadOnlyList<string> sources)
    {
        var parent = Path.GetDirectoryName(_destinationDirectory)
                     ?? throw new InvalidOperationException("Application data directory has no parent.");
        var staging = _destinationDirectory + $".migration-{Guid.NewGuid():N}";

        Directory.CreateDirectory(parent);
        Directory.CreateDirectory(staging);

        try
        {
            var copied = CopyMissingFiles(sources, staging, useAtomicFileMove: false);
            WriteMarker(staging, sources, copied);
            Directory.Move(staging, _destinationDirectory);
            return new AppDataMigrationResult(copied, sources);
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private AppDataMigrationResult MergeIntoExistingDestination(IReadOnlyList<string> sources)
    {
        var changed = MergePortfolioFiles(sources, _destinationDirectory);
        changed += CopyMissingFiles(sources, _destinationDirectory, useAtomicFileMove: true);
        WriteMarker(_destinationDirectory, sources, changed);
        return new AppDataMigrationResult(changed, sources);
    }

    private static int MergePortfolioFiles(IReadOnlyList<string> sources, string destination)
    {
        var changed = 0;

        foreach (var source in sources)
        {
            foreach (var sourceFile in EnumerateFiles(source).Where(IsPortfolioFile))
            {
                var relativePath    = Path.GetRelativePath(source, sourceFile);
                var destinationFile = Path.Combine(destination, relativePath);

                if (!File.Exists(destinationFile))
                    continue;

                var existing = DeserializePortfolio(destinationFile);
                var existingIds = existing.Select(fill => fill.Id).ToHashSet();
                var additions = DeserializePortfolio(sourceFile)
                    .Where(fill => existingIds.Add(fill.Id))
                    .ToList();

                if (additions.Count == 0)
                    continue;

                var merged = existing
                    .Concat(additions)
                    .OrderBy(fill => fill.TimeUtc)
                    .ThenBy(fill => fill.Id)
                    .ToList();
                var json = JsonSerializer.Serialize(merged, JsonUtil.Options);

                AtomicFile.WriteText(destinationFile, json);
                changed++;
            }
        }

        return changed;
    }

    private static int CopyMissingFiles(
        IReadOnlyList<string> sources,
        string                destination,
        bool                  useAtomicFileMove)
    {
        var copied = 0;

        foreach (var source in sources)
        {
            foreach (var sourceFile in EnumerateFiles(source))
            {
                var relativePath   = Path.GetRelativePath(source, sourceFile);
                var destinationFile = Path.Combine(destination, relativePath);

                if (File.Exists(destinationFile))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);

                if (useAtomicFileMove)
                {
                    CopyFileAtomically(sourceFile, destinationFile);
                }
                else
                {
                    File.Copy(sourceFile, destinationFile);
                }

                copied++;
            }
        }

        return copied;
    }

    private static void CopyFileAtomically(string sourceFile, string destinationFile)
    {
        var temporaryFile = destinationFile + $".migration-{Guid.NewGuid():N}.tmp";

        try
        {
            File.Copy(sourceFile, temporaryFile);
            File.Move(temporaryFile, destinationFile, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporaryFile))
                File.Delete(temporaryFile);
        }
    }

    private static void ValidateJsonFiles(IEnumerable<string> sources)
    {
        foreach (var source in sources)
        {
            foreach (var file in EnumerateFiles(source).Where(IsJsonFile))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    using var document = JsonDocument.Parse(stream);
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"Legacy application data contains invalid JSON: {file}", ex);
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFiles(string directory)
        => Directory.EnumerateFiles(directory, "*", EnumerationOptions)
            .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(Path.GetFileName(path), MarkerFileName, StringComparison.OrdinalIgnoreCase));

    private static bool IsJsonFile(string path)
        => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    private static bool IsPortfolioFile(string path)
        => string.Equals(Path.GetFileName(path), "portfolio.json", StringComparison.OrdinalIgnoreCase)
           && path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               .Contains("Portfolio", StringComparer.OrdinalIgnoreCase);

    private static List<TradeFill> DeserializePortfolio(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<List<TradeFill>>(stream, JsonUtil.Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Portfolio file contains invalid JSON: {path}", ex);
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private HashSet<string> ReadMigratedSources()
        => ReadMigratedSources(_destinationDirectory);

    private static HashSet<string> ReadMigratedSources(string destination)
    {
        var path = Path.Combine(destination, MarkerFileName);
        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var stream   = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("sources", out var sources))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            return sources.EnumerateArray()
                .Where(element => element.ValueKind == JsonValueKind.String)
                .Select(element => element.GetString())
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Select(source => Path.GetFullPath(source!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WriteMarker(
        string                destination,
        IReadOnlyList<string> sources,
        int                   copiedFileCount)
    {
        var markerPath      = Path.Combine(destination, MarkerFileName);
        var migratedSources = ReadMigratedSources(destination);
        migratedSources.UnionWith(sources);

        var marker = new
        {
            version = 1,
            migratedAtUtc = DateTimeOffset.UtcNow,
            sources = migratedSources.Order(StringComparer.OrdinalIgnoreCase),
            copiedFileCount
        };
        var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });

        AtomicFile.WriteText(markerPath, json);
    }
}

public sealed record AppDataMigrationResult(
    int                   CopiedFileCount,
    IReadOnlyList<string> SourceDirectories);
