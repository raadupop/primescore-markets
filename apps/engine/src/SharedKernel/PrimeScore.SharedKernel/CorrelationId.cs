namespace PrimeScore.SharedKernel;

/// <summary>Traces one signal from ingestion to decision (SRS OBS-001).</summary>
public readonly record struct CorrelationId(Guid Value)
{
    public static CorrelationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
