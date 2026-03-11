namespace CryptoJournal.Wpf.Services.Environments;

public interface IEnvironmentStore
{
    Task<EnvironmentState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(EnvironmentState state, CancellationToken ct = default);
}

public sealed record EnvironmentState(
    List<EnvironmentDto> Environments,
    string               CurrentId
);

public sealed record EnvironmentDto(
    string Id,
    string Name,
    string QuoteCurrency,
    bool   IsCurrent
);