using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PrimeScore.Ledger;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Storage;

/// <summary>
/// Maintains <c>cat_events</c> inside each append's transaction. A reschedule inserts the next
/// vintage and clears <c>is_current</c> on the one it replaces; the (id, vintage) key makes a
/// second writer of the same vintage fail the whole append instead of forking the history.
/// </summary>
internal sealed class CatalystProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind is LedgerKinds.CatalystScheduled or LedgerKinds.CatalystRescheduled;

    public async Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scope);
        var context = scope.Context<CatalystsDbContext>(CatalystsDbContext.HistoryTable, CatalystsDbContext.Create);
        if (record.Kind == LedgerKinds.CatalystScheduled)
        {
            var payload = record.PayloadAs<CatalystScheduledPayload>();
            context.Events.Add(new CatalystEventRow
            {
                CatalystId = payload.CatalystId,
                Vintage = 1,
                Family = payload.Family.ToWireName(),
                Title = payload.Title,
                ReferencePeriod = payload.ReferencePeriod,
                SourceKey = payload.SourceKey,
                FirstAnnouncedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
                Backfilled = payload.Backfilled,
            }.Apply(record, payload.ScheduledAt, payload.TimeAnnounced, payload.Status, payload.Sep, payload.Tentative, change: null,
                payload.Adapter, payload.SourceKind, payload.SourceUrl, payload.RetrievedAt, payload.FileSha256, payload.Derivation));
            return;
        }

        var next = record.PayloadAs<CatalystRescheduledPayload>();
        var current = await context.Events
            .SingleOrDefaultAsync(row => row.CatalystId == next.CatalystId && row.IsCurrent, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"CatalystRescheduled for unknown catalyst {next.CatalystId}.");
        if (next.Vintage != current.Vintage + 1)
        {
            throw new InvalidOperationException($"CatalystRescheduled vintage {next.Vintage} does not follow vintage {current.Vintage} of {next.CatalystId}.");
        }

        current.IsCurrent = false;
        context.Events.Add(new CatalystEventRow
        {
            CatalystId = current.CatalystId,
            Vintage = next.Vintage,
            Family = current.Family,
            Title = current.Title,
            ReferencePeriod = current.ReferencePeriod,
            SourceKey = current.SourceKey,
            FirstAnnouncedAtMs = current.FirstAnnouncedAtMs,
            Backfilled = current.Backfilled,
        }.Apply(record, next.ScheduledAt, next.TimeAnnounced, next.Status, next.Sep, next.Tentative, next.Change,
            next.Adapter, next.SourceKind, next.SourceUrl, next.RetrievedAt, next.FileSha256, next.Derivation));
    }
}

internal static class CatalystEventRows
{
    /// <summary>Fills the vintage's schedule, provenance and ledger columns and marks it current.</summary>
    public static CatalystEventRow Apply(
        this CatalystEventRow row,
        LedgerRecord record,
        DateTimeOffset scheduledAt,
        bool timeAnnounced,
        CatalystStatus status,
        bool? sep,
        bool? tentative,
        CatalystChange? change,
        string adapter,
        CatalystSourceKind kind,
        string sourceUrl,
        DateTimeOffset retrievedAt,
        string? fileSha256,
        string? derivation)
    {
        row.Status = status.ToString();
        row.ScheduledAtMs = scheduledAt.ToUnixTimeMilliseconds();
        row.ScheduledDate = MarketTime.NewYorkDate(scheduledAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        row.TimeAnnounced = timeAnnounced;
        row.Sep = sep;
        row.Tentative = tentative ?? false;
        row.Change = change?.ToString();
        row.CountedReschedule = change is CatalystChange.Moved or CatalystChange.Status;
        row.RecordedAtMs = record.RecordedAt.ToUnixTimeMilliseconds();
        row.Adapter = adapter;
        row.SourceKind = kind.ToString();
        row.SourceUrl = sourceUrl;
        row.RetrievedAtMs = retrievedAt.ToUnixTimeMilliseconds();
        row.FileSha256 = fileSha256;
        row.Derivation = derivation;
        row.IsCurrent = true;
        row.Sequence = record.Sequence;
        row.LedgerHash = record.Hash;
        return row;
    }
}
