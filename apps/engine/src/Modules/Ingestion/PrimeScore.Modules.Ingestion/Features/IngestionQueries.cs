using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Ingestion.Features;

/// <summary>Filters on observation time, never on recording time (SRS SIG-004, brief §6).</summary>
internal sealed class GetSignalsHandler(IngestionReadStore reads) : IQueryHandler<GetSignals, SignalPage>
{
    public async Task<SignalPage> HandleAsync(GetSignals query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = query.Filter;
        await using var context = reads.Open();
        var rows = context.Signals.Where(row => row.Sequence <= filter.MaxSequence);
        if (filter.AsOf is { } asOf)
        {
            var cut = asOf.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.ObservedAtMs <= cut);
        }

        if (filter.From is { } from)
        {
            var start = from.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.ObservedAtMs >= start);
        }

        if (filter.To is { } to)
        {
            var end = to.ToUnixTimeMilliseconds();
            rows = rows.Where(row => row.ObservedAtMs <= end);
        }

        if (filter.Category is { } category)
        {
            var name = category.ToString();
            rows = rows.Where(row => row.Category == name);
        }

        if (!string.IsNullOrWhiteSpace(filter.Instrument))
        {
            var instrument = filter.Instrument.Trim();
            rows = rows.Where(row => EF.Functions.Collate(row.Instrument, "NOCASE") == instrument);
        }

        if (!string.IsNullOrWhiteSpace(filter.Variant))
        {
            rows = rows.Where(row => row.Variant == filter.Variant);
        }

        if (!string.IsNullOrEmpty(filter.SourcePrefix))
        {
            rows = rows.Where(row => row.SourceIdentifier.StartsWith(filter.SourcePrefix));
        }

        if (!string.IsNullOrWhiteSpace(filter.Provider))
        {
            var provider = filter.Provider.Trim();
            rows = rows.Where(row => EF.Functions.Collate(row.Provider, "NOCASE") == provider);
        }

        var total = await rows.CountAsync(cancellationToken).ConfigureAwait(false);
        var page = await rows
            .OrderByDescending(row => row.ObservedAtMs).ThenByDescending(row => row.Sequence)
            .Skip(Math.Max(0, filter.Skip)).Take(Math.Clamp(filter.Take, 1, 1000))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new SignalPage(page.Select(SignalViews.From).ToArray(), total);
    }
}

internal sealed class GetSignalHandler(IngestionReadStore reads) : IQueryHandler<GetSignal, SignalView?>
{
    public async Task<SignalView?> HandleAsync(GetSignal query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = query.SignalId.ToString("D");
        await using var context = reads.Open();
        var row = await context.Signals.SingleOrDefaultAsync(signal => signal.SignalId == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : SignalViews.From(row);
    }
}

internal sealed class GetRejectionsHandler(IngestionReadStore reads) : IQueryHandler<GetRejections, IReadOnlyList<RejectionView>>
{
    public async Task<IReadOnlyList<RejectionView>> HandleAsync(GetRejections query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = reads.Open();
        var rows = await context.Rejections
            .OrderByDescending(row => row.Sequence)
            .Take(Math.Clamp(query.Take, 1, 500))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new RejectionView(
            Guid.Parse(row.RejectionId),
            row.Sequence,
            DateTimeOffset.FromUnixTimeMilliseconds(row.RecordedAtMs),
            row.SourceIdentifier,
            CanonicalJson.Deserialize<string[]>(row.Errors),
            row.Raw)).ToArray();
    }
}

/// <summary>
/// One row per registered adapter, enabled or not: live state from <see cref="SourceRuntime"/>,
/// run history (last error, partial, counts, flags, note) from <c>ing_source_runs</c>, coverage
/// from the recorded signals of each adapter's series. API rows recorded under an identifier
/// before its prefix was reserved are not the adapter's, so they are not its coverage
/// (<see cref="SourcePrefixes.IsAdapterRecorded"/>).
/// </summary>
internal sealed class GetSourceStatusHandler(
    IngestionReadStore reads,
    SourceRuntime runtime,
    IEnumerable<ISourceAdapter> adapters) : IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>
{
    public async Task<IReadOnlyList<SourceStatus>> HandleAsync(GetSourceStatus query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var registered = adapters.ToArray();
        var catalogue = registered.ToDictionary(adapter => adapter, adapter => adapter.Series);
        var identifiers = catalogue.Values.SelectMany(series => series).Select(series => series.SourceIdentifier).Distinct(StringComparer.Ordinal).ToArray();
        await using var context = reads.Open();
        var perSource = await context.Signals
            .Where(row => identifiers.Contains(row.SourceIdentifier) && EF.Functions.Collate(row.Provider, "NOCASE") != SourcePrefixes.ApiProvider)
            .GroupBy(row => row.SourceIdentifier)
            .Select(group => new { Source = group.Key, Count = group.LongCount(), Latest = group.Max(row => row.ObservedAtMs) })
            .ToDictionaryAsync(group => group.Source, cancellationToken).ConfigureAwait(false);
        var statuses = new List<SourceStatus>(registered.Length);
        foreach (var adapter in registered)
        {
            var state = runtime.For(adapter.Name);
            var runs = context.SourceRuns.Where(run => run.Source == adapter.Name);
            var lastRun = await runs.OrderByDescending(run => run.StartedAtMs).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var lastSuccess = await runs.Where(run => run.Succeeded).OrderByDescending(run => run.StartedAtMs)
                .Select(run => run.FinishedAtMs).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            var series = catalogue[adapter]
                .Select(item => new SeriesStatus(
                    item.SeriesId, item.Instrument, item.Category, item.MappingVerified,
                    perSource.TryGetValue(item.SourceIdentifier, out var recorded) ? recorded.Count : 0,
                    perSource.TryGetValue(item.SourceIdentifier, out var latest) ? DateTimeOffset.FromUnixTimeMilliseconds(latest.Latest) : null,
                    item.Timing,
                    item.SourceIdentifier,
                    item.Url))
                .ToArray();
            var reason = adapter.DisabledReason;
            statuses.Add(new SourceStatus(
                adapter.Name,
                reason is null,
                reason,
                state.Running,
                lastRun is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastRun.StartedAtMs),
                lastSuccess is { } success ? DateTimeOffset.FromUnixTimeMilliseconds(success) : null,
                lastRun?.Error ?? (lastRun is { FinishedAtMs: null } && !state.Running && !query.StoredOnly ? "interrupted before finishing" : null),
                lastRun is { Succeeded: true, Error: not null },
                state.NextRunAt,
                lastRun is { FinishedAtMs: not null, Succeeded: true }
                    ? new SourceRunCounts(lastRun.Accepted, lastRun.Duplicates, lastRun.Revised, lastRun.Missing, lastRun.Rejected)
                    : null,
                series,
                adapter.Description,
                adapter.Schedule.Describe(),
                lastRun?.Flags is { } flags ? CanonicalJson.Deserialize<string[]>(flags) : null,
                lastRun?.Note));
        }

        return statuses;
    }
}

internal static class SignalViews
{
    public static SignalView From(SignalRow row) => new(
        Guid.Parse(row.SignalId),
        row.Sequence,
        new CorrelationId(Guid.Parse(row.CorrelationId)),
        Enum.Parse<SourceCategory>(row.Category),
        row.SourceIdentifier,
        row.Instrument,
        row.Variant,
        DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs),
        DateTimeOffset.FromUnixTimeMilliseconds(row.RecordedAtMs),
        row.PayloadType,
        row.Value,
        row.Payload,
        CanonicalJson.Deserialize<SignalProvenance>(row.Provenance));
}
