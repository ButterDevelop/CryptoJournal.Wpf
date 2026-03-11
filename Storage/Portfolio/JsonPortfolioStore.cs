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

    private string _filePath = "";

    public JsonPortfolioStore(IEnvironmentService env)
    {
        _env = env;
        _dir = AppDataPaths.EnsureDir(Path.Combine(AppDataPaths.RootDir, "Portfolio"));
    }

    public async Task<IReadOnlyList<TradeFill>> LoadAsync(CancellationToken ct = default)
    {
        RecalculateFilePath();

        if (!File.Exists(_filePath))
            return [];

        await using var fs = File.OpenRead(_filePath);
        var data = await JsonSerializer.DeserializeAsync<List<TradeFill>>(fs, JsonUtil.Options, ct);
        return data ?? [];
    }
     
    public async Task SaveAsync(IReadOnlyList<TradeFill> fills, CancellationToken ct = default)
    {
        RecalculateFilePath();

        var tmp = _filePath + ".tmp";

        await using (var fs = File.Create(tmp))
        {
            await JsonSerializer.SerializeAsync(fs, fills, JsonUtil.Options, ct);
        }

        File.Copy(tmp, _filePath, overwrite: true);
        File.Delete(tmp);
    }

    private void RecalculateFilePath()
    {
        _filePath = Path.Combine(AppDataPaths.EnsureDir(Path.Combine(_dir, "envs", _env.Current.Id)), "portfolio.json");
    }
}