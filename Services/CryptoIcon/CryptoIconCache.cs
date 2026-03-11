using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CryptoJournal.Wpf.Services.CryptoIcon
{
    public sealed class CryptoIconCache : ICryptoIconCache
    {
        private const string DefaultCoinPng = "pack://application:,,,/Assets/Icons/default_coin.png";

        private readonly HttpClient                            _http;
        private readonly IReadOnlyList<ICryptoIconUrlProvider> _providers;
        private readonly string                                _dir;

        private readonly ConcurrentDictionary<string, Task<ImageSource>> _inflight =
            new(StringComparer.OrdinalIgnoreCase);

        public ImageSource DefaultIcon { get; }

        public CryptoIconCache(HttpClient http, IEnumerable<ICryptoIconUrlProvider> providers)
        {
            _http      = http;
            _providers = providers.ToList();
            _dir       = Path.Combine(
                             Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                             "CryptoJournal", "icons");
            Directory.CreateDirectory(_dir);

            DefaultIcon = LoadBitmapFromPackUri(DefaultCoinPng, decodePx: 32);
        }

        public Task<ImageSource> GetAsync(string symbol, CancellationToken ct = default)
        {
            var s = Normalize(symbol);
            if (string.IsNullOrWhiteSpace(s))
                return Task.FromResult(DefaultIcon);

            return _inflight.GetOrAdd(s, _ => LoadOrDownloadAsync(s, ct));
        }

        private async Task<ImageSource> LoadOrDownloadAsync(string symbol, CancellationToken ct)
        {
            try
            {
                var pngPath = Path.Combine(_dir, $"{symbol}.png");

                if (File.Exists(pngPath))
                    return LoadBitmapFromFile(pngPath);

                foreach (var p in _providers)
                {
                    var url = await p.TryGetIconUrlAsync(symbol, ct);
                    if (url is null) continue;
                    if (url.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;

                    var bytes = await _http.GetByteArrayAsync(url, ct);

                    var tmp = pngPath + ".tmp";
                    await File.WriteAllBytesAsync(tmp, bytes, ct);
                    File.Copy(tmp, pngPath, overwrite: true);
                    File.Delete(tmp);

                    return LoadBitmapFromBytes(bytes, decodePx: 32);
                }

                // not found from providers
                return DefaultIcon;
            }
            catch
            {
                return DefaultIcon;
            }
            finally
            {
                // so that the _inflight dictionary doesn't grow infinitely
                _inflight.TryRemove(symbol, out _);
            }
        }

        private static string Normalize(string s) => (s ?? "").Trim().ToUpperInvariant();

        private static BitmapImage LoadBitmapFromPackUri(string packUri, int decodePx)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = decodePx;
            bi.UriSource = new Uri(packUri, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private static BitmapImage LoadBitmapFromFile(string path)
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }

        private static BitmapImage LoadBitmapFromBytes(byte[] bytes, int decodePx)
        {
            using var ms = new MemoryStream(bytes);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = decodePx;
            bi.StreamSource = ms;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
    }
}