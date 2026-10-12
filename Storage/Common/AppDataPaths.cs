using System.IO;

namespace CryptoJournal.Wpf.Storage.Common;

public static class AppDataPaths
{
    public static string RootDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CryptoJournal");

    public static string LegacyPortableRootDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CryptoJournal_data");

    public static string LegacyRoamingRootDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CryptoJournal");

    public static string EnsureDir(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
