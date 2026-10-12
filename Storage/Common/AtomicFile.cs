using System.IO;
using System.Text;
using System.Text.Json;

namespace CryptoJournal.Wpf.Storage.Common;

public static class AtomicFile
{
    public static async Task WriteJsonAsync<T>(
        string                path,
        T                     value,
        JsonSerializerOptions options,
        CancellationToken     ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(options);

        var temporaryPath = CreateTemporaryPath(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using (var stream = CreateWriteThroughStream(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, ct);
                await stream.FlushAsync(ct);
                stream.Flush(flushToDisk: true);
            }

            Commit(temporaryPath, path);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    public static void WriteText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var temporaryPath = CreateTemporaryPath(path);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            using (var stream = CreateWriteThroughStream(temporaryPath))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            Commit(temporaryPath, path);
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private static FileStream CreateWriteThroughStream(string path)
    {
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough | FileOptions.Asynchronous);
    }

    private static string CreateTemporaryPath(string path)
        => path + $".{Guid.NewGuid():N}.tmp";

    private static void Commit(string temporaryPath, string destinationPath)
    {
        if (!File.Exists(destinationPath))
        {
            File.Move(temporaryPath, destinationPath);
            return;
        }

        File.Replace(
            temporaryPath,
            destinationPath,
            destinationPath + ".bak",
            ignoreMetadataErrors: true);
    }

    private static void DeleteTemporaryFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
