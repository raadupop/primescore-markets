using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using static PrimeScore.Modules.Catalysts.Contracts.CatalystFamily;
using static PrimeScore.Modules.Catalysts.Contracts.CatalystSourceKind;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>
/// The calendar vintage test over a real ledger (clock pinned at 2026-09-29 12:00 UTC): a reschedule
/// is a new entry under the same id, the projection serves the latest schedule and keeps every
/// vintage, and seeing a schedule again writes nothing.
/// </summary>
public sealed class CatalystRecorderTests : CatalystDatabase
{
    private const string FedUrl = "https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm";

    private static readonly DateTimeOffset FetchedAt = new(2026, 9, 29, 11, 0, 0, TimeSpan.Zero);

    /// <summary>The Fed's current calendar: second meeting day at 14:00 New York, listing coverage 2026.</summary>
    private static CalendarSnapshot Fed(params ObservedCatalyst[] meetings) =>
        new("FedCalendar", Listing, FedUrl, FetchedAt, "feedc0de", [Fomc], new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 21, meetings);

    private static ObservedCatalyst Meeting(int month, int day, bool tentative = false, int year = 2026) =>
        new(Fomc, At(year, month, day, 14), true, "FOMC meeting", null, null, false, tentative, CatalystStatus.Scheduled, null);

    private static CalendarSnapshot Bls(CatalystSourceKind kind, CatalystFamily family, params ObservedCatalyst[] rows) =>
        Snapshot("BlsCalendar", kind, family, rows);

