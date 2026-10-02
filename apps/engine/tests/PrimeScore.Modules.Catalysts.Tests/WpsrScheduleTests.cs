using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Sources;
using PrimeScore.Modules.Catalysts.Sources.Eia;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>EIA's WPSR schedule page, read from an excerpt of the page as fetched on 2026-09-29 (the whole exception table).</summary>
public sealed class WpsrScheduleTests
{
    private static readonly string Page = CalendarFixtures.Text("eia/wpsr-schedule-excerpt.htm");

    [Fact]
    public void One_row_per_data_week_from_the_first_to_the_last_week_in_EIAs_table()
    {
        var page = WpsrSchedulePage.Parse(Page);

        // Week ending 2024-12-27 (first table row) to 2026-11-06 (last): 364 days to 2025-12-26, 6 to
        // 2026-01-01, 309 to 2026-11-06 = 679 days = 97 weeks, so 98 weeks; 14 of them are table rows.
        Assert.Equal(98, page.Rows.Count);
        Assert.Equal(14, page.Rows.Count(row => row.Derivation is null));
        Assert.Equal(84, page.Rows.Count(row => row.Derivation == "standard-weekday"));
        Assert.Empty(page.Rejected);
        Assert.All(page.Rows, row => Assert.Equal(CatalystFamily.Wpsr, row.Family));
        Assert.Equal("WPSR:2024-12-27", page.Rows[0].SourceKey);

        // The last week is 2026-11-06: nothing is derived for the week ending 2026-11-13.
        Assert.Equal("WPSR:2026-11-06", page.Rows[^1].SourceKey);
        Assert.DoesNotContain(page.Rows, row => row.SourceKey == "WPSR:2026-11-13");

        // Coverage runs from the first release (Thursday 2025-01-02) to the last (Thursday 2026-11-12).
        Assert.Equal(new DateOnly(2025, 1, 2), page.CoverageFrom);
        Assert.Equal(new DateOnly(2026, 11, 12), page.CoverageTo);
    }

    [Fact]
    public void A_week_without_a_table_row_is_the_standard_Wednesday_at_10_30_New_York()
    {
        var row = Row(WpsrSchedulePage.Parse(Page), "WPSR:2026-09-25");

        // Friday 2026-09-25 + 5 days = Wednesday 2026-09-30; 10:30 EDT (UTC-4) = 14:30Z.
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 14, 30, 0, TimeSpan.Zero), row.ScheduledAt);
        Assert.Equal("standard-weekday", row.Derivation);
        Assert.Equal("week ending 2026-09-25", row.ReferencePeriod);
        Assert.Equal("Weekly Petroleum Status Report", row.Title);
        Assert.True(row.TimeAnnounced);
    }

    [Fact]
    public void A_table_row_gives_its_own_date_and_time()
    {
        var page = WpsrSchedulePage.Parse(Page);

        // "December 19, 2025 | December 29, 2025 | Monday | 5:00 p.m.": 17:00 EST (UTC-5) = 22:00Z.
        var christmas = Row(page, "WPSR:2025-12-19");
        Assert.Equal(new DateTimeOffset(2025, 12, 29, 22, 0, 0, TimeSpan.Zero), christmas.ScheduledAt);
        Assert.Null(christmas.Derivation);

        // "November 6, 2026 | November 12, 2026 | Thursday | 12:00 p.m.": after the change on 1 November, 12:00 EST = 17:00Z.
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 17, 0, 0, TimeSpan.Zero), Row(page, "WPSR:2026-11-06").ScheduledAt);

        // "December 27, 2024 | January 2, 2025 | Thursday | 11:00 a.m.": 11:00 EST = 16:00Z.
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 16, 0, 0, TimeSpan.Zero), Row(page, "WPSR:2024-12-27").ScheduledAt);
    }

    [Fact]
    public void A_table_row_that_does_not_read_is_quoted_and_its_week_is_not_guessed()
    {
        // The release day no longer agrees with 2026-10-15, a Thursday.
        var html = CalendarFixtures.ReplaceAfter(Page, "October 9, 2026", "<td>Thursday</td>", "<td>Wednesday</td>");

        var page = WpsrSchedulePage.Parse(html);

        Assert.Equal(["October 9, 2026 October 15, 2026 Wednesday 12:00 p.m. Columbus Day"], page.Rejected);
        Assert.DoesNotContain(page.Rows, row => row.SourceKey == "WPSR:2026-10-09");
        Assert.Equal(97, page.Rows.Count);
    }

    [Fact]
    public void A_page_without_the_schedule_table_or_a_readable_row_is_not_recognised()
    {
        Assert.Equal(
            "layout not recognised: no schedule table with week ending and release date columns",
            Assert.Throws<InvalidOperationException>(() => WpsrSchedulePage.Parse("<html><body>Just a moment...</body></html>")).Message);

        var tableStart = Page.IndexOf("<tbody>", StringComparison.Ordinal);
        var tableEnd = Page.IndexOf("</tbody>", StringComparison.Ordinal);
        var empty = string.Concat(Page.AsSpan(0, tableStart), "<tbody>", Page.AsSpan(tableEnd));
        Assert.Equal(
            "layout not recognised: no exception row reads",
            Assert.Throws<InvalidOperationException>(() => WpsrSchedulePage.Parse(empty)).Message);
    }

    private static ObservedCatalyst Row(ParsedCalendar page, string key) => Assert.Single(page.Rows, row => row.SourceKey == key);
}
