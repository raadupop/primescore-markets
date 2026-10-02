using System.Collections.Frozen;

namespace PrimeScore.Ledger;

/// <summary>The ledger vocabulary (brief §6; v3 adds catalysts). Appends with any other kind are rejected.</summary>
public static class LedgerKinds
{
    public const string SignalIngested = "SignalIngested";
    public const string SignalRejected = "SignalRejected";
    public const string AssessmentRecorded = "AssessmentRecorded";
    public const string AssessmentUnavailable = "AssessmentUnavailable";
    public const string CompositeComputed = "CompositeComputed";
    public const string DislocationComputed = "DislocationComputed";
    public const string DecisionMade = "DecisionMade";
    public const string ConfigurationChanged = "ConfigurationChanged";
    public const string ReplayRun = "ReplayRun";
    public const string CatalystScheduled = "CatalystScheduled";
    public const string CatalystRescheduled = "CatalystRescheduled";

    public static readonly FrozenSet<string> All = new[]
    {
        SignalIngested, SignalRejected, AssessmentRecorded, AssessmentUnavailable, CompositeComputed,
        DislocationComputed, DecisionMade, ConfigurationChanged, ReplayRun, CatalystScheduled, CatalystRescheduled,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The pipeline stage a kind belongs to, as reported by <c>GET /logs</c> (SRS OBS-001).</summary>
    public static string Stage(string kind) => kind switch
    {
        SignalIngested or SignalRejected => "ingestion",
        AssessmentRecorded or AssessmentUnavailable => "classification",
        CompositeComputed => "composite",
        DislocationComputed => "dislocation",
        DecisionMade => "decision",
        ConfigurationChanged => "configuration",
        ReplayRun => "replay",
        CatalystScheduled or CatalystRescheduled => "catalysts",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ledger kind."),
    };
}
