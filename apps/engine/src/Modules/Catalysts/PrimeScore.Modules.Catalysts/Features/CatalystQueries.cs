using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Catalysts.Features;

/// <summary>Current schedules in a time range, ordered by instant then id.</summary>
internal sealed class GetCatalystsHandler(CatalystsReadStore reads, IClock clock) : IQueryHandler<GetCatalysts, CatalystList>
{
    public async Task<CatalystList> HandleAsync(GetCatalysts query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var from = query.From.ToUnixTimeMilliseconds();
        var to = query.To.ToUnixTimeMilliseconds();
        var family = query.Family?.ToWireName();
        await using var db = reads.Open();
        var rows = db.Events.Where(row => row.IsCurrent && row.ScheduledAtMs >= from && row.ScheduledAtMs <= to);
        if (family is not null)
        {
            rows = rows.Where(row => row.Family == family);
        }

        var current = await rows.OrderBy(row => row.ScheduledAtMs).ThenBy(row => row.CatalystId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rescheduled = await CatalystsReadStore.RescheduledIdsAsync(db, family, cancellationToken).ConfigureAwait(false);
        return new CatalystList(current.Select(row => CatalystViews.Current(row, rescheduled.Contains(row.CatalystId))).ToArray(), clock.UtcNow);
    }
}

/// <summary>One catalyst with all its vintages, oldest first; null when the id is unknown.</summary>
internal sealed class GetCatalystHandler(CatalystsReadStore reads) : IQueryHandler<GetCatalyst, CatalystDetail?>
{
    public async Task<CatalystDetail?> HandleAsync(GetCatalyst query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var db = reads.Open();
        var vintages = await db.Events.Where(row => row.CatalystId == query.CatalystId).OrderBy(row => row.Vintage)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (vintages.Count == 0)
        {
            return null;
        }

        var current = vintages.Single(row => row.IsCurrent);
        return new CatalystDetail(
            CatalystViews.Current(current, vintages.Any(row => row.CountedReschedule)),
            vintages.Select(CatalystViews.Vintage).ToArray());
    }
}

internal static class CatalystViews
{
    /// <summary>
    /// A current row as a view. Never-rescheduled is false once any vintage moved the catalyst or
    /// changed its status; unknown for a back-filled first vintage, whose original date the engine
    /// never saw; true otherwise.
    /// </summary>
    public static CatalystView Current(CatalystEventRow row, bool rescheduled) => new(
        row.CatalystId,
        CatalystFamilyNames.Parse(row.Family),
        row.Title,
        row.ReferencePeriod,
        DateTimeOffset.FromUnixTimeMilliseconds(row.ScheduledAtMs),
        Date(row.ScheduledDate),
        row.TimeAnnounced,
        Enum.Parse<CatalystStatus>(row.Status),
        row.Sep,
        row.Tentative,
        row.Vintage,
        rescheduled ? false : row.Backfilled ? null : true,
        row.Backfilled,
        row.Derivation,
        DateTimeOffset.FromUnixTimeMilliseconds(row.FirstAnnouncedAtMs),
        row.ActualAtMs is { } actual ? DateTimeOffset.FromUnixTimeMilliseconds(actual) : null,
        Source(row),
        row.Sequence,
        row.LedgerHash);

    public static CatalystVintageView Vintage(CatalystEventRow row) => new(
        row.Vintage,
        row.Change is { } change ? Enum.Parse<CatalystChange>(change) : null,
        DateTimeOffset.FromUnixTimeMilliseconds(row.ScheduledAtMs),
        Date(row.ScheduledDate),
        row.TimeAnnounced,
        Enum.Parse<CatalystStatus>(row.Status),
        row.Sep,
        row.Tentative,
        row.Derivation,
        DateTimeOffset.FromUnixTimeMilliseconds(row.RecordedAtMs),
        Source(row),
        row.Sequence,
        row.LedgerHash);

    private static CatalystSourceView Source(CatalystEventRow row) => new(
        row.Adapter,
        Enum.Parse<CatalystSourceKind>(row.SourceKind),
        row.SourceUrl,
        DateTimeOffset.FromUnixTimeMilliseconds(row.RetrievedAtMs),
        row.FileSha256);

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
