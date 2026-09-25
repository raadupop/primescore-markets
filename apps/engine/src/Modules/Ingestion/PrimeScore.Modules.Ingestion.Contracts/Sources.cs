using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Ingestion.Contracts;

/// <summary>Queue an on-demand pull of a source adapter (the UI's "pull now"; not an API endpoint).</summary>
public sealed record RequestSourcePull(string Source) : ICommand<SourcePullAck>;

/// <param name="Queued">False when the source is disabled or unknown; <paramref name="Reason"/> says why.</param>
public sealed record SourcePullAck(bool Queued, string? Reason) : ICommandAck;

public sealed record GetSourceStatus : IQuery<IReadOnlyList<SourceStatus>>;

public sealed record SourceStatus(
    string Source,
    bool Enabled,
    string? DisabledReason,
    bool Running,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError,
    bool LastRunPartial,
    DateTimeOffset? NextRunAt,
    SourceRunCounts? LastRun,
    IReadOnlyList<SeriesStatus> Series);

/// <param name="Revised">Values that differed from what was already recorded for the same observation; the first recorded value is kept.</param>
public sealed record SourceRunCounts(int Accepted, int Duplicates, int Revised, int Missing, int Rejected);

/// <param name="Missing">Observations the provider reports without a value (FRED "."), which are skipped, never filled.</param>
public sealed record SeriesStatus(
    string SeriesId,
    string Instrument,
    SourceCategory Category,
    bool MappingVerified,
    long Recorded,
    DateTimeOffset? LatestObservedAt,
    string Timing);
