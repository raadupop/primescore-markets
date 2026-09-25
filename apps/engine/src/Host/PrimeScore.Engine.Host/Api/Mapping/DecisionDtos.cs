using System.Text.Json;
using PrimeScore.Api.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using Module = PrimeScore.Modules.Decision.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>Decisions and their audit entries to the contract, and the deploy-condition body to settings (brief §5 rule 4).</summary>
internal static class DecisionDtos
{
    /// <remarks>
    /// <c>decided_at</c> is the observation time the decision refers to (SIG-004), not when it was
    /// recorded. <c>urgency_tier</c> and <c>requires_approval</c> are left out: DEC-004 is Milestone B.
    /// </remarks>
    public static DecisionRecord From(DecisionView view) => new()
    {
        Decision_id = view.DecisionId,
        Outcome = view.Outcome == Module.DecisionOutcome.Deploy ? PrimeScore.Api.Contracts.DecisionOutcome.DEPLOY : PrimeScore.Api.Contracts.DecisionOutcome.IDLE,
        Composite_score = view.CompositeScore,
        Dislocation_value = view.DislocationValue,
        Conditions_evaluated = view.Conditions.Select(condition => new PrimeScore.Api.Contracts.ConditionEvaluation
        {
            Condition_name = condition.Name,
            Required_value = condition.Required,
            Actual_value = condition.Actual,
            Passed = condition.Passed,
        }).ToList(),
        Top_contributing_signals = view.TopContributing.Select(signal => signal.SignalId).ToList(),
        Dissenting_signals = view.Dissenting.Select(signal => signal.SignalId).ToList(),
        Decided_at = view.AsOf,
        Correlation_id = view.CorrelationId.Value,
    };

    public static AuditEntry From(AuditEntryView view) => new()
    {
        Audit_id = view.AuditId,
        Event_type = view.EventType == Module.AuditEventType.DeployDecision ? PrimeScore.Api.Contracts.AuditEventType.DEPLOY_DECISION : PrimeScore.Api.Contracts.AuditEventType.IDLE_DECISION,
        Entity_id = view.EntityId,
        Input_snapshot = JsonDocument.Parse(view.InputSnapshot).RootElement.Clone(),
        Output_snapshot = JsonDocument.Parse(view.OutputSnapshot).RootElement.Clone(),
        Recorded_at = view.RecordedAt,
        Sequence_number = checked((int)view.SequenceNumber),
    };

    /// <summary>Null for an audit event type v1 never records (approvals and positions are Milestone B).</summary>
    public static Module.AuditEventType? ToModule(PrimeScore.Api.Contracts.AuditEventType type) => type switch
    {
        PrimeScore.Api.Contracts.AuditEventType.DEPLOY_DECISION => Module.AuditEventType.DeployDecision,
        PrimeScore.Api.Contracts.AuditEventType.IDLE_DECISION => Module.AuditEventType.IdleDecision,
        _ => null,
    };

    public static DeployCondition ToSettings(Conditions condition) => new(
        condition.Name?.Trim() ?? "",
        condition.Operator switch
        {
            ConditionsOperator.Ge => ">=",
            ConditionsOperator.Gt => ">",
            ConditionsOperator.Le => "<=",
            ConditionsOperator.Lt => "<",
            ConditionsOperator.__ => "==",
            _ => "?",
        },
        condition.Threshold);
}
