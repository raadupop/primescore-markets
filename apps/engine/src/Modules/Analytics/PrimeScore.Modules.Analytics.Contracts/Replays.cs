using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Contracts;

public sealed record RunReplay(string EventLabel, DateTimeOffset From, DateTimeOffset To, string? OverridesJson, string RequestedBy,
    long InputSequence, string InputHash, bool RecordEmpty = false)
    : ICommand<RunReplayAck>;

public sealed record RunReplayAck(Guid? ReplayId, IReadOnlyList<string> Errors) : ICommandAck;

public sealed record GetReplay(Guid ReplayId) : IQuery<ReplayView?>;

public sealed record GetReplayHistory(int Take = 30) : IQuery<IReadOnlyList<ReplayView>>;

public sealed record GetValidationEvents : IQuery<IReadOnlyList<ValidationEvent>>;

public sealed record ValidationEvent(string Id, string Label, DateOnly Date, DateOnly TargetDate, bool ExpectDeploy, string Expected)
{
    public DateTimeOffset From => MarketTime.AtNewYork(MarketTime.AddTradingDays(TargetDate, -1), TimeOnly.MinValue);

    public DateTimeOffset To => MarketTime.AtNewYork(Date, TimeOnly.MaxValue);
}

public sealed record EvaluateValidationEvents(string RequestedBy, long InputSequence, string InputHash) : ICommand<ValidationEvaluationAck>;

public sealed record ValidationEvaluationAck(int Completed) : ICommandAck;

public sealed record GetValidationReport : IQuery<IReadOnlyList<ValidationEventResult>>;

public sealed record ValidationEventResult(ValidationEvent Event, Guid? ReplayId, string Verdict, string Detail);

/// <summary>Recorded replay. DecisionsJson contains canonical DecisionView records; SettingsJson contains the effective EngineSettings.</summary>
public sealed record ReplayView(Guid ReplayId, long LedgerSequence, string EventLabel, DateTimeOffset From, DateTimeOffset To,
    ConfigVersion ConfigVersion, long InputSequence, string InputHash, string SettingsJson, string? OverridesJson,
    string DecisionsJson, int SignalCount, string RequestedBy, DateTimeOffset RecordedAt);