    [Fact]
    public async Task A_new_schedule_is_vintage_1_and_seeing_it_again_writes_nothing()
    {
        var first = await RecordAsync(Fed(Meeting(10, 28)));
        var head = await HeadAsync();
        var again = await RecordAsync(Fed(Meeting(10, 28)));

        Assert.Equal((1, 0, 0), (first.Scheduled, first.Rescheduled, first.Unchanged));
        Assert.Empty(first.Flags);
        Assert.Equal((0, 0, 1), (again.Scheduled, again.Rescheduled, again.Unchanged));
        Assert.Equal(head, await HeadAsync());

        var detail = await DetailAsync("FOMC-2026-10-28");
        var catalyst = detail.Catalyst;
        // 2026-10-28 is in daylight time (UTC-4): 14:00 New York = 18:00 UTC, after the pinned clock, so not back-filled.
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), catalyst.ScheduledAt);
        Assert.Equal(
            (new DateOnly(2026, 10, 28), 1, (bool?)true, false, Now, (bool?)false, false, head),
            (catalyst.ScheduledDate, catalyst.Vintage, catalyst.NeverRescheduled, catalyst.Backfilled, catalyst.FirstAnnouncedAt, catalyst.Sep, catalyst.Tentative, catalyst.LedgerSequence));
        Assert.Equal(new CatalystSourceView("FedCalendar", Listing, FedUrl, FetchedAt, "feedc0de"), catalyst.Source);
        var vintage = Assert.Single(detail.Vintages);
        Assert.Equal(((CatalystChange?)null, Now), (vintage.Change, vintage.RecordedAt));

        var entry = await EntryAsync(head);
        Assert.Equal(("CatalystScheduled", CatalystIds.EntityId("FOMC-2026-10-28"), catalyst.LedgerHash), (entry.Kind, entry.EntityId, entry.Hash));
        Assert.Equal("FOMC-2026-10-28 scheduled for 2026-10-28 14:00 New York (FedCalendar)", entry.Summary);
    }

    [Fact]
    public async Task A_moved_meeting_is_a_new_vintage_under_the_same_id_and_the_history_is_kept()
    {
        await RecordAsync(Fed(Meeting(10, 28), Meeting(12, 9)));
        var head = await HeadAsync();

        var moved = await RecordAsync(Fed(Meeting(11, 4), Meeting(12, 9)));

        Assert.Equal((0, 1, 1), (moved.Scheduled, moved.Rescheduled, moved.Unchanged));
        Assert.Empty(moved.Flags);
        Assert.Equal(head + 1, await HeadAsync());
        var detail = await DetailAsync("FOMC-2026-10-28");
        // 2026-11-04 is in standard time (daylight time ended 2026-11-01): 14:00 New York = 19:00 UTC.
        Assert.Equal((2, new DateTimeOffset(2026, 11, 4, 19, 0, 0, TimeSpan.Zero), (bool?)false, Now),
            (detail.Catalyst.Vintage, detail.Catalyst.ScheduledAt, detail.Catalyst.NeverRescheduled, detail.Catalyst.FirstAnnouncedAt));
        Assert.Equal(
            [new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 4, 19, 0, 0, TimeSpan.Zero)],
            detail.Vintages.Select(vintage => vintage.ScheduledAt));
        Assert.Equal([null, CatalystChange.Moved], detail.Vintages.Select(vintage => vintage.Change));
        Assert.Equal("FOMC-2026-10-28 moved from 2026-10-28 14:00 to 2026-11-04 14:00 New York (vintage 2, FedCalendar)", (await EntryAsync(head + 1)).Summary);

        Assert.Empty(await ListAsync(At(2026, 10, 1), At(2026, 10, 31, 23, 59)));
        Assert.Equal(["FOMC-2026-10-28", "FOMC-2026-12-09"], (await ListAsync(At(2026, 11, 1), At(2026, 12, 31))).Select(catalyst => catalyst.CatalystId));
        Assert.Equal(1, (await DetailAsync("FOMC-2026-12-09")).Catalyst.Vintage);
    }

    [Fact]
    public async Task A_meeting_the_page_drops_and_a_withdrawn_date_are_flagged_without_an_append()
    {
        await RecordAsync(Fed(Meeting(10, 28), Meeting(12, 9)));
        var cpi = new CalendarSnapshot("BlsCalendar", Listing, "https://www.bls.gov/schedule/news_release/cpi.htm", FetchedAt, "c0ffee", [Cpi],
            new DateOnly(2026, 10, 14), new DateOnly(2026, 12, 10), 0, [Release(Cpi, "CPI:2026-09", At(2026, 10, 14, 8, 30))]);
        await RecordAsync(cpi);
        var head = await HeadAsync();

        var dropped = await RecordAsync(Fed(Meeting(10, 28)));
        var withdrawn = await RecordAsync(cpi with
        {
            Rows = [Release(Cpi, "CPI:2026-09", default) with { Placeholder = "TBD" }],
        });

        Assert.Equal(["FOMC-2026-12-09 no longer listed by FedCalendar"], dropped.Flags);
        Assert.Equal(["CPI-2026-10-14 date withdrawn by BlsCalendar ('TBD')"], withdrawn.Flags);
        Assert.Equal((0, 0, 0), (withdrawn.Scheduled, withdrawn.Rescheduled, withdrawn.Unchanged));
        Assert.Equal(head, await HeadAsync());
        Assert.Equal(1, (await DetailAsync("FOMC-2026-12-09")).Catalyst.Vintage);
    }

    [Fact]
    public async Task A_release_matched_by_its_reference_period_is_rescheduled_across_a_48_day_move()
    {
        // The 2025 shutdown moved the September 2025 Employment Situation from 2025-10-03 to 2025-11-20 (48 days).
        await RecordAsync(Bls(Listing, Nfp, Release(Nfp, "NFP:2025-09", At(2025, 10, 3, 8, 30))));
        var moved = await RecordAsync(Bls(Listing, Nfp, Release(Nfp, "NFP:2025-09", At(2025, 11, 20, 8, 30))));

        Assert.Equal((0, 1), (moved.Scheduled, moved.Rescheduled));
        var nfp = Assert.Single(await ListAsync(At(2025, 1, 1), At(2025, 12, 31), Nfp));
        // 2025-11-20 is in standard time: 08:30 New York = 13:30 UTC. Both dates precede the pinned clock: back-filled, and moved.
        Assert.Equal(("NFP-2025-10-03", 2, new DateTimeOffset(2025, 11, 20, 13, 30, 0, TimeSpan.Zero), true, (bool?)false),
            (nfp.CatalystId, nfp.Vintage, nfp.ScheduledAt, nfp.Backfilled, nfp.NeverRescheduled));
    }

    [Fact]
    public async Task A_new_catalyst_whose_id_a_moved_catalyst_owns_gets_a_suffix()
    {
        await RecordAsync(Bls(Listing, Cpi, Release(Cpi, "CPI:2026-09", At(2026, 10, 14, 8, 30))));
        await RecordAsync(Bls(Listing, Cpi, Release(Cpi, "CPI:2026-09", At(2026, 10, 15, 8, 30))));

        var tally = await RecordAsync(Bls(Listing, Cpi, Release(Cpi, "CPI:2026-10", At(2026, 10, 14, 8, 30))));

        Assert.Equal(1, tally.Scheduled);
        Assert.Equal(["CPI-2026-10-14-2", "CPI-2026-10-14"], (await ListAsync(At(2026, 10, 1), At(2026, 10, 31), Cpi)).Select(catalyst => catalyst.CatalystId));
    }

    [Fact]
    public async Task Snapshots_of_one_call_see_the_entries_planned_before_them()
    {
        var start = await HeadAsync();

        // Listing and archive agree: one catalyst.
        var agree = await RecordAsync(
            Bls(Listing, Cpi, Release(Cpi, "CPI:2025-11", At(2025, 12, 18, 8, 30))),
            Bls(Archive, Cpi, Release(Cpi, "CPI:2025-11", At(2025, 12, 18, 8, 30))));

        // The archive disagrees with the listing: the listing outranks it, so no vintage 2 and one flag.
        var archiveLater = await RecordAsync(
            Bls(Listing, Cpi, Release(Cpi, "CPI:2025-12", At(2026, 1, 13, 8, 30))),
            Bls(Archive, Cpi, Release(Cpi, "CPI:2025-12", At(2026, 1, 14, 8, 30))));

        // The listing disagrees with the archive: vintage 1 and vintage 2 in one append.
        var listingLater = await RecordAsync(
            Bls(Archive, Cpi, Release(Cpi, "CPI:2026-01", At(2026, 2, 11, 8, 30))),
            Bls(Listing, Cpi, Release(Cpi, "CPI:2026-01", At(2026, 2, 12, 8, 30))));

        Assert.Equal((1, 0, 1), (agree.Scheduled, agree.Rescheduled, agree.Unchanged));
        Assert.Equal((1, 0, 1), (archiveLater.Scheduled, archiveLater.Rescheduled, archiveLater.Unchanged));
        Assert.Equal(["CPI-2026-01-13: BlsCalendar lists 2026-01-14 08:30; current vintage from BlsCalendar (Listing) kept"], archiveLater.Flags);
        Assert.Equal((1, 1, 0), (listingLater.Scheduled, listingLater.Rescheduled, listingLater.Unchanged));
        Assert.Equal(start + 4, await HeadAsync());

        var moved = await DetailAsync("CPI-2026-02-11");
        Assert.Equal([Archive, Listing], moved.Vintages.Select(vintage => vintage.Source.Kind));
        Assert.Equal([null, CatalystChange.Moved], moved.Vintages.Select(vintage => vintage.Change));
        Assert.Equal(1, (await DetailAsync("CPI-2026-01-13")).Catalyst.Vintage);
    }

    [Fact]
    public async Task A_rule_never_undoes_a_curated_date_and_a_curated_date_corrects_a_rule()
    {
        static CalendarSnapshot Rule(string key, DateTimeOffset at) =>
            Snapshot("ClaimsCalendar", CatalystSourceKind.Rule, Claims, Release(Claims, key, at) with { Derivation = "rule" });
        static CalendarSnapshot Curated(string key, DateTimeOffset at) =>
            Snapshot("import", CatalystSourceKind.Curated, Claims, Release(Claims, key, at) with { Derivation = "curated" });

        await RecordAsync(Curated("CLAIMS:2026-11-21", At(2026, 11, 24, 8, 30)));
        var head = await HeadAsync();
        var ruleAfter = await RecordAsync(Rule("CLAIMS:2026-11-21", At(2026, 11, 25, 8, 30)));

        await RecordAsync(Rule("CLAIMS:2026-11-28", At(2026, 12, 3, 8, 30)));
        var corrected = await RecordAsync(Curated("CLAIMS:2026-11-28", At(2026, 12, 2, 8, 30)));

        Assert.Equal(head, (await HeadAsync()) - 2);
        Assert.Equal(["CLAIMS-2026-11-24: ClaimsCalendar lists 2026-11-25 08:30; current vintage from import (Curated) kept"], ruleAfter.Flags);
        Assert.Equal(1, (await DetailAsync("CLAIMS-2026-11-24")).Catalyst.Vintage);
        Assert.Equal((0, 1), (corrected.Scheduled, corrected.Rescheduled));
        var detail = await DetailAsync("CLAIMS-2026-12-03");
        // A correction is not a reschedule, and the first vintage (2026-12-03) is after the pinned clock.
        Assert.Equal((2, CatalystChange.Correction, (bool?)true, "curated"),
            (detail.Catalyst.Vintage, detail.Vintages[1].Change, detail.Catalyst.NeverRescheduled, detail.Catalyst.Derivation));
    }

    [Fact]
    public async Task An_announced_time_and_a_confirmed_tentative_date_are_vintages_that_do_not_count_as_reschedules()
    {
        var noTime = Release(Opec, "OPEC:ONOMM-41", At(2026, 12, 3), timeAnnounced: false);
        await RecordAsync(Snapshot("OpecCalendar", Curated, Opec, noTime));
        await RecordAsync(Snapshot("OpecCalendar", Curated, Opec, noTime with { ScheduledAt = At(2026, 12, 3, 10), TimeAnnounced = true }));

        await RecordAsync(Fed(Meeting(1, 27, tentative: true, year: 2027)));
        await RecordAsync(Fed(Meeting(1, 27, tentative: false, year: 2027)));

        var opec = await DetailAsync("OPEC-2026-12-03");
        // 2026-12-03 is in standard time: 00:00 New York = 05:00 UTC, 10:00 New York = 15:00 UTC.
        Assert.Equal(
            [new DateTimeOffset(2026, 12, 3, 5, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 3, 15, 0, 0, TimeSpan.Zero)],
            opec.Vintages.Select(vintage => vintage.ScheduledAt));
        Assert.Equal((CatalystChange.TimeAnnounced, (bool?)true, true), (opec.Vintages[1].Change, opec.Catalyst.NeverRescheduled, opec.Catalyst.TimeAnnounced));

        var fomc = await DetailAsync("FOMC-2027-01-27");
        Assert.Equal([true, false], fomc.Vintages.Select(vintage => vintage.Tentative));
        Assert.Equal((CatalystChange.Detail, (bool?)true, false), (fomc.Vintages[1].Change, fomc.Catalyst.NeverRescheduled, fomc.Catalyst.Tentative));
    }

    [Fact]
    public async Task Back_filled_rows_leave_never_rescheduled_unknown_unless_a_known_move_is_imported()
    {
        await RecordAsync(Bls(Archive, Cpi, Release(Cpi, "CPI:2013-12", At(2014, 1, 16, 8, 30))));
        var shutdown = Snapshot("import", Curated, Nfp,
            Release(Nfp, "NFP:2013-09", At(2013, 10, 22, 8, 30)) with { OriginallyScheduledDate = new DateOnly(2013, 10, 4), Derivation = "curated" });
        var known = await RecordAsync(shutdown);

        await RecordAsync(Bls(Archive, Cpi, Release(Cpi, "CPI:2013-09", At(2013, 10, 30, 8, 30))));
        var head = await HeadAsync();
        var late = await RecordAsync(Snapshot("import", Curated, Cpi,
            Release(Cpi, "CPI:2013-09", At(2013, 10, 30, 8, 30)) with { OriginallyScheduledDate = new DateOnly(2013, 10, 16), Derivation = "curated" }));

        var archived = (await DetailAsync("CPI-2014-01-16")).Catalyst;
        Assert.Equal((true, (bool?)null), (archived.Backfilled, archived.NeverRescheduled));

        Assert.Equal((1, 1), (known.Scheduled, known.Rescheduled));
        var nfp = await DetailAsync("NFP-2013-10-04");
        // Both dates are in daylight time (UTC-4): 08:30 New York = 12:30 UTC.
        Assert.Equal(
            [new DateTimeOffset(2013, 10, 4, 12, 30, 0, TimeSpan.Zero), new DateTimeOffset(2013, 10, 22, 12, 30, 0, TimeSpan.Zero)],
            nfp.Vintages.Select(vintage => vintage.ScheduledAt));
        Assert.Equal((CatalystChange.Moved, true, (bool?)false), (nfp.Vintages[1].Change, nfp.Catalyst.Backfilled, nfp.Catalyst.NeverRescheduled));

        Assert.Equal(head, await HeadAsync());
        Assert.Equal(1, late.Unchanged);
        Assert.Equal(["CPI-2013-10-30: originally scheduled 2013-10-16 given, but the catalyst was first recorded at its final date; never_rescheduled stays unknown"], late.Flags);
        Assert.Null((await DetailAsync("CPI-2013-10-30")).Catalyst.NeverRescheduled);
    }

    [Fact]
    public async Task Two_rows_for_one_family_and_date_in_one_source_are_one_catalyst()
    {
        var tally = await RecordAsync(Snapshot("OpecCalendar", Curated, Opec,
            Release(Opec, "OPEC:JMMC-67", At(2026, 12, 6), timeAnnounced: false),
            Release(Opec, "OPEC:ONOMM-41", At(2026, 12, 6), timeAnnounced: false)));

        Assert.Equal((1, 0, 1), (tally.Scheduled, tally.Rescheduled, tally.Unchanged));
        Assert.Equal("OPEC-2026-12-06", Assert.Single(await ListAsync(At(2026, 12, 1), At(2026, 12, 31), Opec)).CatalystId);
    }

    [Fact]
    public async Task An_unseen_cancelled_meeting_is_not_recorded_and_a_seen_one_changes_status()
    {
        var cancelled = Meeting(12, 9) with { Status = CatalystStatus.Cancelled };
        var unseen = await RecordAsync(Fed(Meeting(10, 28)) with { CoverageFrom = null, CoverageTo = null, Rows = [Meeting(10, 28), cancelled] });
        await RecordAsync(Fed(Meeting(10, 28), Meeting(12, 9)));
        var seen = await RecordAsync(Fed(Meeting(10, 28), cancelled));

        Assert.Equal(1, unseen.Scheduled);
        Assert.Equal(1, seen.Rescheduled);
        var detail = await DetailAsync("FOMC-2026-12-09");
        Assert.Equal((CatalystStatus.Cancelled, CatalystChange.Status, (bool?)false), (detail.Catalyst.Status, detail.Vintages[1].Change, detail.Catalyst.NeverRescheduled));
    }
}
