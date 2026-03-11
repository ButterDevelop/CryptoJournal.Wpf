using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Services.Exchanges;
using CryptoJournal.Wpf.UI;
using System.Collections.Concurrent;

namespace CryptoJournal.Wpf.Services.Prices;

/// <summary>
/// Continually polls the latest market prices for a monitored set of symbols.
/// Defaults to DI-ordered exchange fallbacks and caches the fastest provider.
/// </summary>
public sealed class MarketDataPollingService : IDisposable
{
    private readonly IMarketDataClientResolver _resolver;
    private readonly IEnvironmentService       _env;

    private readonly ConcurrentDictionary<string, byte>   _symbols                   = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _preferredExchangeBySymbol = new(StringComparer.OrdinalIgnoreCase);

    private readonly int _maxConcurrency;

    private CancellationTokenSource? _cts;
    private Task?                    _loop;

    public event Action<string, decimal>? LastPriceUpdated; // Triggered upon successful price retrieval; provides the symbol and its quote-denominated price

    public MarketDataPollingService(
        IMarketDataClientResolver resolver,
        IEnvironmentService env,
        int maxConcurrency = 6)
    {
        _resolver = resolver;
        _env = env;
        _maxConcurrency = Math.Max(1, maxConcurrency);
    }

    /// <summary>
    /// Updates the active monitoring set. Symbols must be standardized base assets (e.g., "BTC", "ETH").
    /// </summary>
    public void SetSymbols(IEnumerable<string> symbols)
    {
        _symbols.Clear();

        foreach (var s in symbols.Select(NormalizeSymbol)
                                 .Where(x => x.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _symbols.TryAdd(s, 0);
        }
    }

    public void AddSymbol(string symbol)
    {
        symbol = NormalizeSymbol(symbol);
        if (symbol.Length == 0) return;
        _symbols.TryAdd(symbol, 0);
    }

    public void RemoveSymbol(string symbol)
    {
        symbol = NormalizeSymbol(symbol);
        if (symbol.Length == 0) return;

        _symbols.TryRemove(symbol, out _);
        _preferredExchangeBySymbol.TryRemove(symbol, out _);
    }

    public void Start(TimeSpan interval)
    {
        Stop();

        _cts  = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(interval, _cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts  = null;
        _loop = null;
    }

    private async Task LoopAsync(TimeSpan interval, CancellationToken ct)
    {
        if (interval <= TimeSpan.Zero)
            interval = TimeSpan.FromSeconds(30);

        using var timer = new PeriodicTimer(interval);

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var syms = _symbols.Keys.ToList();
            if (syms.Count == 0) continue;

            var quote = (_env.Current.QuoteCurrency ?? "USDT").Trim().ToUpperInvariant();

            using var gate = new SemaphoreSlim(_maxConcurrency, _maxConcurrency);

            var tasks = syms.Select(async symbol =>
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var pair = SymbolUtil.ToPair(symbol, quote);

                    // Execution fast-path using the cached preferred exchange adapter
                    if (_preferredExchangeBySymbol.TryGetValue(symbol, out var preferred))
                    {
                        var p = await SafeGetLastAsync(preferred, pair, ct).ConfigureAwait(false);
                        if (p is not null)
                        {
                            LastPriceUpdated?.Invoke(symbol, p.Value);
                            return;
                        }
                    }

                    // Fallback iteration through available exchanges in dependency-injection order
                    foreach (var ex in _resolver.Exchanges.Select(r => r.ExchangeId))
                    {
                        var p = await SafeGetLastAsync(ex, pair, ct).ConfigureAwait(false);
                        if (p is null) continue;

                        _preferredExchangeBySymbol[symbol] = ex;
                        LastPriceUpdated?.Invoke(symbol, p.Value);
                        return;
                    }
                }
                catch (OperationCanceledException) { /* ignore */ }
                catch
                {
                    // Suppress exceptions during individual symbol polling to maintain loop integrity
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Maintain continuous polling operations despite transient tick failures
            }
        }
    }

    private async Task<decimal?> SafeGetLastAsync(string exchangeId, string pair, CancellationToken ct)
    {
        try
        {
            var client = _resolver.GetRequired(exchangeId);
            var p      = await client.GetLastPriceAsync(pair, ct).ConfigureAwait(false);
            return (p is not null && p.Value > 0m) ? p : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static string NormalizeSymbol(string? s)
        => (s ?? "").Trim().ToUpperInvariant();

    public void Dispose()
    {
        _cts?.Dispose();
        Stop();
    }
}