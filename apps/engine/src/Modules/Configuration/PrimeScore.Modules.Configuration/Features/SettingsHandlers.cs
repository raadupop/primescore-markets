using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Configuration.Settings;
using PrimeScore.Modules.Configuration.Storage;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Configuration.Features;

internal sealed class GetActiveSettingsHandler(SettingsWriter settings) : IQueryHandler<GetActiveSettings, SettingsVersion>
{
    public Task<SettingsVersion> HandleAsync(GetActiveSettings query, CancellationToken cancellationToken) =>
        settings.ActiveAsync(cancellationToken);
}

internal sealed class GetSettingsVersionHandler(ConfigurationReadStore reads) : IQueryHandler<GetSettingsVersion, SettingsVersion?>
{
    public async Task<SettingsVersion?> HandleAsync(GetSettingsVersion query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = reads.Open();
        var row = await context.Versions.SingleOrDefaultAsync(version => version.Version == query.Version, cancellationToken).ConfigureAwait(false);
        return row is null ? null : SettingsRows.ToVersion(row);
    }
}

internal sealed class GetSettingsHistoryHandler(ConfigurationReadStore reads) : IQueryHandler<GetSettingsHistory, IReadOnlyList<SettingsVersion>>
{
    public async Task<IReadOnlyList<SettingsVersion>> HandleAsync(GetSettingsHistory query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var context = reads.Open();
        var rows = await context.Versions.OrderByDescending(version => version.Version)
            .Take(Math.Clamp(query.Take, 1, 500))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(SettingsRows.ToVersion).ToArray();
    }
}

/// <summary>The contract's <c>PUT /config/weighting-scheme</c> (SRS CLS-002, NFR-003).</summary>
internal sealed class SetWeightingSchemeHandler(SettingsWriter settings) : ICommandHandler<SetWeightingScheme, SettingsChangeAck>
{
    public Task<SettingsChangeAck> HandleAsync(SetWeightingScheme command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return settings.ChangeAsync(
            current =>
            {
                var next = current with { Weighting = command.Scheme };
                if (command.SourceDropoutPenalty is { } penalty)
                {
                    // The contract's static d_c: one factor for any staleness above zero.
                    next = next with { DropoutSchedule = [new DropoutTier(null, penalty)] };
                }

                return (next, []);
            },
            $"Weighting scheme '{command.Scheme.SchemeId}' set through the API",
            command.ChangedBy,
            cancellationToken);
    }
}

/// <summary>The contract's <c>PUT /config/dislocation-threshold</c> (SRS CLS-006).</summary>
internal sealed class SetDislocationSettingsHandler(SettingsWriter settings) : ICommandHandler<SetDislocationSettings, SettingsChangeAck>
{
    private static readonly string[] SensitivityKeys = ["low_vol", "normal", "high_vol"];
    private static readonly string[] BoundaryKeys = ["low_vol_upper", "high_vol_lower"];

