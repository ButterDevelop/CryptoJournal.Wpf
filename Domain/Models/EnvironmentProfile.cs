namespace CryptoJournal.Wpf.Domain.Models
{
    public sealed record EnvironmentProfile(
        string Id,
        string Name,
        string QuoteCurrency,
        bool   IsCurrent
    );
}