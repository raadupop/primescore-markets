namespace PrimeScore.SharedKernel;

/// <summary>Wall-clock time. Injected so tests and replay can control it.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