    public Task<SettingsChangeAck> HandleAsync(SetDislocationSettings command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return settings.ChangeAsync(
            current =>
            {
                var errors = new List<string>();
                // One normalised value for both the match and the write: " ovx " selects the oil context.
                var reference = string.IsNullOrWhiteSpace(command.ReferenceInstrument) ? null : command.ReferenceInstrument.Trim().ToUpperInvariant();
                var target = reference is null
                    ? null
                    : current.Contexts.FirstOrDefault(context => string.Equals(context.ReferenceInstrument, reference, StringComparison.OrdinalIgnoreCase));
                target ??= current.Contexts[0];

                var sensitivity = target.Sensitivity;
                foreach (var (key, value) in command.SensitivityFactors ?? new Dictionary<string, double>())
                {
                    sensitivity = key switch
                    {
                        "low_vol" => sensitivity with { LowVol = value },
                        "normal" => sensitivity with { Normal = value },
                        "high_vol" => sensitivity with { HighVol = value },
                        _ => Unknown(errors, $"sensitivity_factor_map.{key}", SensitivityKeys, sensitivity),
                    };
                }

                var regime = target.Regime;
                if (command.RegimeBoundaries is { } boundaries)
                {
                    foreach (var key in boundaries.Keys.Where(key => !BoundaryKeys.Contains(key, StringComparer.Ordinal)))
                    {
                        errors.Add($"regime_boundaries.{key}: unknown key; expected {string.Join(", ", BoundaryKeys)}");
                    }

                    if (!boundaries.TryGetValue("low_vol_upper", out var low) || !boundaries.TryGetValue("high_vol_lower", out var high))
                    {
                        errors.Add("regime_boundaries: both low_vol_upper and high_vol_lower are required");
                    }
                    else
                    {
                        regime = regime with { Mode = RegimeMode.Level, LowVolUpper = low, HighVolLower = high };
                    }
                }

                var updated = target with
                {
                    ReferenceInstrument = reference ?? target.ReferenceInstrument,
                    DislocationThreshold = command.Threshold,
                    Sensitivity = sensitivity,
                    Regime = regime,
                };
                var contexts = current.Contexts.Select(context => ReferenceEquals(context, target) ? updated : context).ToArray();
                return (current with { Contexts = contexts }, errors);
            },
            $"Dislocation threshold set through the API (reference {command.ReferenceInstrument ?? "of the default context"})",
            command.ChangedBy,
            cancellationToken);
    }

    private static SensitivityMap Unknown(List<string> errors, string path, string[] expected, SensitivityMap unchanged)
    {
        errors.Add($"{path}: unknown key; expected {string.Join(", ", expected)}");
        return unchanged;
    }
}

/// <summary>The contract's <c>PUT /config/deploy-conditions</c> (SRS DEC-001, NFR-003).</summary>
internal sealed class SetDeployConditionsHandler(SettingsWriter settings) : ICommandHandler<SetDeployConditions, SettingsChangeAck>
{
    public Task<SettingsChangeAck> HandleAsync(SetDeployConditions command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return settings.ChangeAsync(
            current =>
            {
                var errors = new List<string>();
                if (command.Conditions.Count == 0)
                {
                    errors.Add("conditions: at least one condition is required");
                }

                foreach (var condition in command.Conditions)
                {
                    var message = (condition.Name ?? "").Trim() switch
                    {
                        DeployConditionNames.Dislocation =>
                            "the dislocation threshold is set per context through PUT /config/dislocation-threshold",
                        DeployConditionNames.Cooldown or "risk_budget" or "cooldown" =>
                            "cooldowns and risk budgets are Milestone B (SRS RSK-001); v1 always evaluates no active cooldown",
                        _ => null,
                    };
                    if (message is not null)
                    {
                        errors.Add($"conditions.{condition.Name}: {message}");
                    }
                }

                if (errors.Count > 0)
                {
                    return (null, errors);
                }

                var updated = (current.DeployConditions ?? []).ToList();
                foreach (var condition in command.Conditions)
                {
                    var trimmed = condition with { Name = (condition.Name ?? "").Trim(), Operator = (condition.Operator ?? "").Trim() };
                    updated.RemoveAll(existing => existing.Name == trimmed.Name);
                    updated.Add(trimmed);
                }

                var ordered = updated
                    .OrderBy(condition => DeployConditionNames.Configurable.ToList().IndexOf(condition.Name) is var index and >= 0 ? index : int.MaxValue)
                    .ToArray();
                return (current with { DeployConditions = ordered }, errors);
            },
            "Deploy conditions set through the API",
            command.ChangedBy,
            cancellationToken);
    }
}

internal sealed class ReplaceSettingsHandler(SettingsWriter settings) : ICommandHandler<ReplaceSettings, SettingsChangeAck>
{
    public Task<SettingsChangeAck> HandleAsync(ReplaceSettings command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return settings.ChangeAsync(_ => (command.Settings, []), command.Reason, command.ChangedBy, cancellationToken);
    }
}
