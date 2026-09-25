using System.Text.Json;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Classification.Pipeline;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Classification.Storage;

/// <summary>Ledger payload of <see cref="LedgerKinds.AssessmentRecorded"/>: the classifier answer and what it was computed on.</summary>
/// <param name="FallbackOf">For a CLS-004 fallback, the assessment whose values were reused.</param>
/// <param name="FailureReason">For a fallback, why the classifier could not answer this time.</param>
/// <param name="MacroWindow">For a macro print, the earlier surprises and consensus rows its window was built from.</param>
/// <param name="Variant">The signal's series within its instrument (metric type, indicator); fallbacks reuse only the same series.</param>
internal sealed record AssessmentRecordedPayload(
    Guid AssessmentId,
    Guid SignalId,
    SourceCategory Category,
    string Instrument,
    string Variant,
    DateTimeOffset ObservedAt,
    double Score,
    string ScoreType,
    double Certainty,
    double? HistorySufficiency,
    double? TemporalRelevance,
    string ClassificationMethod,
    string? EventTaxonomy,
    string ReasoningTrace,
    JsonElement ComputedMetrics,
    IReadOnlyList<string> Flags,
    bool IsFallback,
    double? StalenessSeconds,
    Guid? FallbackOf,
    UnavailableReason? FailureReason,
    string? FailureDetail,
    int ReferenceWindowLength,
    DateTimeOffset? ReferenceWindowLastUpdate,
    ConsensusUsed? Consensus,
    IReadOnlyList<MacroSurprise>? MacroWindow);

/// <summary>Ledger payload of <see cref="LedgerKinds.AssessmentUnavailable"/>: why a signal has no assessment.</summary>
internal sealed record AssessmentUnavailablePayload(
    Guid AssessmentId,
    Guid SignalId,
    SourceCategory Category,
    string Instrument,
    string Variant,
    DateTimeOffset ObservedAt,
    UnavailableReason Reason,
    int? HttpStatus,
    string Detail);

/// <summary>Maintains <c>cls_assessments</c> inside each append's transaction.</summary>
internal sealed class AssessmentProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind is LedgerKinds.AssessmentRecorded or LedgerKinds.AssessmentUnavailable;

    public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scope);
        var context = scope.Context<ClassificationDbContext>(ClassificationDbContext.HistoryTable, ClassificationDbContext.Create);
        context.Assessments.Add(record.Kind == LedgerKinds.AssessmentRecorded
            ? Recorded(record, record.PayloadAs<AssessmentRecordedPayload>())
            : Unavailable(record, record.PayloadAs<AssessmentUnavailablePayload>()));
        return Task.CompletedTask;
    }

    private static AssessmentRow Recorded(LedgerRecord record, AssessmentRecordedPayload payload) => new()
    {
        AssessmentId = payload.AssessmentId.ToString("D"),
        Sequence = record.Sequence,
        SignalId = payload.SignalId.ToString("D"),
        CorrelationId = record.CorrelationId.ToString(),
        Category = payload.Category.ToString(),
        Instrument = payload.Instrument,
        Variant = payload.Variant,
        ObservedAtMs = payload.ObservedAt.ToUnixTimeMilliseconds(),
        AssessedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
        Available = true,
        Reason = payload.FailureReason?.ToString(),
        Detail = payload.FailureDetail,
        Score = payload.Score,
        ScoreType = payload.ScoreType,
        Certainty = payload.Certainty,
        HistorySufficiency = payload.HistorySufficiency,
        TemporalRelevance = payload.TemporalRelevance,
        ClassificationMethod = payload.ClassificationMethod,
        EventTaxonomy = payload.EventTaxonomy,
        IsFallback = payload.IsFallback,
        StalenessSeconds = payload.StalenessSeconds,
        FallbackOf = payload.FallbackOf?.ToString("D"),
        Flags = CanonicalJson.Serialize(payload.Flags),
        ReasoningTrace = payload.ReasoningTrace,
        ComputedMetrics = payload.ComputedMetrics.GetRawText(),
        ReferenceWindowLength = payload.ReferenceWindowLength,
        Consensus = payload.Consensus is null ? null : CanonicalJson.Serialize(payload.Consensus),
    };

    private static AssessmentRow Unavailable(LedgerRecord record, AssessmentUnavailablePayload payload) => new()
    {
        AssessmentId = payload.AssessmentId.ToString("D"),
        Sequence = record.Sequence,
        SignalId = payload.SignalId.ToString("D"),
        CorrelationId = record.CorrelationId.ToString(),
        Category = payload.Category.ToString(),
        Instrument = payload.Instrument,
        Variant = payload.Variant,
        ObservedAtMs = payload.ObservedAt.ToUnixTimeMilliseconds(),
        AssessedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
        Available = false,
        Reason = payload.Reason.ToString(),
        Detail = payload.Detail,
        HttpStatus = payload.HttpStatus,
        Flags = "[]",
    };
}
