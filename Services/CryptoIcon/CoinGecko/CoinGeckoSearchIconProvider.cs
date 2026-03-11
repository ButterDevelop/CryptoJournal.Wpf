using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CryptoJournal.Wpf.Services.CryptoIcon.CoinGecko
{
    public sealed class CoinGeckoSearchIconProvider : ICryptoIconUrlProvider
    {
        private readonly HttpClient _http;

        public CoinGeckoSearchIconProvider(HttpClient http) => _http = http;

        public async Task<Uri?> TryGetIconUrlAsync(string symbol, CancellationToken ct = default)
        {
            symbol = symbol.Trim().ToLowerInvariant();
            if (symbol.Length == 0) return null;

            // if CoinGecko requires a key, add the x-cg-demo-api-key / x-cg-pro-api-key header according to their documentation
            var url = $"https://api.coingecko.com/api/v3/search?query={Uri.EscapeDataString(symbol)}";

            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            await using var s = await resp.Content.ReadAsStreamAsync(ct);
            var data = await JsonSerializer.DeserializeAsync<SearchResp>(s, cancellationToken: ct);
            if (data?.Coins is null) return null;

            var hit = data.Coins.FirstOrDefault(c => string.Equals(c.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                  ?? data.Coins.FirstOrDefault();

            if (hit?.Thumb is null) return null;
            return Uri.TryCreate(hit.Thumb, UriKind.Absolute, out var u) ? u : null;
        }

        private sealed class SearchResp
        {
            [JsonPropertyName("coins")]
            public List<Coin>? Coins { get; set; }
        }

        private sealed class Coin
        {
            [JsonPropertyName("symbol")] public string? Symbol { get; set; }
            [JsonPropertyName("thumb")]  public string? Thumb  { get; set; }
            [JsonPropertyName("large")]  public string? Large  { get; set; }
        }
    }
}