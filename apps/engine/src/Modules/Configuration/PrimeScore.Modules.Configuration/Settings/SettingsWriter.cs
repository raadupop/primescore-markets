using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PrimeScore.Ledger;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Configuration.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Configuration.Settings;

/// <summary>
/// The one write path for settings. Changes are serialized, validated, diffed against the
/// version in force and appended as <c>ConfigurationChanged</c> with the new version number;
/// the in-memory copy of the active version is replaced only after the append committed.
/// </summary>
internal sealed class SettingsWriter(ILedger ledger, ConfigurationReadStore reads) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SettingsVersion? _active;

    public async Task<SettingsVersion> ActiveAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _active) is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _active ??= await LoadLatestAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No configuration version exists; the configuration schema seeds one on start.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SeedAsync(EngineSettings defaults, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await LoadLatestAsync(cancellationToken).ConfigureAwait(false) is { } existing)
            {
                _active = existing;
                return;
            }

            _active = await AppendAsync(null, defaults, "Seeded defaults on first start (brief §6, ADR-0004)", "engine", cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SettingsChangeAck> ChangeAsync(
        Func<EngineSettings, (EngineSettings? Settings, IReadOnlyList<string> Errors)> change,
        string reason,
        string changedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = _active ??= await LoadLatestAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No configuration version exists.");
            var (next, errors) = change(current.Settings);
            if (next is null || errors.Count > 0)
            {
                return new SettingsChangeAck(false, null, errors.Count > 0 ? errors : ["settings: the change could not be applied"]);
            }

            var problems = SettingsValidator.Validate(next);
            if (problems.Count > 0)
            {
                return new SettingsChangeAck(false, null, problems);
            }

            if (SettingsDiff.Between(current.Settings, next).Count == 0)
            {
                return new SettingsChangeAck(true, null, []);
            }

            _active = await AppendAsync(current, next, reason, changedBy, cancellationToken).ConfigureAwait(false);
            return new SettingsChangeAck(true, _active.Version.Value, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<SettingsVersion> AppendAsync(SettingsVersion? current, EngineSettings next, string reason, string changedBy, CancellationToken cancellationToken)
    {
        var version = current is null ? new ConfigVersion(1) : current.Version.Next();
        var changes = SettingsDiff.Between(current?.Settings, next);
        var summary = string.Create(CultureInfo.InvariantCulture,
            $"Configuration version {version.Value} by {Printable(changedBy)}: {Printable(reason)} ({changes.Count} change(s))");
        var records = await ledger.AppendAsync(
            [LedgerAppend.Create(LedgerKinds.ConfigurationChanged, Guid.NewGuid(), CorrelationId.New(), version, summary,
                new SettingsChangedPayload(version.Value, changedBy, reason, next, changes))],
            cancellationToken).ConfigureAwait(false);
        var record = records[0];

        // Cache exactly what a restart would load (canonical key order, millisecond time), so one
        // version always computes the same way whether it came from the cache or the database.
        return new SettingsVersion(
            version, record.Sequence, DateTimeOffset.FromUnixTimeMilliseconds(record.RecordedAt.ToUnixTimeMilliseconds()), changedBy, reason,
            CanonicalJson.Deserialize<EngineSettings>(CanonicalJson.Serialize(next)), changes);
    }

    private async Task<SettingsVersion?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        await using var context = reads.Open();
        var row = await context.Versions.OrderByDescending(version => version.Version).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return row is null ? null : SettingsRows.ToVersion(row);
    }

    private static string Printable(string text) => new(text.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
}

internal static class SettingsRows
{
    public static SettingsVersion ToVersion(SettingsRow row) => new(
        new ConfigVersion(row.Version),
        row.Sequence,
        DateTimeOffset.FromUnixTimeMilliseconds(row.RecordedAtMs),
        row.ChangedBy,
        row.Reason,
        CanonicalJson.Deserialize<EngineSettings>(row.Settings),
        CanonicalJson.Deserialize<string[]>(row.Changes));
}
