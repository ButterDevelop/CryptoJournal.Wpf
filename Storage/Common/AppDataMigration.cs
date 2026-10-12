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
        if (File.Exists(Path.Combine(_destinationDirectory, MarkerFileName)))
            return new AppDataMigrationResult(0, []);
        var sources = _sourceDirectories
            .Where(Directory.Exists)
            .Where(path => !PathsEqual(path, _destinationDirectory))
            .ToArray();

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
        var copied = CopyMissingFiles(sources, _destinationDirectory, useAtomicFileMove: true);
        WriteMarker(_destinationDirectory, sources, copied);
        return new AppDataMigrationResult(copied, sources);
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

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static void WriteMarker(
        string                destination,
        IReadOnlyList<string> sources,
        int                   copiedFileCount)
    {
        var marker = new
        {
            version = 1,
            migratedAtUtc = DateTimeOffset.UtcNow,
            sources,
            copiedFileCount
        };
        var path = Path.Combine(destination, MarkerFileName);
        var json = JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true });

        File.WriteAllText(path, json);
    }
}

public sealed record AppDataMigrationResult(
    int                   CopiedFileCount,
    IReadOnlyList<string> SourceDirectories);
