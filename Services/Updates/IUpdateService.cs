namespace CryptoJournal.Wpf.Services.Updates;

public interface IUpdateService
{
    Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken cancellationToken = default);

    Task<string> DownloadAsync(UpdateInfo update, CancellationToken cancellationToken = default);
}
