using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Classification.Aggregation;

/// <summary>
/// The newest composite of a context by observation time, at or before <c>AsOf</c> (SRS
/// SIG-004); where several share that time, the last recorded.
/// </summary>
internal sealed class GetCompositeHandler(ClassificationReadStore reads) : IQueryHandler<GetComposite, CompositeView?>
{
    public async Task<CompositeView?> HandleAsync(GetComposite query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = AggregateViews.ContextName(query.Context);
        var cut = (query.AsOf ?? DateTimeOffset.MaxValue).ToUnixTimeMilliseconds();
        await using var db = reads.Open();
        var row = await db.Composites
            .Where(composite => composite.Context == context && composite.AsOfMs <= cut)
            .OrderByDescending(composite => composite.AsOfMs).ThenByDescending(composite => composite.Sequence)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : AggregateViews.From(row);
    }
}

/// <summary>The newest dislocation of a context by observation time, at or before <c>AsOf</c>.</summary>
internal sealed class GetDislocationHandler(ClassificationReadStore reads) : IQueryHandler<GetDislocation, DislocationView?>
{
    public async Task<DislocationView?> HandleAsync(GetDislocation query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = AggregateViews.ContextName(query.Context);
        var cut = (query.AsOf ?? DateTimeOffset.MaxValue).ToUnixTimeMilliseconds();
        await using var db = reads.Open();
        var row = await db.Dislocations
            .Where(dislocation => dislocation.Context == context && dislocation.AsOfMs <= cut)
            .OrderByDescending(dislocation => dislocation.AsOfMs).ThenByDescending(dislocation => dislocation.Sequence)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : AggregateViews.From(row);
    }
}

