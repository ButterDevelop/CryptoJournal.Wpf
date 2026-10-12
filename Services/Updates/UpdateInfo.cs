namespace CryptoJournal.Wpf.Services.Updates;

public sealed record UpdateInfo(
    string Version,
    string ReleaseUrl,
    string InstallerName,
    string InstallerUrl,
    string ChecksumUrl);
