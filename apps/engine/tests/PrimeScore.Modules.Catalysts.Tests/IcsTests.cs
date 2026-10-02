using PrimeScore.Modules.Catalysts.Sources;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>The RFC 5545 subset the BEA feed uses: folding, TEXT escapes, VEVENT and UTC DTSTART.</summary>
public sealed class IcsTests
{
    [Fact]
    public void Folded_lines_are_joined_before_escapes_are_read()
    {
        // BEA folds at 75 octets, here between the backslash and the comma it escapes.
        var events = Ics.ReadEvents(Calendar(
            "SUMMARY:GDP (Third Estimate)\\, Industries\\, Corporate Profits\\, State GDP\\\r\n , and State Personal Income\\, 2nd Quarter 2026\\; State PCE\\, 2025",
            "DTSTART:20260930T123000Z"));

        Assert.Equal(
            "GDP (Third Estimate), Industries, Corporate Profits, State GDP, and State Personal Income, 2nd Quarter 2026; State PCE, 2025",
            Assert.Single(events).Text("SUMMARY"));
    }

    [Fact]
    public void A_continuation_line_keeps_the_text_after_its_one_leading_space()
    {
        var events = Ics.ReadEvents(Calendar("SUMMARY:Gross Domestic Product by State\\, 4th\r\n  Quarter 2024", "DTSTART:20250328T140000Z"));

        Assert.Equal("Gross Domestic Product by State, 4th Quarter 2024", Assert.Single(events).Text("SUMMARY"));
    }

    [Fact]
    public void Text_escapes_are_undone()
    {
        Assert.Equal("a\\b,c;d\ne\nf", Ics.Unescape("a\\\\b\\,c\\;d\\ne\\Nf"));
    }

    [Fact]
    public void DTSTART_is_read_only_as_a_UTC_date_time()
    {
        var events = Ics.ReadEvents(
            Calendar("SUMMARY:a", "DTSTART;VALUE=DATE-TIME:20250130T133000Z")
            + Calendar("SUMMARY:b", "DTSTART:20251205T150000Z")
            + Calendar("SUMMARY:c", "DTSTART;TZID=America/New_York:20251205T100000")
            + Calendar("SUMMARY:d", "DTSTART;VALUE=DATE:20251205")
            + Calendar("SUMMARY:e", "DTSTART:20251205T100000"));

        Assert.Equal(new DateTimeOffset(2025, 1, 30, 13, 30, 0, TimeSpan.Zero), events[0].StartUtc);
        Assert.Equal(new DateTimeOffset(2025, 12, 5, 15, 0, 0, TimeSpan.Zero), events[1].StartUtc);

        // A zoned, all-day or floating start is not guessed.
        Assert.Null(events[2].StartUtc);
        Assert.Null(events[3].StartUtc);
        Assert.Null(events[4].StartUtc);
    }

    [Fact]
    public void Properties_of_a_nested_component_do_not_replace_the_events_own()
    {
        var events = Ics.ReadEvents(Calendar(
            "SUMMARY:outer", "BEGIN:VALARM", "SUMMARY:inner", "DTSTART:20990101T000000Z", "END:VALARM", "DTSTART:20261029T123000Z"));

        var single = Assert.Single(events);
        Assert.Equal("outer", single.Text("SUMMARY"));
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 12, 30, 0, TimeSpan.Zero), single.StartUtc);
    }

    [Fact]
    public void Text_that_is_not_a_calendar_is_not_recognised()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Ics.ReadEvents("<html><body>Just a moment...</body></html>"));

        Assert.Equal("layout not recognised: not an iCalendar file (no BEGIN:VCALENDAR)", error.Message);
    }

    /// <summary>A calendar with one VEVENT holding <paramref name="lines"/>; CRLF line ends as BEA serves them.</summary>
    private static string Calendar(params string[] lines) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\n" + string.Concat(lines.Select(line => line + "\r\n")) + "END:VEVENT\r\nEND:VCALENDAR\r\n";
}
