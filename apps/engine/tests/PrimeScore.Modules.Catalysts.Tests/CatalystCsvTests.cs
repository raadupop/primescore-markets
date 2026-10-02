using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Sources;

namespace PrimeScore.Modules.Catalysts.Tests;

public sealed class CatalystCsvTests
{
    internal const string Header =
        "family,scheduled_date,scheduled_time_new_york,originally_scheduled_date,title,reference_period,source_key,sep,source_url,retrieved_at,verified_by,note";

    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private static CatalystCsvFile Parse(params string[] rows) => CatalystCsv.Parse(string.Join("\r\n", [Header, .. rows]), Now);

    [Fact]
    public void A_valid_row_carries_its_own_source_url_retrieval_time_and_verifier()
    {
        var file = Parse("CPI,2014-01-16,08:30,,Consumer Price Index,December 2013,CPI:2013-12,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,archive row");

        Assert.Empty(file.Errors);
        var row = Assert.Single(file.Rows);
        // 2014-01-16 is in standard time (UTC-5): 08:30 New York = 13:30 UTC.
        Assert.Equal(new DateTimeOffset(2014, 1, 16, 13, 30, 0, TimeSpan.Zero), row.ScheduledAt);
        Assert.Equal(
            (CatalystFamily.Cpi, true, "Consumer Price Index", "December 2013", "CPI:2013-12", (bool?)null, false, CatalystStatus.Scheduled, "curated"),
            (row.Family, row.TimeAnnounced, row.Title, row.ReferencePeriod, row.SourceKey, row.Sep, row.Tentative, row.Status, row.Derivation));
        Assert.Equal(
            ("https://www.bls.gov/schedule/2014/home.htm", (DateTimeOffset?)new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), "operator", (DateOnly?)null),
            (row.RowSourceUrl, row.RowRetrievedAt, row.VerifiedBy, row.OriginallyScheduledDate));
    }

    [Fact]
    public void An_empty_time_means_not_announced_and_midnight_New_York()
    {
        var file = Parse("OPEC,2026-06-07,,,JMMC meeting,,OPEC:JMMC-66,,https://www.opec.org/pr-detail/1574596-5-april-2026.html,2026-04-06T09:00:00+02:00,operator,");

        var row = Assert.Single(file.Rows);
        // 2026-06-07 is in daylight time (UTC-4): 00:00 New York = 04:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 6, 7, 4, 0, 0, TimeSpan.Zero), row.ScheduledAt);
        Assert.False(row.TimeAnnounced);
        // 09:00 at UTC+2 = 07:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 4, 6, 7, 0, 0, TimeSpan.Zero), row.RowRetrievedAt);
    }

    [Fact]
    public void Quoted_cells_may_hold_commas_and_sep_is_read_for_FOMC()
    {
        var file = Parse(
            "GDP,2026-10-29,08:30,,\"GDP (Advance Estimate), 3rd Quarter 2026\",3rd Quarter 2026,GDP:2026Q3:advance,,https://www.bea.gov/news/schedule,2026-09-29T10:00:00Z,operator,",
            "FOMC,2026-12-09,14:00,,FOMC meeting,,,true,https://www.federalreserve.gov/monetarypolicy/fomccalendars.htm,2026-09-29T10:00:00Z,operator,\"note, with comma\"");

        Assert.Empty(file.Errors);
        Assert.Equal("GDP (Advance Estimate), 3rd Quarter 2026", file.Rows[0].Title);
        Assert.Equal((true, (string?)null), (file.Rows[1].Sep, file.Rows[1].SourceKey));
    }

    [Fact]
    public void An_original_date_is_read_and_must_differ_from_the_scheduled_date()
    {
        var moved = Parse("NFP,2013-10-22,08:30,2013-10-04,Employment Situation,September 2013,NFP:2013-09,,https://www.bls.gov/schedule/2013/home.htm,2026-09-01T12:00:00Z,operator,shutdown");
        var same = Parse("NFP,2013-10-22,08:30,2013-10-22,Employment Situation,September 2013,NFP:2013-09,,https://www.bls.gov/schedule/2013/home.htm,2026-09-01T12:00:00Z,operator,");

        Assert.Equal(new DateOnly(2013, 10, 4), Assert.Single(moved.Rows).OriginallyScheduledDate);
        Assert.Empty(same.Rows);
        Assert.StartsWith("line 2: originally_scheduled_date must differ", Assert.Single(same.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public void Any_invalid_row_rejects_the_whole_file_and_every_problem_names_its_line()
    {
        var file = Parse(
            "CPI,2014-01-16,08:30,,Consumer Price Index,December 2013,CPI:2013-12,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,",
            "CPI,2014-02-20,08:30,,Consumer Price Index,January 2014,CPI:2014-01,,,2026-09-01T12:00:00Z,operator,",
            "CPI,2014-03-18,08:30,,Consumer Price Index,February 2014,CPI:2014-02,,https://www.bls.gov/schedule/2014/home.htm,,operator,",
            "CPI,2014-04-15,08:30,,Consumer Price Index,March 2014,CPI:2014-03,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,,",
            "ECB,2014-05-15,08:30,,Rates,,,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,",
            "CPI,2014-13-01,08:30,,Consumer Price Index,,,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,",
            "CPI,2014-06-17,8.30,,Consumer Price Index,,,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,",
            "CPI,2014-07-22,08:30,,Consumer Price Index,,,,https://www.bls.gov/schedule/2014/home.htm,2026-09-01 12:00:00,operator,",
            "CPI,2014-08-19,08:30,,Consumer Price Index,,,true,https://www.bls.gov/schedule/2014/home.htm,2026-09-01T12:00:00Z,operator,",
            "CPI,2014-09-17,08:30");

        Assert.Empty(file.Rows);
        Assert.Equal(
            [
                "line 3: source_url must be an absolute http(s) URL",
                "line 4: retrieved_at must be an ISO 8601 date-time with an offset (e.g. 2026-09-01T12:00:00Z)",
                "line 5: verified_by is required",
                "line 6: family must be one of FOMC, CPI, NFP, CLAIMS, GDP, PCE, WPSR, OPEC",
                "line 7: scheduled_date must be YYYY-MM-DD",
                "line 8: scheduled_time_new_york must be HH:mm or empty",
                "line 9: retrieved_at must be an ISO 8601 date-time with an offset (e.g. 2026-09-01T12:00:00Z)",
                "line 10: sep applies to FOMC rows only",
                "line 11: expected 12 columns, found 3",
            ],
            file.Errors);
    }

    [Fact]
    public void A_row_retrieved_after_the_recording_clock_rejects_the_file()
    {
        var file = Parse("CPI,2026-10-14,08:30,,Consumer Price Index,September 2026,CPI:2026-09,,https://www.bls.gov/schedule/news_release/cpi.htm,2026-09-29T12:00:01Z,operator,");

        Assert.Empty(file.Rows);
        Assert.Equal(["line 2: retrieved_at 2026-09-29T12:00:01Z is later than the recording time 2026-09-29T12:00:00Z"], file.Errors);
    }

    [Fact]
    public void The_header_must_match_exactly()
    {
        var file = CatalystCsv.Parse("family,scheduled_date\r\nCPI,2026-10-14", Now);

        Assert.Empty(file.Rows);
        Assert.StartsWith("line 1: header must be: family,scheduled_date,scheduled_time_new_york,", Assert.Single(file.Errors), StringComparison.Ordinal);
        Assert.Empty(CatalystCsv.Parse(Header + "\r\n", Now).Errors);
    }
}
