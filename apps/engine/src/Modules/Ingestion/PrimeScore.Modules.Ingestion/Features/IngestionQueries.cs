using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Features;

/// <summary>Filters on observation time, never on recording time (SRS SIG-004, brief §6).</summary>
internal sealed class GetSignalsHandler(IngestionReadStore reads) : IQueryHandler<GetSignals, SignalPage>
{
    public async Task<SignalPage> HandleAsync(GetSignals query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var filter = query.Filter;
        await using var context = reads.Open();
        var rows = context.Signals.AsQueryable();
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

internal sealed class GetSourceStatusHandler(
    IngestionReadStore reads,
    SourcePullQueue queue,
    IndicatorRegistry registry,
    IOptions<FredOptions> options) : IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>
{
    public async Task<IReadOnlyList<SourceStatus>> HandleAsync(GetSourceStatus query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var context = reads.Open();
        var runs = context.SourceRuns.Where(run => run.Source == FredOptions.SourceName);
        var lastRun = await runs.OrderByDescending(run => run.StartedAtMs).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var lastSuccess = await runs.Where(run => run.Succeeded).OrderByDescending(run => run.StartedAtMs)
            .Select(run => run.FinishedAtMs).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var perSource = await context.Signals
            .Where(row => row.Provider == FredOptions.SourceName)
            .GroupBy(row => row.SourceIdentifier)
            .Select(group => new { Source = group.Key, Count = group.LongCount(), Latest = group.Max(row => row.ObservedAtMs) })
            .ToDictionaryAsync(group => group.Source, cancellationToken).ConfigureAwait(false);
        var series = FredSeriesCatalog.Build(registry, settings)
            .Select(item => new SeriesStatus(
                item.SeriesId, item.Instrument, item.Category, item.Verified,
                perSource.TryGetValue(item.SourceIdentifier, out var recorded) ? recorded.Count : 0,
                perSource.TryGetValue(item.SourceIdentifier, out var latest) ? DateTimeOffset.FromUnixTimeMilliseconds(latest.Latest) : null,
                item.TimingDescription))
            .ToArray();
        return
        [
            new SourceStatus(
                FredOptions.SourceName,
                settings.DisabledReason() is null,
                settings.DisabledReason(),
                queue.Running,
                lastRun is null ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastRun.StartedAtMs),
                lastSuccess is { } success ? DateTimeOffset.FromUnixTimeMilliseconds(success) : null,
                lastRun?.Error ?? (lastRun is { FinishedAtMs: null } && !queue.Running ? "interrupted before finishing" : null),
                lastRun is { Succeeded: true, Error: not null },
                queue.NextRunAt,
                lastRun is { FinishedAtMs: not null, Succeeded: true }
                    ? new SourceRunCounts(lastRun.Accepted, lastRun.Duplicates, lastRun.Revised, lastRun.Missing, lastRun.Rejected)
                    : null,
                series),
        ];
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
