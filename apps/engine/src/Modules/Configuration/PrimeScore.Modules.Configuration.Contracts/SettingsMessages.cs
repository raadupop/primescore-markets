using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Configuration.Contracts;

/// <summary>The settings in force now (seeded on first start).</summary>
public sealed record GetActiveSettings : IQuery<SettingsVersion>;

/// <summary>A stored version, or null when it does not exist.</summary>
public sealed record GetSettingsVersion(int Version) : IQuery<SettingsVersion?>;

public sealed record GetSettingsAtSequence(long MaxSequence) : IQuery<SettingsVersion?>;

/// <summary>Stored versions, newest first.</summary>
public sealed record GetSettingsHistory(int Take = 50) : IQuery<IReadOnlyList<SettingsVersion>>;

/// <summary>Validate temporary settings against a fixed stored version, without recording a change.</summary>
public sealed record ResolveReplaySettings(int Version, string? OverridesJson, bool MarkInSample = true) : IQuery<ReplaySettingsResult>;

public sealed record ReplaySettingsResult(EngineSettings? Settings, IReadOnlyList<string> Errors);

/// <summary>
/// The contract's <c>PUT /config/weighting-scheme</c>. A <paramref name="SourceDropoutPenalty"/>
/// replaces the dropout schedule with that one factor (the contract's static <c>d_c</c>).
/// </summary>
public sealed record SetWeightingScheme(WeightingSettings Scheme, double? SourceDropoutPenalty, string ChangedBy) : ICommand<SettingsChangeAck>;

/// <summary>
/// The contract's <c>PUT /config/dislocation-threshold</c>. It applies to the context whose
/// reference instrument is <paramref name="ReferenceInstrument"/>; any other instrument (or
/// none) applies to the default context, which then uses that instrument as its reference.
/// </summary>
/// <param name="SensitivityFactors">Keys <c>low_vol</c>, <c>normal</c>, <c>high_vol</c>; omitted keys keep their value.</param>
/// <param name="RegimeBoundaries">Keys <c>low_vol_upper</c>, <c>high_vol_lower</c>; switches the context to level regimes.</param>
public sealed record SetDislocationSettings(
    double Threshold,
    string? ReferenceInstrument,
    IReadOnlyDictionary<string, double>? SensitivityFactors,
    IReadOnlyDictionary<string, double>? RegimeBoundaries,
    string ChangedBy) : ICommand<SettingsChangeAck>;

/// <summary>
/// The contract's <c>PUT /config/deploy-conditions</c>: each listed condition replaces the one of
/// the same name; conditions not listed keep their values.
/// </summary>
public sealed record SetDeployConditions(IReadOnlyList<DeployCondition> Conditions, string ChangedBy) : ICommand<SettingsChangeAck>;

/// <summary>Replaces the whole settings value (configuration screen); validated like every change.</summary>
public sealed record ReplaceSettings(EngineSettings Settings, string Reason, string ChangedBy, int? ExpectedVersion = null) : ICommand<SettingsChangeAck>;

/// <param name="Version">The new version when accepted; unchanged settings record no version.</param>
public sealed record SettingsChangeAck(bool Accepted, int? Version, IReadOnlyList<string> Errors) : ICommandAck;
