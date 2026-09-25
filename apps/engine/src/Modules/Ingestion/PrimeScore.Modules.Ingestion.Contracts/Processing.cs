using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Contracts;

/// <summary>Recorded signals oldest observation first, for downstream stages that process in time order.</summary>
public sealed record GetSignalsInObservationOrder(int Skip, int Take) : IQuery<SignalPage>;

/// <summary>
/// One series' observations strictly before <paramref name="Before"/>, oldest first, at most
/// <paramref name="Length"/> (the classifier's reference window, brief §8). A series is an
/// instrument within a category, optionally narrowed to one variant (e.g. <c>IMPLIED_VOLATILITY</c>
/// rather than a <c>PRICE</c> submitted for the same symbol). When sources overlap, one point is
/// kept per New York date and variant: the earliest recorded.
/// </summary>
public sealed record GetObservationSeries(
    string Instrument,
    SourceCategory Category,
    string? Variant,
    DateTimeOffset Before,
    int Length) : IQuery<ObservationSeries>;

public sealed record ObservationSeries(IReadOnlyList<ObservationPoint> Points)
{
    /// <summary>Observation time of the newest point; null when empty.</summary>
    public DateTimeOffset? LastObservedAt => Points.Count == 0 ? null : Points[^1].ObservedAt;
}

public sealed record ObservationPoint(DateTimeOffset ObservedAt, double Value, Guid SignalId);
