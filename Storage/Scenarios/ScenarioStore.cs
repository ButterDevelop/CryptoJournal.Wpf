using CryptoJournal.Wpf.Storage.Common;
using System.IO;
using System.Text.Json;

namespace CryptoJournal.Wpf.Storage.Scenarios;

public sealed class ScenarioStore : IScenarioStore
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly Lock _sync = new();

    private string _envId = "";
    private ScenarioStateDto _state = new();

    public event Action<string>? ScenariosChanged;

    public void NotifyScenariosChanged(string symbol)
    {
        ScenariosChanged?.Invoke(symbol);
    }

    public void Load(string environmentId)
    {
        if (string.IsNullOrWhiteSpace(environmentId))
            throw new ArgumentException("EnvironmentId is required.", nameof(environmentId));

        var envId = environmentId.Trim();
        var file  = ScenarioPaths.GetActiveScenarioFile(envId);

        ScenarioStateDto loaded;
        if (!File.Exists(file))
        {
            loaded = new ScenarioStateDto { EnvironmentId = envId };
        }
        else
        {
            var json = File.ReadAllText(file);
            loaded = JsonSerializer.Deserialize<ScenarioStateDto>(json, _json)
                     ?? new ScenarioStateDto { EnvironmentId = envId };

            loaded.EnvironmentId = envId;

            // Normalize symbol keys upon loading for consistency
            loaded.PlansBySymbol = loaded.PlansBySymbol
                .ToDictionary(k => NormalizeSymbol(k.Key),
                              v => v.Value,
                              StringComparer.OrdinalIgnoreCase);
        }

        lock (_sync)
        {
            _envId = envId;
            _state = loaded;
        }
    }

    public void Save()
    {
        ScenarioStateDto snapshot;
        string envId;

        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(_envId))
                throw new InvalidOperationException("ScenarioStore.Load(environmentId) must be called before Save().");

            envId = _envId;

            // Create a deep snapshot of plans and legs to ensure stable serialization
            snapshot = new ScenarioStateDto
            {
                EnvironmentId = _state.EnvironmentId,
                PlansBySymbol = _state.PlansBySymbol.ToDictionary(
                    kv => kv.Key,
                    kv => ClonePlan(kv.Value),
                    StringComparer.OrdinalIgnoreCase)
            };
        }

        var file = ScenarioPaths.GetActiveScenarioFile(envId);
        var tmp  = file + ".tmp";

        var json = JsonSerializer.Serialize(snapshot, _json);
        File.WriteAllText(tmp, json);

        if (File.Exists(file)) File.Delete(file);
        File.Move(tmp, file);
    }

    public ScenarioPlanDto? TryGetPlan(string symbol)
    {
        var key = NormalizeSymbol(symbol);
        if (key.Length == 0) return null;

        lock (_sync)
        {
            return _state.PlansBySymbol.TryGetValue(key, out var plan)
                ? ClonePlan(plan) // IMPORTANT: Provide a cloned instance to prevent unintended mutations
                : null;
        }
    }

    public void SetPlan(ScenarioPlanDto plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var key = NormalizeSymbol(plan.Symbol);
        if (key.Length == 0) return;

        plan.Symbol = key;

        lock (_sync)
        {
            _state.PlansBySymbol[key] = ClonePlan(plan); // IMPORTANT: Store a cloned instance to decouple from the original object
        }
    }

    public IReadOnlyList<ScenarioPlanDto> GetPlansSnapshot()
    {
        lock (_sync)
        {
            return _state.PlansBySymbol.Values.Select(Clone).ToList();
        }
    }

    public bool RemovePlan(string symbol)
    {
        var key = NormalizeSymbol(symbol);
        if (key.Length == 0) return false;

        lock (_sync)
        {
            return _state.PlansBySymbol.Remove(key);
        }
    }

    private static ScenarioPlanDto Clone(ScenarioPlanDto p)
    {
        return new ScenarioPlanDto
        {
            Symbol        = p.Symbol,
            IsPercentMode = p.IsPercentMode,
            BaseQty       = p.BaseQty,
            Legs          = p.Legs?.Select(l => new ScenarioLegDto
            {
                InputAmount = l.InputAmount,
                TargetPrice = l.TargetPrice,
                Note        = l.Note
            }).ToList() ?? []
        };
    }

    private static ScenarioPlanDto ClonePlan(ScenarioPlanDto p) => new()
    {
        Symbol        = NormalizeSymbol(p.Symbol),
        IsPercentMode = p.IsPercentMode,
        BaseQty       = p.BaseQty,
        Legs          = (p.Legs ?? []).Select(CloneLeg).ToList()
    };

    private static ScenarioLegDto CloneLeg(ScenarioLegDto l) => new()
    {
        InputAmount = l.InputAmount,
        TargetPrice = l.TargetPrice,
        Note        = l.Note ?? string.Empty
    };

    private static string NormalizeSymbol(string? symbol)
        => (symbol ?? "").Trim().ToUpperInvariant();
}