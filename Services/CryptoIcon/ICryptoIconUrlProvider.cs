namespace CryptoJournal.Wpf.Services.CryptoIcon
{
    public interface ICryptoIconUrlProvider
    {
        Task<Uri?> TryGetIconUrlAsync(string symbol, CancellationToken ct = default);
    }
}