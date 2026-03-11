using CryptoJournal.Wpf.Storage.Common;
using System.IO;

namespace CryptoJournal.Wpf.Storage.Attachments;

public interface ILocalImageStore
{
    string AttachmentsDir { get; }
    Task<string> SaveImageAsync(string sourceFilePath);
    Task<string> SaveImageStreamAsync(Stream sourceStream, string extension);
    void DeleteImage(string filename);
    string GetImagePath(string filename);
}

public sealed class LocalImageStore : ILocalImageStore
{
    public string AttachmentsDir => AppDataPaths.EnsureDir(Path.Combine(AppDataPaths.RootDir, "Attachments"));

    public async Task<string> SaveImageAsync(string sourceFilePath)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Image source not found", sourceFilePath);

        var ext      = Path.GetExtension(sourceFilePath);
        var filename = $"{Guid.NewGuid():N}{ext}";
        var dest     = Path.Combine(AttachmentsDir, filename);

        using var srcStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        using var dstStream = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        
        await srcStream.CopyToAsync(dstStream);

        return filename;
    }

    public async Task<string> SaveImageStreamAsync(Stream sourceStream, string extension)
    {
        var filename = $"{Guid.NewGuid():N}{extension}";
        var dest     = Path.Combine(AttachmentsDir, filename);

        using var dstStream = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await sourceStream.CopyToAsync(dstStream);

        return filename;
    }

    public void DeleteImage(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return;
        var path = Path.Combine(AttachmentsDir, filename);
        if (File.Exists(path))
            try { File.Delete(path); } catch { /* ignore if locked */ }
    }

    public string GetImagePath(string filename)
    {
        return Path.Combine(AttachmentsDir, filename);
    }
}
