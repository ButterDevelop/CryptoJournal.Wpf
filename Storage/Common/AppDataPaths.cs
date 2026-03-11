using System.IO;

namespace CryptoJournal.Wpf.Storage.Common;

public static class AppDataPaths
{
    public static string RootDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoJournal_data");

    public static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}