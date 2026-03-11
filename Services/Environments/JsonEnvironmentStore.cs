using System.IO;
using System.Text.Json;

namespace CryptoJournal.Wpf.Services.Environments;

internal sealed class JsonEnvironmentStore : IEnvironmentStore
{
    private static readonly JsonSerializerOptions _opt = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public JsonEnvironmentStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CryptoJournal");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "environments.json");
    }

    public async Task<EnvironmentState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
        {
            // default env
            var def = new EnvironmentState(
                Environments:
                [
                    new EnvironmentDto(Id: "main", Name: "Main", QuoteCurrency: "USDT", IsCurrent: true)
                ],
                CurrentId: "main"
            );

            await SaveAsync(def, ct);
            return def;
        }

        await using var fs = File.OpenRead(_path);
        var state = await JsonSerializer.DeserializeAsync<EnvironmentState>(fs, _opt, ct);

        if (state is null || state.Environments.Count == 0)
        {
            var def = new EnvironmentState(
                [new("main", "Main", "USDT", true)],
                "main");

            await SaveAsync(def, ct);
            return def;
        }

        // if the current one is lost, take the first one
        if (!state.Environments.Any(e => e.Id == state.CurrentId))
            state = state with { CurrentId = state.Environments[0].Id };

        return state;
    }

    public async Task SaveAsync(EnvironmentState state, CancellationToken ct = default)
    {
        await using var fs = File.Create(_path);
        await JsonSerializer.SerializeAsync(fs, state, _opt, ct);
    }
}