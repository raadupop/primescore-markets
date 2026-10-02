using System.Net;
using System.Text.RegularExpressions;
using PrimeScore.Acceptance.Api.Harness;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// The daily brief (SRS ANA-004, ADR-0016) on the Volmageddon history, through the page only: no API serves it
/// while Cboe-derived figures stay in personal research use (ADR-0015).
/// </summary>
public sealed class BriefTests(VolmageddonEngine fixture) : IClassFixture<VolmageddonEngine>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The latest decision is the 2018-02-05 close: VIX 37.32 at or above 1,259 of 1,260 prior closes (99.9th percentile),
    /// extremely stretched; the 2018-02-02 close (17.31, percentile 0.8594) was stretched, so one day in the state.
    /// No calendar is recorded and the history was submitted through the API, not by the Cboe adapter, so the grid is unknown.
    /// </summary>
    [Fact]
    public async Task UI_the_equity_brief_states_the_level_the_days_in_state_and_that_nothing_is_scheduled()
    {
        using var http = fixture.Engine.Http(Role.Read);
        var text = PageText(await http.GetStringAsync(new Uri(fixture.Engine.ApiBase, "/brief?market=equity"), Token));

        foreach (var expected in new[]
        {
            "close of 2018-02-05",
            "VIX 37.32, 99.9th percentile of its prior closes: Extremely stretched, 1 day in this state (before: Stretched)",
            "No scheduled release in the next 10 trading days.",
            "No scheduled release closed on this date.",
            "Nothing new.",
            "For your own research only",
            "Personal research only; derived from Cboe data (ADR-0015).",
        })
        {
            // On failure, show the brief's part of the page rather than its first characters.
            var start = Math.Max(0, text.IndexOf("Daily brief", StringComparison.Ordinal));
            Assert.True(text.Contains(expected, StringComparison.Ordinal), $"Missing \"{expected}\" in: {text[start..Math.Min(text.Length, start + 1500)]}");
        }

        // Grid rows exist for every index and say "Too little history" without Cboe closes.
        var rows = Rows(await http.GetStringAsync(new Uri(fixture.Engine.ApiBase, "/brief"), Token));
        Assert.DoesNotMatch(@"\| VIX \|", rows); // the equity reference is the state line
        foreach (var instrument in new[] { "VXN", "RVX", "VVIX", "OVX", "GVZ" })
        {
            Assert.Matches($@"\| {instrument} \| — \| — \| — of 0 prior closes \| Too little history", rows);
        }
    }

    private static string Rows(string html) => string.Join("\n", Regex.Matches(html, "<tr>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant)
        .Select(row => "| " + string.Join(" | ", Regex.Matches(row.Value, "<td[^>]*>(.*?)</td>", RegexOptions.Singleline).Select(cell => PageText(cell.Groups[1].Value).Trim()))));

    /// <summary>Visible text; tags become spaces, so " ," left by a closing tag before a comma is joined back.</summary>
    private static string PageText(string html) => Regex.Replace(
        WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant)),
        @"\s+", " ", RegexOptions.CultureInvariant).Replace(" ,", ",", StringComparison.Ordinal);
}
