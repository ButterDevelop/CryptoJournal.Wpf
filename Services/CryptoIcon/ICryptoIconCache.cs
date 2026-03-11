using System.Windows.Media;

namespace CryptoJournal.Wpf.Services.CryptoIcon
{
    public interface ICryptoIconCache
    {
        ImageSource DefaultIcon { get; }
        Task<ImageSource> GetAsync(string symbol, CancellationToken ct = default);
    }
}