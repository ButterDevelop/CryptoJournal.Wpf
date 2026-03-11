using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Exchanges;
using CryptoJournal.Wpf.Infrastructure.Serialization;
using CryptoJournal.Wpf.Services.Exchanges;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace CryptoJournal.Wpf.Storage.Candles;

public sealed class FileCandleCache : ICandleCache
{
    private readonly IMarketDataClientResolver _resolver;

    // Maintain granular locks per exchange/symbol/interval to prevent concurrent file or index corruption
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public FileCandleCache(IMarketDataClientResolver resolver)
    {
        _resolver = resolver;
    }

    public async Task<CandleCacheIndex> LoadIndexAsync(string exchange, string symbol, string interval, CancellationToken ct = default)
    {
        exchange = exchange.Trim().ToLowerInvariant();
        symbol   = symbol.Trim().ToUpperInvariant();
        interval = interval.Trim().ToLowerInvariant();

        var path = CandlePathResolver.GetIndexPath(exchange, symbol, interval);
        if (!File.Exists(path))
        {
            return new CandleCacheIndex { Exchange = exchange, Symbol = symbol, Interval = interval };
        }

        await using var fs = File.OpenRead(path);
        var idx = await JsonSerializer.DeserializeAsync<CandleCacheIndex>(fs, JsonUtil.Options, ct)
              ?? new CandleCacheIndex();

        idx.Exchange = exchange;
        idx.Symbol   = symbol;
        idx.Interval = interval;
        return idx;
    }

