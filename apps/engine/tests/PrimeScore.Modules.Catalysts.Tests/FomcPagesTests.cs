using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.Modules.Catalysts.Sources;
using PrimeScore.Modules.Catalysts.Sources.Fed;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>The Federal Reserve's FOMC calendars, read from excerpts of the pages as fetched on 2026-09-29.</summary>
public sealed class FomcPagesTests
{
    private static readonly string Current = CalendarFixtures.Text("fed/fomccalendars-excerpt.htm");

    [Fact]
    public void The_current_page_lists_eight_2026_meetings_decided_at_14_00_New_York_on_the_last_day()
    {
        var page = FomcPages.ParseCurrent(Current);

        var meetings2026 = Year(page, 2026);
        Assert.Equal(8, meetings2026.Count);

        // 28 October 2026 is in daylight time (UTC-4): 14:00 + 4 h = 18:00Z. 9 December is in standard time (UTC-5): 19:00Z.
        var october = Assert.Single(meetings2026, row => row.ScheduledAt.Month == 10);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 18, 0, 0, TimeSpan.Zero), october.ScheduledAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 9, 19, 0, 0, TimeSpan.Zero), meetings2026[^1].ScheduledAt);
        Assert.All(page.Rows, row =>
        {
            Assert.Equal(CatalystFamily.Fomc, row.Family);
            Assert.True(row.TimeAnnounced);
            Assert.Null(row.SourceKey);
            Assert.Equal(CatalystStatus.Scheduled, row.Status);
        });

        // "15-16*" carries the SEP marker; "27-28" does not.
        Assert.True(Assert.Single(meetings2026, row => row.ScheduledAt.Month == 9).Sep);
        Assert.False(october.Sep);
        Assert.Equal("FOMC meeting October 27-28, 2026", october.Title);
    }

    [Fact]
    public void Only_the_panel_with_the_tentative_note_is_tentative_and_the_coverage_spans_its_panel_years()
    {
        var page = FomcPages.ParseCurrent(Current);

        Assert.All(Year(page, 2027), row => Assert.True(row.Tentative));
        Assert.All(Year(page, 2026).Concat(Year(page, 2025)), row => Assert.False(row.Tentative));
        Assert.Equal(8, Year(page, 2027).Count);

        // Panels 2026, 2025 and 2027: from 1 January of the first year to 31 December of the last.
        Assert.Equal(new DateOnly(2025, 1, 1), page.CoverageFrom);
        Assert.Equal(new DateOnly(2027, 12, 31), page.CoverageTo);
    }

    [Fact]
    public void A_notation_vote_is_skipped_and_counted()
    {
        var page = FomcPages.ParseCurrent(Current);

        // 2025: eight meetings plus "22 (notation vote)" in August.
        Assert.Equal(["notation vote"], page.Skipped);
        Assert.Equal(8, Year(page, 2025).Count);
        Assert.DoesNotContain(page.Rows, row => MarketTime.NewYorkDate(row.ScheduledAt) == new DateOnly(2025, 8, 22));
        Assert.Empty(page.Rejected);
        Assert.Equal(25, page.RowsExamined);
    }

    [Fact]
    public void A_meeting_across_two_months_is_decided_on_the_first_day_of_the_second()
    {
        var html = Page(Panel(2024, ("Apr/May", "30-1"), ("July", "30-31")));

        var page = FomcPages.ParseCurrent(html);

        // 1 May 2024, daylight time: 18:00Z.
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 18, 0, 0, TimeSpan.Zero), page.Rows[0].ScheduledAt);
        Assert.Equal("FOMC meeting April 30-May 1, 2024", page.Rows[0].Title);
    }

    [Fact]
    public void A_cancelled_or_unscheduled_meeting_on_the_current_page_is_kept_cancelled_or_skipped()
    {
        var page = FomcPages.ParseCurrent(Page(Panel(2026, ("March", "17-18 (cancelled)"), ("March", "20 (unscheduled)"), ("April", "28-29"))));

        Assert.Equal(CatalystStatus.Cancelled, page.Rows[0].Status);
        Assert.Equal(CatalystStatus.Scheduled, page.Rows[1].Status);
        Assert.Equal(["unscheduled meeting"], page.Skipped);
    }

    [Fact]
    public void A_row_in_a_changed_format_is_quoted_and_the_others_are_read()
    {
        // Only the October 2026 block: the first "27-28" after <strong>October</strong> (January also reads 27-28).
        var html = CalendarFixtures.ReplaceAfter(Current, "<strong>October</strong>", "27-28", "27 to 28");

        var page = FomcPages.ParseCurrent(html);

        Assert.Equal(["2026 October 27 to 28"], page.Rejected);
        Assert.Equal(23, page.Rows.Count);
        Assert.Contains(page.Rows, row => row.ScheduledAt == new DateTimeOffset(2026, 1, 28, 19, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_page_without_a_panel_or_with_a_panel_of_unreadable_rows_is_not_recognised()
    {
        var noPanel = Assert.Throws<InvalidOperationException>(() => FomcPages.ParseCurrent("<html><body><h4>Meetings</h4></body></html>"));
        var unreadable = Assert.Throws<InvalidOperationException>(() => FomcPages.ParseCurrent(Page(Panel(2026, ("Octobre", "27-28"), ("October", "twenty")))));

        Assert.StartsWith("layout not recognised", noPanel.Message, StringComparison.Ordinal);
        Assert.Equal("layout not recognised: no meeting row of the 2026 panel parses", unreadable.Message);
    }

    [Fact]
    public void The_2013_historical_page_gives_eight_meetings_with_SEP_from_the_projection_materials()
    {
        var page = FomcPages.ParseHistorical(CalendarFixtures.Text("fed/fomchistorical2013-excerpt.htm"));

        Assert.Equal(8, page.Rows.Count);
        Assert.Equal(["unscheduled meeting"], page.Skipped); // October 16 (unscheduled)
        Assert.Null(page.CoverageFrom);

        // "April/May 30-1": decided 1 May 2013, daylight time, 18:00Z.
        Assert.Equal(new DateTimeOffset(2013, 5, 1, 18, 0, 0, TimeSpan.Zero), page.Rows[2].ScheduledAt);

        // SEP: individual projections were published for March, June, September and December.
        var sepMonths = page.Rows.Where(row => row.Sep == true).Select(row => row.ScheduledAt.Month).ToArray();
        Assert.Equal([3, 6, 9, 12], sepMonths);
        Assert.False(page.Rows[0].Sep);
        Assert.All(page.Rows, row => Assert.False(row.Tentative));

        // 18 December 2013 is in standard time: 14:00 + 5 h.
        Assert.Equal(new DateTimeOffset(2013, 12, 18, 19, 0, 0, TimeSpan.Zero), page.Rows[^1].ScheduledAt);
    }

    [Fact]
    public void The_2020_historical_page_keeps_the_cancelled_March_meeting_and_skips_notation_votes_and_unscheduled_meetings()
    {
        var page = FomcPages.ParseHistorical(CalendarFixtures.Text("fed/fomchistorical2020-excerpt.htm"));

        // January 28-29, March 17-18 (cancelled), April 28-29; March 2 and 15 unscheduled; March 19, 23, 31 notation votes.
        Assert.Equal(3, page.Rows.Count);
        var march = page.Rows[1];
        Assert.Equal(CatalystStatus.Cancelled, march.Status);

        // 18 March 2020: daylight time began 8 March, so 18:00Z.
        Assert.Equal(new DateTimeOffset(2020, 3, 18, 18, 0, 0, TimeSpan.Zero), march.ScheduledAt);
        Assert.Equal(2, page.Skipped.Count(label => label == "unscheduled meeting"));
        Assert.Equal(3, page.Skipped.Count(label => label == "notation vote"));
    }

    [Fact]
    public void A_historical_page_without_meeting_headings_is_not_recognised()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => FomcPages.ParseHistorical("<html><body><h3>2013</h3></body></html>"));

        Assert.Equal("layout not recognised: no meeting heading", exception.Message);
    }

    private static List<ObservedCatalyst> Year(ParsedCalendar page, int year) =>
        page.Rows.Where(row => MarketTime.NewYorkDate(row.ScheduledAt).Year == year).OrderBy(row => row.ScheduledAt).ToList();

    private static string Page(string panels) => $"<!doctype html><html><body>{panels}</body></html>";

    /// <summary>A year panel in the current page's markup (copied from the 2026 panel) with invented rows.</summary>
    private static string Panel(int year, params (string Month, string Date)[] rows) =>
        $"""<div class="panel panel-default"><div class="panel-heading"><h4><a id="1">{year} FOMC Meetings</a></h4></div>"""
        + string.Concat(rows.Select(row => $"""
            <div class="row fomc-meeting" ">
                <div class="fomc-meeting__month col-xs-5 col-sm-3 col-md-2"><strong>{row.Month}</strong></div>
                <div class="fomc-meeting__date col-xs-4 col-sm-9 col-md-10 col-lg-1">{row.Date}</div>
                <div class="col-xs-12 col-md-4 col-lg-2"></div>
            </div>
            """))
        + "<div class=\"panel-footer\">* Meeting associated with a Summary of Economic Projections.</div></div>";
}