/// <summary>The day's last composite and its dislocation, per New York date, oldest first.</summary>
internal sealed class GetCompositeHistoryHandler(ClassificationReadStore reads) : IQueryHandler<GetCompositeHistory, IReadOnlyList<DailyAggregate>>
{
    public async Task<IReadOnlyList<DailyAggregate>> HandleAsync(GetCompositeHistory query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var context = AggregateViews.ContextName(query.Context);
        var from = (query.From ?? DateTimeOffset.MinValue).ToUnixTimeMilliseconds();
        var to = (query.To ?? DateTimeOffset.MaxValue).ToUnixTimeMilliseconds();
        await using var db = reads.Open();
        var composites = await db.Composites
            .Where(composite => composite.Context == context && composite.AsOfMs >= from && composite.AsOfMs <= to)
            .Select(composite => new { composite.CompositeId, composite.AsOfMs, composite.Sequence, composite.Score, composite.ConfigVersion, composite.WeightingSchemeId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var days = composites
            .GroupBy(composite => MarketTime.NewYorkDate(DateTimeOffset.FromUnixTimeMilliseconds(composite.AsOfMs)))
            .Select(day => (Date: day.Key, Last: day.MaxBy(composite => (composite.AsOfMs, composite.Sequence))!))
            .OrderBy(day => day.Date)
            .ToArray();
        var ids = days.Select(day => day.Last.CompositeId).ToArray();
        var dislocations = (await db.Dislocations
                .Where(dislocation => dislocation.Context == context && dislocation.AsOfMs >= from && dislocation.AsOfMs <= to)
                .Select(dislocation => new
                {
                    dislocation.DislocationId, dislocation.CompositeId, dislocation.MarketObservedIv, dislocation.DislocationValue,
                    dislocation.Threshold, dislocation.ThresholdBreached, dislocation.Regime, dislocation.SensitivityFactor,
                    dislocation.RegimePercentile,
                })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(dislocation => ids.Contains(dislocation.CompositeId, StringComparer.Ordinal))
            .ToDictionary(dislocation => dislocation.CompositeId, StringComparer.Ordinal);
        return days.Select(day =>
        {
            var dislocation = dislocations.GetValueOrDefault(day.Last.CompositeId);
            return new DailyAggregate(
                day.Date,
                DateTimeOffset.FromUnixTimeMilliseconds(day.Last.AsOfMs),
                Guid.Parse(day.Last.CompositeId),
                day.Last.Sequence,
                day.Last.ConfigVersion,
                day.Last.WeightingSchemeId,
                day.Last.Score,
                dislocation is null ? null : Guid.Parse(dislocation.DislocationId),
                dislocation?.MarketObservedIv,
                dislocation?.DislocationValue,
                dislocation?.Threshold,
                dislocation?.ThresholdBreached,
                dislocation?.Regime,
                dislocation?.SensitivityFactor,
                dislocation?.RegimePercentile);
        }).ToArray();
    }
}

internal sealed class GetAggregatesAfterHandler(ClassificationReadStore reads) : IQueryHandler<GetAggregatesAfter, IReadOnlyList<AggregateRecord>>
{
    public async Task<IReadOnlyList<AggregateRecord>> HandleAsync(GetAggregatesAfter query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = reads.Open();
        var dislocations = await db.Dislocations
            .Where(dislocation => dislocation.Sequence > query.AfterSequence)
            .OrderBy(dislocation => dislocation.Sequence)
            .Take(Math.Clamp(query.Take, 1, 1000))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (dislocations.Count == 0)
        {
            return [];
        }

        var compositeIds = dislocations.Select(dislocation => dislocation.CompositeId).Distinct(StringComparer.Ordinal).ToArray();
        var composites = (await db.Composites.Where(composite => compositeIds.Contains(composite.CompositeId))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(composite => composite.CompositeId, AggregateViews.From, StringComparer.Ordinal);
        var assessmentIds = composites.Values
            .SelectMany(composite => composite.Contributing.SelectMany(category => category.ConfirmedAssessments))
            .Select(id => id.ToString("D"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var assessments = (await db.Assessments.Where(assessment => assessmentIds.Contains(assessment.AssessmentId))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(assessment => Guid.Parse(assessment.AssessmentId));
        return dislocations
            .Where(dislocation => composites.ContainsKey(dislocation.CompositeId))
            .Select(dislocation =>
            {
                var composite = composites[dislocation.CompositeId];
                var confirmed = composite.Contributing
                    .SelectMany(category => category.ConfirmedAssessments)
                    .Select(id => assessments.GetValueOrDefault(id))
                    .OfType<AssessmentRow>()
                    .Select(row => new ConfirmedAssessment(
                        Guid.Parse(row.AssessmentId), Guid.Parse(row.SignalId), row.Sequence, Enum.Parse<SourceCategory>(row.Category),
                        row.Instrument, DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs), row.Score ?? 0, row.Certainty ?? 0, row.IsFallback))
                    .ToArray();
                return new AggregateRecord(composite, AggregateViews.From(dislocation), confirmed);
            })
            .ToArray();
    }
}

internal sealed class GetAggregateContextsHandler(ClassificationReadStore reads) : IQueryHandler<GetAggregateContexts, IReadOnlyList<AggregateContextView>>
{
    public async Task<IReadOnlyList<AggregateContextView>> HandleAsync(GetAggregateContexts query, CancellationToken cancellationToken)
    {
        await using var db = reads.Open();
        var contexts = await db.Composites
            .GroupBy(composite => composite.Context)
            .Select(group => new { Context = group.Key, Count = group.Count(), Newest = group.Max(composite => composite.AsOfMs) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return contexts
            .OrderBy(context => context.Context, StringComparer.Ordinal)
            .Select(context => new AggregateContextView(context.Context, context.Count, DateTimeOffset.FromUnixTimeMilliseconds(context.Newest)))
            .ToArray();
    }
}

internal static class AggregateViews
{
    /// <summary>Context names are stored lower case (settings validation); the API matches case-insensitively.</summary>
    public static string ContextName(string context) => (context ?? "").Trim().ToLowerInvariant();

    public static CompositeView From(CompositeRow row) => new(
        Guid.Parse(row.CompositeId),
        row.Sequence,
        new CorrelationId(Guid.Parse(row.CorrelationId)),
        new ConfigVersion(row.ConfigVersion),
        row.Context,
        row.Score,
        row.WeightingSchemeId,
        row.Aggregation,
        CanonicalJson.Deserialize<CategoryContribution[]>(row.Contributing),
        CanonicalJson.Deserialize<AbsentCategoryView[]>(row.Absent),
        Guid.Parse(row.TriggerSignalId),
        Guid.Parse(row.TriggerAssessmentId),
        DateTimeOffset.FromUnixTimeMilliseconds(row.AsOfMs),
        DateTimeOffset.FromUnixTimeMilliseconds(row.ComputedAtMs));

    public static DislocationView From(DislocationRow row) => new(
        Guid.Parse(row.DislocationId),
        row.Sequence,
        new CorrelationId(Guid.Parse(row.CorrelationId)),
        new ConfigVersion(row.ConfigVersion),
        row.Context,
        Guid.Parse(row.CompositeId),
        row.CompositeScore,
        row.ReferenceInstrument,
        row.MarketObservedIv,
        DateTimeOffset.FromUnixTimeMilliseconds(row.IvObservedAtMs),
        Guid.Parse(row.IvSignalId),
        row.Regime,
        row.RegimePercentile,
        row.RegimeHistory,
        row.SensitivityFactor,
        row.SignalImpliedIv,
        row.DislocationValue,
        row.Threshold,
        row.ThresholdBreached,
        DateTimeOffset.FromUnixTimeMilliseconds(row.AsOfMs),
        DateTimeOffset.FromUnixTimeMilliseconds(row.ComputedAtMs));
}
