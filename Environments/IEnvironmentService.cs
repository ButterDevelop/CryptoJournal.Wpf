using CryptoJournal.Wpf.Domain.Models;

namespace CryptoJournal.Wpf.Services.Environments;

public interface IEnvironmentService
{
    IReadOnlyList<EnvironmentProfile> Environments { get; }
    EnvironmentProfile Current { get; }

    event EventHandler? CurrentChanged;
    event EventHandler? EnvironmentsChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task SetCurrentAsync(string id, CancellationToken ct = default);

    Task<EnvironmentProfile> CreateAsync(string name, string quoteCurrency, bool isCurrent, CancellationToken ct = default);
    Task UpdateAsync(string id, string name, string quoteCurrency, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}