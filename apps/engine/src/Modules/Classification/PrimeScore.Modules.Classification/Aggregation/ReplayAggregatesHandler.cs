using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Storage;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Json;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Classification.Aggregation;

internal sealed class GetReplayAggregatesHandler(
    ClassificationReadStore reads,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    IndicatorRegistry registry) : IQueryHandler<GetReplayAggregates, IReadOnlyList<AggregateRecord>>
{
    public async Task<IReadOnlyList<AggregateRecord>> HandleAsync(GetReplayAggregates query, CancellationToken cancellationToken)
    {
        var settings = CanonicalJson.Deserialize<EngineSettings>(query.SettingsJson);
        var output = new List<AggregateRecord>();
        foreach (var context in settings.Contexts)
        {
            var parameters = CompositeRunner.Parameters(settings, context);
            var earliest = parameters.Expected.Min(category => CompositeCalculator.WindowStart(query.From, parameters.Windows[category]));
            await using var db = reads.Open();
            var from = earliest.ToUnixTimeMilliseconds();
            var to = query.To.ToUnixTimeMilliseconds();
            var rows = await db.Assessments.AsNoTracking()
                .Where(row => row.ObservedAtMs >= from && row.ObservedAtMs <= to && row.Sequence <= query.MaxSequence)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            var latest = rows.GroupBy(row => row.SignalId, StringComparer.Ordinal)
                .Select(group => group.MaxBy(row => row.Sequence)!)
                .Where(row => context.Includes(Enum.Parse<SourceCategory>(row.Category), row.Instrument))
                .OrderBy(row => row.ObservedAtMs).ThenBy(row => row.Sequence).ToArray();
            var reference = await series.HandleAsync(new GetObservationSeries(context.ReferenceInstrument, SourceCategory.MarketData,
                "IMPLIED_VOLATILITY", query.To.AddMilliseconds(1), 1_000_000, query.MaxSequence), cancellationToken).ConfigureAwait(false);
            var length = registry.TryGetSymbol(context.ReferenceInstrument, out var symbol) ? symbol.IndicatorClass.ReferenceWindowLength : 1260;
            foreach (var trigger in latest.Where(row => row.Available && row.ObservedAtMs >= query.From.ToUnixTimeMilliseconds()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var at = DateTimeOffset.FromUnixTimeMilliseconds(trigger.ObservedAtMs);
                var past = latest.Where(row => row.ObservedAtMs <= trigger.ObservedAtMs).ToArray();
                var available = past.Where(row => row.Available).Select(Input).ToArray();
                var unavailable = past.Where(row => !row.Available).Select(row => new OutcomeInput(Enum.Parse<SourceCategory>(row.Category),
                    DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs), CompositeRunner.ReasonLabel(row.Reason))).ToArray();
                var result = CompositeCalculator.Compute(at, parameters, available, unavailable);
                var points = reference.Points.Where(point => point.ObservedAt <= at).TakeLast(length + 1).ToArray();
                if (points.Length == 0)
                {
                    continue;
                }

                var level = points[^1];
                var dislocation = DislocationCalculator.Compute(result.Score, level.Value,
                    points.Take(points.Length - 1).Select(point => point.Value).ToArray(), CompositeRunner.Dislocation(context));
                var correlation = new CorrelationId(Guid.Parse(trigger.CorrelationId));
                var composite = new CompositeView(Guid.NewGuid(), 0, correlation, query.Version, context.Name, result.Score,
                    settings.Weighting.SchemeId, settings.Weighting.Aggregation.ToString(), result.Contributing.Select(CompositeRunner.View).ToArray(),
                    result.Absent.Select(item => new AbsentCategoryView(item.Category, item.Reason)).ToArray(),
                    Guid.Parse(trigger.SignalId), Guid.Parse(trigger.AssessmentId), at, at);
                var view = new DislocationView(Guid.NewGuid(), 0, correlation, query.Version, context.Name, composite.CompositeId,
                    result.Score, context.ReferenceInstrument, level.Value, level.ObservedAt, level.SignalId, dislocation.Regime,
                    dislocation.RegimePercentile, dislocation.RegimeHistory, dislocation.SensitivityFactor,
                    dislocation.SignalImpliedIv, dislocation.DislocationValue, dislocation.Threshold, dislocation.ThresholdBreached, at, at);
                var confirmedIds = result.Contributing.SelectMany(item => item.Confirmed).ToHashSet();
                var confirmed = past.Where(row => confirmedIds.Contains(Guid.Parse(row.AssessmentId))).Select(row =>
                    new ConfirmedAssessment(Guid.Parse(row.AssessmentId), Guid.Parse(row.SignalId), row.Sequence,
                        Enum.Parse<SourceCategory>(row.Category), row.Instrument, DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs),
                        row.Score!.Value, row.Certainty!.Value, row.IsFallback)).ToArray();
                output.Add(new AggregateRecord(composite, view, confirmed));
            }
        }

        return output.OrderBy(item => item.Composite.AsOf).ThenBy(item => item.Composite.Context, StringComparer.Ordinal).ToArray();
    }

    private static AssessmentInput Input(AssessmentRow row) => new(Guid.Parse(row.AssessmentId), Guid.Parse(row.SignalId),
        Enum.Parse<SourceCategory>(row.Category), row.Instrument, DateTimeOffset.FromUnixTimeMilliseconds(row.ObservedAtMs),
        row.Score!.Value, row.Certainty!.Value, row.IsFallback);
}
