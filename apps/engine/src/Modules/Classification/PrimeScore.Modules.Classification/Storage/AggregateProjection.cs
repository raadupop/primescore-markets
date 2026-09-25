using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Classification.Storage;

/// <summary>Ledger payload of <see cref="LedgerKinds.CompositeComputed"/> (SRS CLS-002, ADR-0004).</summary>
/// <param name="AsOf">Observation time of the triggering signal; only assessments observed at or before it take part.</param>
internal sealed record CompositeComputedPayload(
    Guid CompositeId,
    string Context,
    Guid TriggerAssessmentId,
    Guid TriggerSignalId,
    DateTimeOffset AsOf,
    double Score,
    string WeightingSchemeId,
    string Aggregation,
    IReadOnlyList<CategoryContribution> Contributing,
    IReadOnlyList<AbsentCategoryView> Absent);

/// <summary>Ledger payload of <see cref="LedgerKinds.DislocationComputed"/> (SRS CLS-006, ADR-0004 §6).</summary>
internal sealed record DislocationComputedPayload(
    Guid DislocationId,
    string Context,
    Guid CompositeId,
    DateTimeOffset AsOf,
    double CompositeScore,
    string ReferenceInstrument,
    double MarketObservedIv,
    DateTimeOffset IvObservedAt,
    Guid IvSignalId,
    string Regime,
    double? RegimePercentile,
    int RegimeHistory,
    double SensitivityFactor,
    double SignalImpliedIv,
    double DislocationValue,
    double Threshold,
    bool ThresholdBreached);

/// <summary>Maintains <c>cls_composites</c> and <c>cls_dislocations</c> inside each append's transaction.</summary>
internal sealed class AggregateProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind is LedgerKinds.CompositeComputed or LedgerKinds.DislocationComputed;

    public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scope);
        var context = scope.Context<ClassificationDbContext>(ClassificationDbContext.HistoryTable, ClassificationDbContext.Create);
        if (record.Kind == LedgerKinds.CompositeComputed)
        {
            var composite = record.PayloadAs<CompositeComputedPayload>();
            context.Composites.Add(new CompositeRow
            {
                CompositeId = composite.CompositeId.ToString("D"),
                Sequence = record.Sequence,
                CorrelationId = record.CorrelationId.ToString(),
                ConfigVersion = record.ConfigVersion.Value,
                Context = composite.Context,
                TriggerAssessmentId = composite.TriggerAssessmentId.ToString("D"),
                TriggerSignalId = composite.TriggerSignalId.ToString("D"),
                AsOfMs = composite.AsOf.ToUnixTimeMilliseconds(),
                ComputedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
                Score = composite.Score,
                WeightingSchemeId = composite.WeightingSchemeId,
                Aggregation = composite.Aggregation,
                Contributing = CanonicalJson.Serialize(composite.Contributing),
                Absent = CanonicalJson.Serialize(composite.Absent),
            });
        }
        else
        {
            var dislocation = record.PayloadAs<DislocationComputedPayload>();
            context.Dislocations.Add(new DislocationRow
            {
                DislocationId = dislocation.DislocationId.ToString("D"),
                Sequence = record.Sequence,
                CorrelationId = record.CorrelationId.ToString(),
                ConfigVersion = record.ConfigVersion.Value,
                Context = dislocation.Context,
                CompositeId = dislocation.CompositeId.ToString("D"),
                AsOfMs = dislocation.AsOf.ToUnixTimeMilliseconds(),
                ComputedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
                CompositeScore = dislocation.CompositeScore,
                ReferenceInstrument = dislocation.ReferenceInstrument,
                MarketObservedIv = dislocation.MarketObservedIv,
                IvObservedAtMs = dislocation.IvObservedAt.ToUnixTimeMilliseconds(),
                IvSignalId = dislocation.IvSignalId.ToString("D"),
                Regime = dislocation.Regime,
                RegimePercentile = dislocation.RegimePercentile,
                RegimeHistory = dislocation.RegimeHistory,
                SensitivityFactor = dislocation.SensitivityFactor,
                SignalImpliedIv = dislocation.SignalImpliedIv,
                DislocationValue = dislocation.DislocationValue,
                Threshold = dislocation.Threshold,
                ThresholdBreached = dislocation.ThresholdBreached,
            });
        }

        return Task.CompletedTask;
    }
}

internal sealed class CompositeRow
{
    public string CompositeId { get; set; } = "";

    public long Sequence { get; set; }

    public string CorrelationId { get; set; } = "";

    public int ConfigVersion { get; set; }

    public string Context { get; set; } = "";

    public string TriggerAssessmentId { get; set; } = "";

    public string TriggerSignalId { get; set; } = "";

    public long AsOfMs { get; set; }

    public long ComputedAtMs { get; set; }

    public double Score { get; set; }

    public string WeightingSchemeId { get; set; } = "";

    public string Aggregation { get; set; } = "";

    public string Contributing { get; set; } = "[]";

    public string Absent { get; set; } = "[]";
}

internal sealed class DislocationRow
{
    public string DislocationId { get; set; } = "";

    public long Sequence { get; set; }

    public string CorrelationId { get; set; } = "";

    public int ConfigVersion { get; set; }

    public string Context { get; set; } = "";

    public string CompositeId { get; set; } = "";

    public long AsOfMs { get; set; }

    public long ComputedAtMs { get; set; }

    public double CompositeScore { get; set; }

    public string ReferenceInstrument { get; set; } = "";

    public double MarketObservedIv { get; set; }

    public long IvObservedAtMs { get; set; }

    public string IvSignalId { get; set; } = "";

    public string Regime { get; set; } = "";

    public double? RegimePercentile { get; set; }

    public int RegimeHistory { get; set; }

    public double SensitivityFactor { get; set; }

    public double SignalImpliedIv { get; set; }

    public double DislocationValue { get; set; }

    public double Threshold { get; set; }

    public bool ThresholdBreached { get; set; }
}