    public async Task SaveIndexAsync(CandleCacheIndex index, CancellationToken ct = default)
    {
        index.Exchange = index.Exchange.Trim().ToLowerInvariant();
        index.Symbol   = index.Symbol.Trim().ToUpperInvariant();
        index.Interval = index.Interval.Trim().ToLowerInvariant();

        var path = CandlePathResolver.GetIndexPath(index.Exchange, index.Symbol, index.Interval);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var tmp = path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, index, JsonUtil.Options, ct);

        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }

    public async Task UpsertAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candlesUtcSorted, CancellationToken ct = default)
    {
        if (candlesUtcSorted.Count == 0) return;

        // Validate chronological sorting
        for (int i = 1; i < candlesUtcSorted.Count; i++)
            if (candlesUtcSorted[i].OpenTimeUtc < candlesUtcSorted[i - 1].OpenTimeUtc)
                throw new InvalidOperationException("Candles must be sorted by OpenTimeUtc ascending.");

        var idx = await LoadIndexAsync(exchange, symbol, interval, ct);

        long totalAdded = 0;

        foreach (var group in candlesUtcSorted.GroupBy(c => new { c.OpenTimeUtc.UtcDateTime.Year, c.OpenTimeUtc.UtcDateTime.Month }))
        {
            var first = group.First();
            var path  = CandlePathResolver.GetChunkFilePath(exchange, symbol, interval, first.OpenTimeUtc);

            var existing = await ReadChunkAllAsync(path, ct);
            var existingCount = existing.Count;

            var merged = existing
                .Concat(group)
                .GroupBy(c => c.OpenTimeUtc)
                .Select(g => g.Last())
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            totalAdded += merged.Count - existingCount;

            await WriteChunkAllAsync(path, merged, ct);
        }

        var newMinTime = candlesUtcSorted.Min(c => c.OpenTimeUtc);
        var newMaxTime = candlesUtcSorted.Max(c => c.OpenTimeUtc);

        idx.FirstOpenTimeUtc = idx.FirstOpenTimeUtc is null ? newMinTime : (newMinTime < idx.FirstOpenTimeUtc ? newMinTime : idx.FirstOpenTimeUtc);
        idx.LastOpenTimeUtc  = idx.LastOpenTimeUtc  is null ? newMaxTime : (newMaxTime > idx.LastOpenTimeUtc  ? newMaxTime : idx.LastOpenTimeUtc);

        idx.Count += totalAdded;

        // Update the All-Time High track upon any new candle inserts
        var maxHighCandle = candlesUtcSorted.OrderByDescending(c => c.High).First();
        if (idx.AthHigh is null || maxHighCandle.High > idx.AthHigh.Value)
        {
            idx.AthHigh        = maxHighCandle.High;
            idx.AthHighTimeUtc = maxHighCandle.OpenTimeUtc;
        }

        await SaveIndexAsync(idx, ct);
    }

    public async Task<DateTimeOffset?> GetLastOpenTimeUtcAsync(string exchange, string symbol, string interval, CancellationToken ct = default)
    {
        var idx = await LoadIndexAsync(exchange, symbol, interval, ct);
        return idx.LastOpenTimeUtc;
    }

    public async Task AppendAsync(string exchange, string symbol, string interval, IReadOnlyList<Candle> candlesUtcSorted, CancellationToken ct = default)
    {
        if (candlesUtcSorted.Count == 0)
            return;

        // Ensure strict chronological ascending order
        for (int i = 1; i < candlesUtcSorted.Count; i++)
        {
            if (candlesUtcSorted[i].OpenTimeUtc <= candlesUtcSorted[i - 1].OpenTimeUtc)
                throw new InvalidOperationException("Candles must be sorted strictly by OpenTimeUtc.");
        }

        var idx  = await LoadIndexAsync(exchange, symbol, interval, ct);
        var last = idx.LastOpenTimeUtc;

        // Filter dataset to prevent duplicate candle entries
        var toWrite = last is null
            ? candlesUtcSorted
            : candlesUtcSorted.Where(c => c.OpenTimeUtc > last.Value).ToList();

        if (toWrite.Count > 0)
        {
            var athCandle = toWrite.OrderByDescending(c => c.High).First();
            if (idx.AthHigh is null || athCandle.High > idx.AthHigh.Value)
            {
                idx.AthHigh        = athCandle.High;
                idx.AthHighTimeUtc = athCandle.OpenTimeUtc;
            }
        }

        if (toWrite.Count == 0)
            return;

        // Persist candles to storage chunked by month
        foreach (var group in toWrite.GroupBy(c => new { c.OpenTimeUtc.UtcDateTime.Year, c.OpenTimeUtc.UtcDateTime.Month }))
        {
            var first = group.First();
            var path  = CandlePathResolver.GetChunkFilePath(exchange, symbol, interval, first.OpenTimeUtc);

            // Append to GZip file: open a FileStream and wrap it with a GZipStream
            var existing = await ReadChunkAllAsync(path, ct);
            var merged = existing.Concat(group).OrderBy(c => c.OpenTimeUtc).ToList();

            // De-duplicate aggregated entries using their OpenTime
            merged = merged
                .GroupBy(c => c.OpenTimeUtc)
                .Select(g => g.Last())
                .OrderBy(c => c.OpenTimeUtc)
                .ToList();

            await WriteChunkAllAsync(path, merged, ct);
        }

        idx.LastOpenTimeUtc = toWrite[^1].OpenTimeUtc;
        idx.Count += toWrite.Count;

        await SaveIndexAsync(idx, ct);
    }

    public async IAsyncEnumerable<Candle> ReadAllAsync(string exchange, string symbol, string interval, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var dir = CandlePathResolver.GetSeriesDir(exchange, symbol, interval);
        if (!Directory.Exists(dir))
            yield break;

        var files = Directory.GetFiles(dir, "*.jsonl.gz").OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            foreach (var c in await ReadChunkAllAsync(file, ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return c;
            }
        }
    }

    private static async Task<List<Candle>> ReadChunkAllAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            return [];

        await using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var sr = new StreamReader(gz, Encoding.UTF8);

        var list = new List<Candle>();
        string? line;
        while ((line = await sr.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;

            var candle = JsonSerializer.Deserialize<Candle>(line, JsonUtil.JsonlOptions);
            if (candle is not null)
                list.Add(candle);
        }

        return list.OrderBy(c => c.OpenTimeUtc).ToList();
    }

    private static async Task WriteChunkAllAsync(string path, List<Candle> candles, CancellationToken ct)
    {
        var tmp = path + ".tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        using (var sw = new StreamWriter(gz, Encoding.UTF8))
        {
            foreach (var c in candles)
            {
                ct.ThrowIfCancellationRequested();
                var jsonLine = JsonSerializer.Serialize(c, JsonUtil.JsonlOptions);
                await sw.WriteLineAsync(jsonLine);
            }
        }

        File.Copy(tmp, path, overwrite: true);
        File.Delete(tmp);
    }

    public Task ClearAsync(string exchange, string symbol, string interval, CancellationToken ct)
    {
        var dir = CandlePathResolver.GetSeriesDir(exchange, symbol, interval);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    public async Task<CandleCacheIndex> GetOrBuildIndexAsync(
        string exchangeId,
        string pair,
        string interval,
        CancellationToken ct)
    {
        exchangeId = (exchangeId ?? "").Trim().ToLowerInvariant();
        pair       = (pair       ?? "").Trim().ToUpperInvariant();
        interval   = (interval   ?? "").Trim().ToLowerInvariant();

        if (exchangeId.Length == 0 || pair.Length == 0 || interval.Length == 0)
            return new CandleCacheIndex { Exchange = exchangeId, Symbol = pair, Interval = interval };

        var key  = $"{exchangeId}|{pair}|{interval}";
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Priority 1: Attempt to load an existing index map (optimal fast path)
            var idx = await LoadIndexAsync(exchangeId, pair, interval, ct).ConfigureAwait(false);
            if (idx.Count > 0 && idx.AthHigh is not null && idx.AthHigh.Value > 0m)
                return idx;

            // Priority 2: Rebuild index from existing chunk files if the index map is missing or void
            // This mitigates errors when files exist but the index file failed to generate properly
            var rebuilt = await TryRebuildIndexFromFilesAsync(exchangeId, pair, interval, ct).ConfigureAwait(false);
            if (rebuilt is not null && rebuilt.Count > 0 && rebuilt.AthHigh is not null && rebuilt.AthHigh.Value > 0m)
                return rebuilt;

            // Priority 3: Fetch historical candles from the assigned exchange API to initialize local storage
            IMarketDataClient client;
            try
            {
                client = _resolver.GetRequired(exchangeId);
            }
            catch
            {
                // Return the current (likely empty) dataset if the exchange adapter is not registered
                return idx;
            }

            // Paginates backwards chronically to backfill historical missing candles
            // Market clients implicitly return recent candles when start and end parameters are null
            const int limitPerRequest = 1000;
            const int maxTotalCandles = 50_000; // Hard cap limit to prevent out-of-memory errors and excessive bandwidth

            DateTimeOffset? endUtc       = null;
            DateTimeOffset? lastEarliest = null;

            var totalWritten = 0;

            while (totalWritten < maxTotalCandles)
            {
                ct.ThrowIfCancellationRequested();

                IReadOnlyList<Candle> batch;
                try
                {
                    batch = await client.GetCandlesAsync(
                        symbol:   pair,
                        interval: interval,
                        startUtc: null,
                        endUtc:   endUtc,
                        limit:    limitPerRequest,
                        ct:       ct
                    ).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Terminate the build process upon encountering network or exchange API errors
                    break;
                }

                if (batch is null || batch.Count == 0)
                    break;

                // Organize batch chronologically and ensure distinct OpenTimeUtc values
                var ordered = batch.Where(c => c is not null)
                                   .GroupBy(c => c.OpenTimeUtc)
                                   .Select(g => g.Last())
                                   .OrderBy(c => c.OpenTimeUtc)
                                   .ToList();

                if (ordered.Count == 0)
                    break;

                // Commit changes to disk (Upsert implicitly handles chunk partitioning, indexing, and ATH tracking)
                await UpsertAsync(exchangeId, pair, interval, ordered, ct).ConfigureAwait(false);
                totalWritten += ordered.Count;

                // Shift the pagination cursor backwards chronologically:
                var earliest = ordered[0].OpenTimeUtc;

                // Break iteration to prevent infinite loops if the exchange returns redundant data
                if (lastEarliest is not null && earliest >= lastEarliest.Value)
                    break;

                lastEarliest = earliest;

                // Ask for candles strictly earlier than the earliest candle in this batch
                endUtc = earliest.AddMilliseconds(-1);

                // Terminate initialization if the exchange response is smaller than the requested limit
                if (batch.Count < limitPerRequest)
                    break;
            }

            // Refresh the in-memory index map following the complete build
            idx = await LoadIndexAsync(exchangeId, pair, interval, ct).ConfigureAwait(false);
            return idx;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CandleCacheIndex?> TryRebuildIndexFromFilesAsync(
        string            exchangeId,
        string            pair,
        string            interval,
        CancellationToken ct)
    {
        // Scan physical chunk files and regenerate the internal index structure procedurally
        var dir = CandlePathResolver.GetSeriesDir(exchangeId, pair, interval);
        if (!Directory.Exists(dir))
            return null;

        var files = Directory.GetFiles(dir, "*.jsonl.gz")
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                         .ToArray();

        if (files.Length == 0)
            return null;

        var idx = new CandleCacheIndex
        {
            Exchange = exchangeId,
            Symbol   = pair,
            Interval = interval,
            Count    = 0
        };

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            var list = await ReadChunkAllAsync(file, ct).ConfigureAwait(false);
            if (list.Count == 0)
                continue;

            idx.Count += list.Count;

            var first = list[0].OpenTimeUtc;
            var last  = list[^1].OpenTimeUtc;

            idx.FirstOpenTimeUtc = idx.FirstOpenTimeUtc is null
                ? first
                : (first < idx.FirstOpenTimeUtc.Value ? first : idx.FirstOpenTimeUtc);

            idx.LastOpenTimeUtc = idx.LastOpenTimeUtc is null
                ? last
                : (last > idx.LastOpenTimeUtc.Value ? last : idx.LastOpenTimeUtc);

            // Recalculate and update the All-Time High relative to this specific chunk
            var maxHigh = list[0];
            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].High > maxHigh.High)
                    maxHigh = list[i];
            }

            if (idx.AthHigh is null || maxHigh.High > idx.AthHigh.Value)
            {
                idx.AthHigh = maxHigh.High;
                idx.AthHighTimeUtc = maxHigh.OpenTimeUtc;
            }
        }

        if (idx.Count == 0)
            return null;

        // Persist the regenerated index map to disk to optimize future load times
        await SaveIndexAsync(idx, ct).ConfigureAwait(false);
        return idx;
    }
}