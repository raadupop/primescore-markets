using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrimeScore.Ledger;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Storage;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Recording;

/// <summary>
/// The one write path for catalyst schedules (ADR-0011). Recording is idempotent: a schedule seen
/// again writes nothing. A changed schedule appends a new vintage, never an update, and only when
/// its source kind is at least the current vintage's (or an import is forced). Snapshots of one call
/// are applied in the order given against a working set that already holds the entries planned
/// before them, and every entry of the call goes into one append. Calls are serialized here so the
/// comparison and the append cannot interleave; another process writing the same vintage fails the
/// append on the (id, vintage) key, and the call is planned again once.
/// </summary>
internal sealed class CatalystRecorder(ILedger ledger, CatalystsReadStore reads, IClock clock) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<RecordTally> RecordAsync(IReadOnlyList<CalendarSnapshot> snapshots, bool force, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var families = snapshots.SelectMany(snapshot => snapshot.Families.Concat(snapshot.Rows.Select(row => row.Family))).ToHashSet();
                var plan = new Plan(await LoadAsync(families, cancellationToken).ConfigureAwait(false), clock.UtcNow, force);
                foreach (var snapshot in snapshots)
                {
                    plan.Apply(snapshot);
                }

                if (plan.Appends.Count == 0)
                {
                    return plan.Tally();
                }

                try
                {
                    await ledger.AppendAsync(plan.Appends, cancellationToken).ConfigureAwait(false);
                    return plan.Tally();
                }
                catch (DbUpdateException) when (attempt == 1)
                {
                    // Another process recorded a vintage in between: plan again against what is stored now.
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<List<Tracked>> LoadAsync(IReadOnlyCollection<CatalystFamily> families, CancellationToken cancellationToken)
    {
        var names = families.Select(family => family.ToWireName()).ToArray();
        await using var context = reads.Open();
        var current = await context.Events.Where(row => row.IsCurrent && names.Contains(row.Family))
            .OrderBy(row => row.ScheduledAtMs).ThenBy(row => row.CatalystId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var firstDates = await context.Events.Where(row => row.Vintage == 1 && names.Contains(row.Family))
            .Select(row => new { row.CatalystId, row.ScheduledDate })
            .ToDictionaryAsync(row => row.CatalystId, row => row.ScheduledDate, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        return current.Select(row => new Tracked
        {
            Id = row.CatalystId,
            Family = CatalystFamilyNames.Parse(row.Family),
            SourceKey = row.SourceKey,
            ScheduledAt = DateTimeOffset.FromUnixTimeMilliseconds(row.ScheduledAtMs),
            TimeAnnounced = row.TimeAnnounced,
            Status = Enum.Parse<CatalystStatus>(row.Status),
            Sep = row.Sep,
            Tentative = row.Tentative,
            Kind = Enum.Parse<CatalystSourceKind>(row.SourceKind),
            Adapter = row.Adapter,
            Vintage = row.Vintage,
            FirstDate = DateOnly.ParseExact(firstDates[row.CatalystId], "yyyy-MM-dd", CultureInfo.InvariantCulture),
        }).ToList();
    }

    /// <summary>The current state of one catalyst, stored or planned in this call.</summary>
    private sealed class Tracked
    {
        public required string Id { get; init; }

        public required CatalystFamily Family { get; init; }

        public required string? SourceKey { get; init; }

        public required DateTimeOffset ScheduledAt { get; set; }

        public required bool TimeAnnounced { get; set; }

        public required CatalystStatus Status { get; set; }

        public required bool? Sep { get; set; }

        public required bool Tentative { get; set; }

        public required CatalystSourceKind Kind { get; set; }

        public required string Adapter { get; set; }

        public required int Vintage { get; set; }

        /// <summary>New York date of vintage 1.</summary>
        public required DateOnly FirstDate { get; init; }

        public DateOnly Date => MarketTime.NewYorkDate(ScheduledAt);
    }

    /// <summary>The entries one call will append, decided row by row against the working set.</summary>
    private sealed class Plan(List<Tracked> working, DateTimeOffset now, bool force)
    {
        private readonly CorrelationId _correlation = CorrelationId.New();
        private readonly HashSet<string> _ids = working.Select(tracked => tracked.Id).ToHashSet(StringComparer.Ordinal);
        private readonly List<string> _flags = [];
        private int _scheduled;
        private int _rescheduled;
        private int _unchanged;

        public List<LedgerAppend> Appends { get; } = [];

        public RecordTally Tally() => new(_scheduled, _rescheduled, _unchanged, [.. _flags]);

        public void Apply(CalendarSnapshot snapshot)
        {
            var matched = new HashSet<Tracked>(ReferenceEqualityComparer.Instance);
            var listedDates = snapshot.Rows.Where(row => row.Placeholder is null)
                .Select(row => (row.Family, MarketTime.NewYorkDate(row.ScheduledAt))).ToHashSet();

            // Placeholders carry no instant; they sort first and only mark their catalyst as listed.
            foreach (var row in snapshot.Rows.OrderBy(row => row.Placeholder is null).ThenBy(row => row.ScheduledAt))
            {
                if (row.Placeholder is not null)
                {
                    if (row.SourceKey is not null && ByKey(row.Family, row.SourceKey) is { } withdrawn)
                    {
                        matched.Add(withdrawn);
                        _flags.Add($"{withdrawn.Id} date withdrawn by {snapshot.Adapter} ('{row.Placeholder}')");
                    }

                    continue;
                }

                var target = Match(row, snapshot, matched, listedDates);
                if (target is null)
                {
                    if (row.Status == CatalystStatus.Scheduled)
                    {
                        matched.Add(Schedule(row, snapshot));
                    }

                    // A cancelled meeting the engine never saw scheduled is not a catalyst.
                    continue;
                }

                if (!matched.Add(target))
                {
                    // One catalyst per family and New York date: a second row for it in the same source is not a second event.
                    if (Differs(target, row))
                    {
                        _flags.Add($"{target.Id}: {snapshot.Adapter} lists it twice; the first row is used");
                    }

                    _unchanged++;
                    continue;
                }

                if (row.OriginallyScheduledDate is { } original && original != target.Date
                    && target.FirstDate == target.Date && target.Date == MarketTime.NewYorkDate(row.ScheduledAt))
                {
                    _flags.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{target.Id}: originally scheduled {original:yyyy-MM-dd} given, but the catalyst was first recorded at its final date; never_rescheduled stays unknown"));
                }

                if (!Differs(target, row))
                {
                    _unchanged++;
                    continue;
                }

                var outranks = snapshot.Kind >= target.Kind;
                if (!outranks && !force)
                {
                    _flags.Add($"{target.Id}: {snapshot.Adapter} lists {Describe(row.ScheduledAt, row.TimeAnnounced)}; current vintage from {target.Adapter} ({target.Kind}) kept");
                    _unchanged++;
                    continue;
                }

                Reschedule(target, row, snapshot, Classify(target, row, snapshot.Kind), forced: !outranks);
            }

            if (snapshot is { CoverageFrom: { } from, CoverageTo: { } to })
            {
                _flags.AddRange(working
                    .Where(tracked => snapshot.Families.Contains(tracked.Family) && tracked.Adapter == snapshot.Adapter
                        && !matched.Contains(tracked) && tracked.Date >= from && tracked.Date <= to)
                    .OrderBy(tracked => tracked.ScheduledAt).ThenBy(tracked => tracked.Id, StringComparer.Ordinal)
                    .Select(tracked => $"{tracked.Id} no longer listed by {snapshot.Adapter}"));
            }
        }

        /// <summary>
        /// Matching (ADR-0011): the same source key; else the same New York date (the scheduled date,
        /// then a curated row's original date); else, for FOMC, the nearest meeting of this adapter
        /// within the proximity window that the source no longer lists at its own date.
        /// </summary>
        private Tracked? Match(ObservedCatalyst row, CalendarSnapshot snapshot, HashSet<Tracked> matched, HashSet<(CatalystFamily, DateOnly)> listedDates)
        {
            if (row.SourceKey is not null && ByKey(row.Family, row.SourceKey) is { } byKey)
            {
                return byKey;
            }

            var date = MarketTime.NewYorkDate(row.ScheduledAt);
            foreach (var candidateDate in row.OriginallyScheduledDate is { } original ? new[] { date, original } : new[] { date })
            {
                var onDate = working.Where(tracked => tracked.Family == row.Family && tracked.Date == candidateDate).ToArray();
                if ((onDate.FirstOrDefault(tracked => !matched.Contains(tracked)) ?? onDate.FirstOrDefault()) is { } byDate)
                {
                    return byDate;
                }
            }

            if (snapshot.ProximityDays <= 0)
            {
                return null;
            }

            return working
                .Where(tracked => tracked.Family == row.Family && tracked.Adapter == snapshot.Adapter && !matched.Contains(tracked)
                    && !listedDates.Contains((tracked.Family, tracked.Date))
                    && Math.Abs(tracked.Date.DayNumber - date.DayNumber) <= snapshot.ProximityDays)
                .OrderBy(tracked => Math.Abs(tracked.Date.DayNumber - date.DayNumber)).ThenBy(tracked => tracked.ScheduledAt)
                .FirstOrDefault();
        }

        private Tracked? ByKey(CatalystFamily family, string key) =>
            working.FirstOrDefault(tracked => tracked.Family == family && string.Equals(tracked.SourceKey, key, StringComparison.Ordinal));

        /// <summary>The fields a vintage is about; titles and derivations are not compared.</summary>
        private static bool Differs(Tracked tracked, ObservedCatalyst row) =>
            row.ScheduledAt != tracked.ScheduledAt
            || row.TimeAnnounced != tracked.TimeAnnounced
            || row.Status != tracked.Status
            || (row.Sep is not null && row.Sep != tracked.Sep)
            || row.Tentative != tracked.Tentative;

        /// <summary>
        /// Why the vintage is appended: a rule-derived schedule replaced by a better source is a
        /// correction; otherwise status, then a move (date, or an announced time), then a first
        /// announced time, then SEP or tentative only.
        /// </summary>
        private static CatalystChange Classify(Tracked tracked, ObservedCatalyst row, CatalystSourceKind kind)
        {
            if (tracked.Kind == CatalystSourceKind.Rule && kind > CatalystSourceKind.Rule)
            {
                return CatalystChange.Correction;
            }

            if (row.Status != tracked.Status)
            {
                return CatalystChange.Status;
            }

            if (MarketTime.NewYorkDate(row.ScheduledAt) != tracked.Date)
            {
                return CatalystChange.Moved;
            }

            if (row.ScheduledAt != tracked.ScheduledAt || row.TimeAnnounced != tracked.TimeAnnounced)
            {
                return tracked.TimeAnnounced ? CatalystChange.Moved : CatalystChange.TimeAnnounced;
            }

            return CatalystChange.Detail;
        }

        private Tracked Schedule(ObservedCatalyst row, CalendarSnapshot snapshot)
        {
            // A curated row with a known move records its original date first, so the move is a counted reschedule.
            var firstAt = row.OriginallyScheduledDate is { } original
                ? MarketTime.AtNewYork(original, row.TimeAnnounced ? NewYorkTimeOfDay(row.ScheduledAt) : TimeOnly.MinValue)
                : row.ScheduledAt.ToUniversalTime();
            var firstDate = MarketTime.NewYorkDate(firstAt);
            var id = CatalystIds.Allocate(row.Family, firstDate, _ids.Contains);
            _ids.Add(id);

            var backfilled = firstAt <= now;
            Appends.Add(LedgerAppend.Create(
                LedgerKinds.CatalystScheduled, CatalystIds.EntityId(id), _correlation, ConfigVersion.None,
                Printable($"{id} scheduled for {Describe(firstAt, row.TimeAnnounced)} New York ({Source(snapshot)})"),
                new CatalystScheduledPayload(
                    id, row.Family, row.Title, row.ReferencePeriod, row.SourceKey, firstAt, row.TimeAnnounced, row.Status, row.Sep,
                    row.Tentative ? true : null, snapshot.Adapter, snapshot.Kind, row.RowSourceUrl ?? snapshot.SourceUrl,
                    (row.RowRetrievedAt ?? snapshot.RetrievedAt).ToUniversalTime(), snapshot.FileSha256, row.Derivation, backfilled, row.VerifiedBy)));
            _scheduled++;

            var tracked = new Tracked
            {
                Id = id,
                Family = row.Family,
                SourceKey = row.SourceKey,
                ScheduledAt = firstAt,
                TimeAnnounced = row.TimeAnnounced,
                Status = row.Status,
                Sep = row.Sep,
                Tentative = row.Tentative,
                Kind = snapshot.Kind,
                Adapter = snapshot.Adapter,
                Vintage = 1,
                FirstDate = firstDate,
            };
            working.Add(tracked);
            if (Differs(tracked, row))
            {
                Reschedule(tracked, row, snapshot, CatalystChange.Moved, forced: false);
            }

            return tracked;
        }

        private void Reschedule(Tracked tracked, ObservedCatalyst row, CalendarSnapshot snapshot, CatalystChange change, bool forced)
        {
            var vintage = tracked.Vintage + 1;
            var scheduledAt = row.ScheduledAt.ToUniversalTime();
            Appends.Add(LedgerAppend.Create(
                LedgerKinds.CatalystRescheduled, CatalystIds.EntityId(tracked.Id), _correlation, ConfigVersion.None,
                Printable(string.Create(CultureInfo.InvariantCulture,
                    $"{tracked.Id} {Verb(change, row.Status)} from {Describe(tracked.ScheduledAt, tracked.TimeAnnounced)} to {Describe(scheduledAt, row.TimeAnnounced)} New York (vintage {vintage}, {Source(snapshot)}{(forced ? ", forced" : "")})")),
                new CatalystRescheduledPayload(
                    tracked.Id, vintage, change, tracked.ScheduledAt, scheduledAt, row.TimeAnnounced, row.Status, row.Sep ?? tracked.Sep,
                    row.Tentative ? true : null, snapshot.Adapter, snapshot.Kind, row.RowSourceUrl ?? snapshot.SourceUrl,
                    (row.RowRetrievedAt ?? snapshot.RetrievedAt).ToUniversalTime(), snapshot.FileSha256, row.Derivation, forced ? true : null, row.VerifiedBy)));
            _rescheduled++;

            tracked.ScheduledAt = scheduledAt;
            tracked.TimeAnnounced = row.TimeAnnounced;
            tracked.Status = row.Status;
            tracked.Sep = row.Sep ?? tracked.Sep;
            tracked.Tentative = row.Tentative;
            tracked.Kind = snapshot.Kind;
            tracked.Adapter = snapshot.Adapter;
            tracked.Vintage = vintage;
        }

        private static string Verb(CatalystChange change, CatalystStatus status) => change switch
        {
            CatalystChange.Moved => "moved",
            CatalystChange.Status => status == CatalystStatus.Cancelled ? "cancelled" : "reinstated",
            CatalystChange.TimeAnnounced => "time announced",
            CatalystChange.Correction => "corrected",
            _ => "details changed",
        };

        private static string Source(CalendarSnapshot snapshot) =>
            snapshot.RequestedBy is { } by ? $"{snapshot.Adapter} by {by}" : snapshot.Adapter;
    }

    /// <summary><c>2026-10-28 14:00</c>, or <c>2026-12-03 (time not announced)</c>, in New York time.</summary>
    private static string Describe(DateTimeOffset instant, bool timeAnnounced) =>
        MarketTime.NewYorkDate(instant).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (timeAnnounced
            ? " " + NewYorkTimeOfDay(instant).ToString("HH:mm", CultureInfo.InvariantCulture)
            : " (time not announced)");

    /// <summary>The New York wall-clock time of an instant, checked against <see cref="MarketTime.AtNewYork"/> so both agree on daylight time.</summary>
    internal static TimeOnly NewYorkTimeOfDay(DateTimeOffset instant)
    {
        var date = MarketTime.NewYorkDate(instant);
        foreach (var hours in new[] { -4, -5 })
        {
            var local = instant.UtcDateTime.AddHours(hours);
            var time = TimeOnly.FromDateTime(local);
            if (DateOnly.FromDateTime(local) == date && MarketTime.AtNewYork(date, time) == instant)
            {
                return time;
            }
        }

        return TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, MarketTime.NewYork).DateTime);
    }

    /// <summary>Ledger summaries may not contain control characters (hash field separator).</summary>
    private static string Printable(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }
}
