using CryptoJournal.Wpf.Domain.Models;
using CryptoJournal.Wpf.Services.Environments;

namespace CryptoJournal.Wpf.Tests.Support;

internal sealed class TestEnvironmentService : IEnvironmentService
{
    private readonly List<EnvironmentProfile> _environments;

    public TestEnvironmentService(string quoteCurrency = "USDT")
    {
        Current       = new EnvironmentProfile("test", "Test", quoteCurrency, true);
        _environments = [Current];
    }

    public IReadOnlyList<EnvironmentProfile> Environments => _environments;
    public EnvironmentProfile Current { get; private set; }

    public event EventHandler? CurrentChanged
    {
        add { }
        remove { }
    }

    public event EventHandler? EnvironmentsChanged
    {
        add { }
        remove { }
    }

    public Task InitializeAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetCurrentAsync(string id, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<EnvironmentProfile> CreateAsync(
        string            name,
        string            quoteCurrency,
        bool              isCurrent,
        CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task UpdateAsync(
        string            id,
        string            name,
        string            quoteCurrency,
        CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task DeleteAsync(string id, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }
}
