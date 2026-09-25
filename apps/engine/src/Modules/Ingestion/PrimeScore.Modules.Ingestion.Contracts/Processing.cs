using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Contracts;

/// <summary>Recorded signals oldest observation first, for downstream stages that process in time order.</summary>
public sealed record GetSignalsInObservationOrder(int Skip, int Take) : IQuery<SignalPage>;

/// <summary>Just the ids and observation times of recorded signals, oldest observation first (no payloads).</summary>
public sealed record GetSignalKeysInObservationOrder(int Skip, int Take) : IQuery<IReadOnlyList<SignalKey>>;

public sealed record SignalKey(Guid SignalId, DateTimeOffset ObservedAt);

/// <summary>The given recorded signals (at most 1,000), oldest observation first; unknown ids are left out.</summary>
public sealed record GetSignalsById(IReadOnlyList<Guid> SignalIds) : IQuery<SignalPage>;

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
    int Length,
    long MaxSequence = long.MaxValue) : IQuery<ObservationSeries>;

public sealed record ObservationSeries(IReadOnlyList<ObservationPoint> Points)
{
    /// <summary>Observation time of the newest point; null when empty.</summary>
    public DateTimeOffset? LastObservedAt => Points.Count == 0 ? null : Points[^1].ObservedAt;
}

/// <param name="LedgerSequence">Order of recording: where sources overlap, the lowest was recorded first.</param>
public sealed record ObservationPoint(DateTimeOffset ObservedAt, double Value, Guid SignalId, long LedgerSequence);
