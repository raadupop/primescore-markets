using PrimeScore.Ledger;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Configuration.Storage;

/// <summary>Ledger payload of <see cref="LedgerKinds.ConfigurationChanged"/>: the whole new value and its diff.</summary>
internal sealed record SettingsChangedPayload(int Version, string ChangedBy, string Reason, EngineSettings Settings, IReadOnlyList<string> Changes);

/// <summary>Maintains <c>cfg_versions</c> inside each append's transaction.</summary>
internal sealed class SettingsProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind == LedgerKinds.ConfigurationChanged;

    public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scope);
        var payload = record.PayloadAs<SettingsChangedPayload>();
        scope.Context<ConfigurationDbContext>(ConfigurationDbContext.HistoryTable, ConfigurationDbContext.Create).Versions.Add(new SettingsRow
        {
            Version = payload.Version,
            Sequence = record.Sequence,
            RecordedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
            ChangedBy = payload.ChangedBy,
            Reason = payload.Reason,
            Settings = CanonicalJson.Serialize(payload.Settings),
            Changes = CanonicalJson.Serialize(payload.Changes),
        });
        return Task.CompletedTask;
    }
}
