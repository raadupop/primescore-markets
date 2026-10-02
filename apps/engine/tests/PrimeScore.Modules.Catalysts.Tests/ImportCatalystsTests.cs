using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>The operator's CSV import (the back-fill path) over a real ledger, clock pinned at 2026-09-29 12:00 UTC.</summary>
public sealed class ImportCatalystsTests : CatalystDatabase
{
    private const string Cpi2014 =
        "CPI,2014-01-16,08:30,,Consumer Price Index,December 2013,CPI:2013-12,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,archive row";

    private const string Opec2026 =
        "OPEC,2026-12-06,,,ONOMM,,OPEC:ONOMM-41,,https://www.opec.org/press-releases.html,2026-09-28T08:00:00Z,operator,";

    private static string Csv(params string[] rows) => string.Join("\n", [CatalystCsvTests.Header, .. rows]) + "\n";

    [Fact]
    public async Task Importing_the_same_file_again_records_nothing()
    {
        var first = await ImportAsync(Csv(Cpi2014, Opec2026));
        var head = await HeadAsync();
        var again = await ImportAsync(Csv(Cpi2014, Opec2026));

        Assert.Equal((2, 0, 0), (first.Scheduled, first.Rescheduled, first.Unchanged));
        Assert.Equal((0, 0, 2), (again.Scheduled, again.Rescheduled, again.Unchanged));
        Assert.Empty(again.Flags);
        Assert.Empty(again.Errors);
        Assert.Equal(head, await HeadAsync());
    }

    [Fact]
    public async Task A_curated_back_fill_row_keeps_its_own_provenance_and_is_labelled_back_filled()
    {
        await ImportAsync(Csv(Cpi2014));

        var catalyst = (await DetailAsync("CPI-2014-01-16")).Catalyst;
        Assert.Equal((true, (bool?)null, "curated", "December 2013"),
            (catalyst.Backfilled, catalyst.NeverRescheduled, catalyst.Derivation, catalyst.ReferencePeriod));
        Assert.Equal(
            new CatalystSourceView("import", CatalystSourceKind.Curated, "https://www.bls.gov/schedule/2014/home.htm",
                new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), "abcdef0123"),
            catalyst.Source);
        var entry = await EntryAsync(catalyst.LedgerSequence);
        Assert.Equal("CPI-2014-01-16 scheduled for 2014-01-16 08:30 New York (import by unit-test)", entry.Summary);
        Assert.Contains("\"verified_by\":\"operator\"", entry.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Force_lets_a_curated_row_replace_an_archive_date_and_marks_the_vintage_forced()
    {
        await RecordAsync(Snapshot("BlsCalendar", CatalystSourceKind.Archive, CatalystFamily.Cpi,
            Release(CatalystFamily.Cpi, "CPI:2013-12", At(2014, 1, 16, 8, 30))));
        var moved = Cpi2014.Replace("2014-01-16", "2014-01-15", StringComparison.Ordinal);

        var kept = await ImportAsync(Csv(moved));
        var forced = await ImportAsync(Csv(moved), force: true);

        Assert.Equal((0, 0, 1), (kept.Scheduled, kept.Rescheduled, kept.Unchanged));
        Assert.Equal(["CPI-2014-01-16: import lists 2014-01-15 08:30; current vintage from BlsCalendar (Archive) kept"], kept.Flags);
        Assert.Equal((0, 1, 0), (forced.Scheduled, forced.Rescheduled, forced.Unchanged));
        var detail = await DetailAsync("CPI-2014-01-16");
        Assert.Equal((2, CatalystChange.Moved, CatalystSourceKind.Curated), (detail.Catalyst.Vintage, detail.Vintages[1].Change, detail.Catalyst.Source.Kind));
        var entry = await EntryAsync(detail.Catalyst.LedgerSequence);
        Assert.Contains("\"forced\":true", entry.Payload, StringComparison.Ordinal);
        Assert.EndsWith("(vintage 2, import by unit-test, forced)", entry.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_invalid_file_records_nothing_and_names_the_line()
    {
        var head = await HeadAsync();

        var ack = await ImportAsync(Csv(Opec2026, Cpi2014.Replace("2014-01-16", "2014-13-01", StringComparison.Ordinal)));

        Assert.Equal((0, 0, 0), (ack.Scheduled, ack.Rescheduled, ack.Unchanged));
        Assert.Equal(["line 3: scheduled_date must be YYYY-MM-DD"], ack.Errors);
        Assert.Equal(head, await HeadAsync());
    }
}
