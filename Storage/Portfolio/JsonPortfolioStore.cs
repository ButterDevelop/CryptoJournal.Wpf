using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Infrastructure.Serialization;
using CryptoJournal.Wpf.Services.Environments;
using CryptoJournal.Wpf.Storage.Common;
using System.IO;
using System.Text.Json;

namespace CryptoJournal.Wpf.Storage.Portfolio;

public sealed class JsonPortfolioStore : IPortfolioStore
{
    private readonly IEnvironmentService _env;
    private readonly string              _dir;
    private readonly SemaphoreSlim       _gate = new(1, 1);

    public JsonPortfolioStore(IEnvironmentService env)
        : this(env, AppDataPaths.RootDir)
    {
    }

    internal JsonPortfolioStore(IEnvironmentService env, string rootDirectory)
    {
        _env = env;
        _dir = AppDataPaths.EnsureDir(Path.Combine(rootDirectory, "Portfolio"));
    }

    public async Task<IReadOnlyList<TradeFill>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        try
        {
            var filePath = GetFilePath();
            if (!File.Exists(filePath))
                return [];

            try
            {
                await using var stream = File.OpenRead(filePath);
                var data = await JsonSerializer.DeserializeAsync<List<TradeFill>>(stream, JsonUtil.Options, ct);
                return data ?? [];
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Portfolio file contains invalid JSON: {filePath}", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyList<TradeFill> fills, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fills);
        await _gate.WaitAsync(ct);

        try
        {
            await AtomicFile.WriteJsonAsync(GetFilePath(), fills, JsonUtil.Options, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetFilePath()
    {
        var environmentDirectory = AppDataPaths.EnsureDir(Path.Combine(_dir, "envs", _env.Current.Id));
        return Path.Combine(environmentDirectory, "portfolio.json");
    }
}
