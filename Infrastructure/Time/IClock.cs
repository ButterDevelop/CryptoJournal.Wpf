namespace CryptoJournal.Wpf.Infrastructure.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}