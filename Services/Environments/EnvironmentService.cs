using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Environments;

public sealed class EnvironmentService : IEnvironmentService
{
    private readonly IEnvironmentStore _store;

    private List<EnvironmentProfile> _envs = [];
    private EnvironmentProfile _current = new("main", "Main", "USDT", true);

    public IReadOnlyList<EnvironmentProfile> Environments => _envs;
    public EnvironmentProfile Current => _current;

    public event EventHandler? CurrentChanged;
    public event EventHandler? EnvironmentsChanged;

    public EnvironmentService(IEnvironmentStore store) => _store = store;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(ct);
        _envs = state.Environments
            .Select(x => new EnvironmentProfile(x.Id, x.Name, x.QuoteCurrency.Trim().ToUpperInvariant(), x.IsCurrent))
            .ToList();

        _current = _envs.First(e => e.Id == state.CurrentId);

        EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetCurrentAsync(string id, CancellationToken ct = default)
    {
        var next = _envs.FirstOrDefault(e => e.Id == id);
        if (next is null) return;
        if (next.Id == _current.Id) return;

        _current = next;

        await PersistAsync(ct);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<EnvironmentProfile> CreateAsync(string name, string quoteCurrency, bool isCurrent, CancellationToken ct = default)
    {
        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "New";

        quoteCurrency = (quoteCurrency ?? "USDT").Trim().ToUpperInvariant();
        if (quoteCurrency.Length < 3) quoteCurrency = "USDT";

        var id  = Guid.NewGuid().ToString("N")[..10];
        var env = new EnvironmentProfile(id, name, quoteCurrency, isCurrent);
        _envs.Add(env);

        await PersistAsync(ct);
        EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
        return env;
    }

    public async Task UpdateAsync(string id, string name, string quoteCurrency, CancellationToken ct = default)
    {
        var idx = _envs.FindIndex(e => e.Id == id);
        if (idx < 0) return;

        name = (name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) name = _envs[idx].Name;

        quoteCurrency = (quoteCurrency ?? _envs[idx].QuoteCurrency).Trim().ToUpperInvariant();
        if (quoteCurrency.Length < 3) quoteCurrency = _envs[idx].QuoteCurrency;

        var updated = _envs[idx] with { Name = name, QuoteCurrency = quoteCurrency };
        _envs[idx] = updated;

        // if you updated the current one
        if (_current.Id == id)
            _current = updated;

        await PersistAsync(ct);

        EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var toDel = _envs.FirstOrDefault(e => e.Id == id);
        if (toDel is null) return;

        if (_envs.Count == 1)
            return; // can't delete the last one

        _envs.Remove(toDel);

        if (_current.Id == id)
            _current = _envs[0];

        await PersistAsync(ct);

        EnvironmentsChanged?.Invoke(this, EventArgs.Empty);
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var state = new EnvironmentState(
            Environments: _envs.Select(e => new EnvironmentDto(e.Id, e.Name, e.QuoteCurrency, e.IsCurrent)).ToList(),
            CurrentId: _current.Id);

        await _store.SaveAsync(state, ct);
    }
}