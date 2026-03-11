using System.IO;
using System.Text.Json;

namespace CryptoJournal.Wpf.Services.Exchanges;

public sealed class JsonSymbolExchangePinStore : ISymbolExchangePinStore
{
    private static readonly JsonSerializerOptions JSON_SETTINGS = new() { WriteIndented = true };

    private readonly string        _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Cached in-memory map to avoid re-reading file on every request.
    private Dictionary<string, string>? _cache;

    public JsonSymbolExchangePinStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CryptoJournal",
            "symbol_exchange_pins.json");
    }

    public async Task<string?> GetAsync(string symbol, CancellationToken ct = default)
    {
        symbol = NormalizeSymbol(symbol);

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            return _cache!.TryGetValue(symbol, out var ex) ? ex : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string symbol, string exchangeId, CancellationToken ct = default)
    {
        symbol = NormalizeSymbol(symbol);
        exchangeId = NormalizeExchange(exchangeId);

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            _cache![symbol] = exchangeId;
            await SaveAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string symbol, CancellationToken ct = default)
    {
        symbol = NormalizeSymbol(symbol);

        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedAsync(ct);
            if (_cache!.Remove(symbol))
                await SaveAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_cache is not null) return;

        if (!File.Exists(_path))
        {
            _cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var json = await File.ReadAllTextAsync(_path, ct);
        _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                 ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_cache, JSON_SETTINGS);

        await File.WriteAllTextAsync(_path, json, ct);
    }

    private static string NormalizeSymbol(string s)
        => (s ?? "").Trim().ToUpperInvariant();

    private static string NormalizeExchange(string s)
        => (s ?? "").Trim().ToLowerInvariant();
}