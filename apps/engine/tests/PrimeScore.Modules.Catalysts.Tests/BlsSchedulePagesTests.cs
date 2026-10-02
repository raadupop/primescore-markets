using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Sources;
using PrimeScore.Modules.Catalysts.Sources.Bls;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>BLS release schedules, read from excerpts of the pages as fetched on 2026-09-29.</summary>
public sealed class BlsSchedulePagesTests
{
    private static readonly string Cpi = CalendarFixtures.Text("bls/cpi-excerpt.htm");

    [Fact]
    public void The_CPI_page_keys_each_release_by_reference_month_at_08_30_New_York()
    {
        var page = BlsSchedulePages.ParseRelease(Cpi, CatalystFamily.Cpi);

        // November 2025 through November 2026.
        Assert.Equal(13, page.Rows.Count);
        Assert.Empty(page.Rejected);

        // 14 October 2026 is in daylight time (UTC-4): 08:30 + 4 h = 12:30Z.
        var september = Row(page, "CPI:2026-09");
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero), september.ScheduledAt);
        Assert.Equal("September 2026", september.ReferencePeriod);
        Assert.Equal("Consumer Price Index", september.Title);
        Assert.True(september.TimeAnnounced);
        Assert.Null(september.Sep);

        // Daylight time began on 8 March 2026: "Mar. 11, 2026" is 12:30Z, "Feb. 13, 2026" 13:30Z (UTC-5).
        Assert.Equal(new DateTimeOffset(2026, 3, 11, 12, 30, 0, TimeSpan.Zero), Row(page, "CPI:2026-02").ScheduledAt);
        Assert.Equal(new DateTimeOffset(2026, 2, 13, 13, 30, 0, TimeSpan.Zero), Row(page, "CPI:2026-01").ScheduledAt);

        // First row "Dec. 18, 2025", last "Dec. 10, 2026".
        Assert.Equal(new DateOnly(2025, 12, 18), page.CoverageFrom);
        Assert.Equal(new DateOnly(2026, 12, 10), page.CoverageTo);
    }

    [Fact]
    public void The_Employment_Situation_page_is_NFP_with_zero_padded_days()
    {
        var page = BlsSchedulePages.ParseRelease(CalendarFixtures.Text("bls/empsit-excerpt.htm"), CatalystFamily.Nfp);

        // "Oct. 02, 2026", daylight time: 12:30Z. "Jan. 09, 2026" in standard time: 13:30Z.
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 12, 30, 0, TimeSpan.Zero), Row(page, "NFP:2026-09").ScheduledAt);
        Assert.Equal(new DateTimeOffset(2026, 1, 9, 13, 30, 0, TimeSpan.Zero), Row(page, "NFP:2025-12").ScheduledAt);
        Assert.All(page.Rows, row => Assert.Equal(CatalystFamily.Nfp, row.Family));
        Assert.Equal(new DateOnly(2025, 12, 16), page.CoverageFrom);
    }

    [Fact]
    public void A_TBD_or_blank_date_is_a_placeholder_for_its_key()
    {
        var html = CalendarFixtures.ReplaceAfter(Cpi, "<td>October 2026</td>", "<td>Nov. 10, 2026</td>", "<td>TBD</td>");
        html = CalendarFixtures.ReplaceAfter(html, "<td>November 2026</td>", "<td>Dec. 10, 2026</td>", "<td></td>");

        var page = BlsSchedulePages.ParseRelease(html, CatalystFamily.Cpi);

        Assert.Equal("TBD", Row(page, "CPI:2026-10").Placeholder);
        Assert.Equal("", Row(page, "CPI:2026-11").Placeholder);
        Assert.Empty(page.Rejected);

        // Coverage comes from dated rows only: the last is now "Oct. 14, 2026".
        Assert.Equal(new DateOnly(2026, 10, 14), page.CoverageTo);
    }

    [Fact]
    public void A_date_in_another_format_is_quoted_and_the_others_are_read()
    {
        var html = CalendarFixtures.ReplaceAfter(Cpi, "<td>September 2026</td>", "<td>Oct. 14, 2026</td>", "<td>2026-10-14</td>");

        var page = BlsSchedulePages.ParseRelease(html, CatalystFamily.Cpi);

        Assert.Equal(["September 2026 2026-10-14 08:30 AM"], page.Rejected);
        Assert.Equal(12, page.Rows.Count);
    }

    [Fact]
    public void A_page_without_the_release_table_or_without_a_readable_row_is_not_recognised()
    {
        var noTable = Assert.Throws<InvalidOperationException>(() => BlsSchedulePages.ParseRelease("<html><body><p>Access Denied</p></body></html>", CatalystFamily.Cpi));
        var start = Cpi.IndexOf("<tbody>", StringComparison.Ordinal);
        var end = Cpi.IndexOf("</tbody>", StringComparison.Ordinal);
        var empty = Assert.Throws<InvalidOperationException>(() => BlsSchedulePages.ParseRelease(Cpi[..(start + 7)] + Cpi[end..], CatalystFamily.Cpi));

        Assert.Equal("layout not recognised: no release-list table with Reference Month and Release Date columns", noTable.Message);
        Assert.Equal("layout not recognised: no release row parses", empty.Message);
    }

    [Fact]
    public void The_2014_archive_keeps_CPI_and_the_Employment_Situation_but_not_the_veterans_release()
    {
        var page = BlsSchedulePages.ParseArchive(CalendarFixtures.Text("bls/bls2014-excerpt.htm"));

        // January and March tables: NFP Jan 10 and Mar 7, CPI Jan 16 and Mar 18; "Employment Situation of Veterans" is left out.
        Assert.Equal(["NFP:2013-12", "CPI:2013-12", "NFP:2014-02", "CPI:2014-02"], page.Rows.Select(row => row.SourceKey));
        Assert.DoesNotContain(page.Rows, row => row.Title.Contains("Veterans", StringComparison.Ordinal));

        // "Thursday, January 16, 2014", standard time: 08:30 + 5 h = 13:30Z.
        var cpi = Row(page, "CPI:2013-12");
        Assert.Equal(new DateTimeOffset(2014, 1, 16, 13, 30, 0, TimeSpan.Zero), cpi.ScheduledAt);
        Assert.Equal("December 2013", cpi.ReferencePeriod);
        Assert.Null(page.CoverageFrom);
    }

    [Fact]
    public void The_2013_archive_shows_the_shutdown_dates_the_releases_actually_happened_on()
    {
        var page = BlsSchedulePages.ParseArchive(CalendarFixtures.Text("bls/bls2013-excerpt.htm"));

        // Employment Situation for September 2013 on Tuesday 22 October and CPI on Wednesday 30 October (daylight time, 12:30Z);
        // Employment Situation for October on Friday 8 November, after the 3 November change to standard time (13:30Z).
        Assert.Equal(new DateTimeOffset(2013, 10, 22, 12, 30, 0, TimeSpan.Zero), Row(page, "NFP:2013-09").ScheduledAt);
        Assert.Equal(new DateTimeOffset(2013, 10, 30, 12, 30, 0, TimeSpan.Zero), Row(page, "CPI:2013-09").ScheduledAt);
        Assert.Equal(new DateTimeOffset(2013, 11, 8, 13, 30, 0, TimeSpan.Zero), Row(page, "NFP:2013-10").ScheduledAt);
        Assert.Equal(6, page.Rows.Count);
    }

    [Fact]
    public void The_2025_archive_reads_unpadded_days()
    {
        var page = BlsSchedulePages.ParseArchive(CalendarFixtures.Text("bls/bls2025-excerpt.htm"));

        // "Friday, October 24, 2025": CPI for September 2025, daylight time, 12:30Z.
        Assert.Equal(new DateTimeOffset(2025, 10, 24, 12, 30, 0, TimeSpan.Zero), Row(page, "CPI:2025-09").ScheduledAt);

        // Eleven CPI and eleven Employment Situation releases: those for October 2025 were never published.
        Assert.Equal(22, page.Rows.Count);
    }

    [Fact]
    public void An_archive_row_whose_weekday_disagrees_with_its_date_is_quoted()
    {
        var html = CalendarFixtures.Text("bls/bls2014-excerpt.htm").Replace("Thursday, January 16, 2014", "Friday, January 16, 2014", StringComparison.Ordinal);

        var page = BlsSchedulePages.ParseArchive(html);

        Assert.Equal(["Friday, January 16, 2014 08:30 AM Consumer Price Index for December 2013"], page.Rejected);
        Assert.Equal(3, page.Rows.Count);
    }

    private static ObservedCatalyst Row(ParsedCalendar page, string key) => Assert.Single(page.Rows, row => row.SourceKey == key);
}
