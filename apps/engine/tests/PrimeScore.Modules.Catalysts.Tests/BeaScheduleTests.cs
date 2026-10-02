using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Sources.Bea;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>BEA's release calendar feed, read from an excerpt of the feed as fetched on 2026-09-29.</summary>
public sealed class BeaScheduleTests
{
    private static readonly string Feed = CalendarFixtures.Text("bea/schedule-excerpt.ics");

    [Fact]
    public void GDP_estimates_and_PCE_releases_are_read_and_every_other_release_is_left_out()
    {
        var page = BeaSchedule.Parse(Feed);

        // 17 events: 6 GDP estimates, 7 Personal Income and Outlays; trade, GDP by state, GDP for
        // Puerto Rico and GDP by county are not examined.
        Assert.Equal(
            [
                "GDP:2024Q4:advance", "PCE:december 2024", "PCE:september 2025", "GDP:2025Q3:initial", "GDP:2025Q3:updated",
                "PCE:october and november 2025", "PCE:july 2026", "GDP:2026Q2:third", "PCE:august 2026", "GDP:2026Q3:advance",
                "PCE:september 2026", "GDP:2026Q3:third", "PCE:november 2026",
            ],
            page.Rows.Select(row => row.SourceKey));
        Assert.Empty(page.Rejected);
        Assert.Equal(13, page.RowsExamined);

        // First event 2025-01-07 (trade), last 2026-12-23: both are 13:30Z, 08:30 New York in standard time.
        Assert.Equal(new DateOnly(2025, 1, 7), page.CoverageFrom);
        Assert.Equal(new DateOnly(2026, 12, 23), page.CoverageTo);
    }

    [Fact]
    public void The_Q3_2026_advance_estimate_is_08_30_New_York_on_29_October()
    {
        var gdp = Row(BeaSchedule.Parse(Feed), "GDP:2026Q3:advance");

        // DTSTART 20261029T123000Z; 29 October 2026 is in daylight time (UTC-4): 12:30Z = 08:30 New York.
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero), gdp.ScheduledAt);
        Assert.Equal(new DateOnly(2026, 10, 29), MarketTime.NewYorkDate(gdp.ScheduledAt));
        Assert.Equal(CatalystFamily.Gdp, gdp.Family);
        Assert.Equal("Gross Domestic Product", gdp.Title);
        Assert.Equal("2026 Q3 advance estimate", gdp.ReferencePeriod);
        Assert.True(gdp.TimeAnnounced);
        Assert.Null(gdp.Derivation);
    }

    [Fact]
    public void The_older_summary_form_and_the_2025_estimate_words_are_read()
    {
        var page = BeaSchedule.Parse(Feed);

        // "Gross Domestic Product\, 4th Quarter and Year 2024 (Advance Estimate)", folded mid-word; 20250130T133000Z.
        Assert.Equal(new DateTimeOffset(2025, 1, 30, 13, 30, 0, TimeSpan.Zero), Row(page, "GDP:2024Q4:advance").ScheduledAt);

        // After the 2025 shutdown BEA published an "Initial" and an "Updated" estimate for Q3 2025.
        Assert.Equal(new DateTimeOffset(2025, 12, 23, 13, 30, 0, TimeSpan.Zero), Row(page, "GDP:2025Q3:initial").ScheduledAt);
        Assert.Equal("2025 Q3 updated estimate", Row(page, "GDP:2025Q3:updated").ReferencePeriod);
    }

    [Fact]
    public void PCE_is_keyed_by_its_period_as_written_in_lower_case()
    {
        var page = BeaSchedule.Parse(Feed);

        // 20260122T150000Z: 10:00 New York in standard time (UTC-5).
        var combined = Row(page, "PCE:october and november 2025");
        Assert.Equal(new DateTimeOffset(2026, 1, 22, 15, 0, 0, TimeSpan.Zero), combined.ScheduledAt);
        Assert.Equal("October and November 2025", combined.ReferencePeriod);
        Assert.Equal("Personal Income and Outlays", combined.Title);

        // "Personal Income and Outlays\, July 2026 " carries a trailing space in the feed.
        Assert.Equal("July 2026", Row(page, "PCE:july 2026").ReferencePeriod);
        Assert.Equal(new DateOnly(2026, 9, 30), MarketTime.NewYorkDate(Row(page, "PCE:august 2026").ScheduledAt));
    }

    [Fact]
    public void A_GDP_event_whose_quarter_does_not_read_is_quoted_and_the_others_are_read()
    {
        var feed = Feed.Replace("GDP (Advance Estimate)\\, 3rd Quarter 2026", "GDP (Advance Estimate)\\, Third Quarter 2026", StringComparison.Ordinal);

        var page = BeaSchedule.Parse(feed);

        Assert.Equal(["GDP (Advance Estimate), Third Quarter 2026 20261029T123000Z"], page.Rejected);
        Assert.Equal(12, page.Rows.Count);
    }

    [Fact]
    public void A_feed_without_a_GDP_or_PCE_event_is_not_recognised()
    {
        const string Trade = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:U.S. International Trade in Goods and Services\\, November 2024\r\n"
            + "DTSTART;VALUE=DATE-TIME:20250107T133000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        Assert.Equal(
            "layout not recognised: no GDP or Personal Income and Outlays event reads",
            Assert.Throws<InvalidOperationException>(() => BeaSchedule.Parse(Trade)).Message);
        Assert.Equal(
            "layout not recognised: no VEVENT",
            Assert.Throws<InvalidOperationException>(() => BeaSchedule.Parse("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n")).Message);
    }

    private static ObservedCatalyst Row(Sources.ParsedCalendar page, string key) => Assert.Single(page.Rows, row => row.SourceKey == key);
}
