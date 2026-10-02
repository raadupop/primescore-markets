using System.Globalization;
using System.Text.RegularExpressions;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// The outcomes record (ADR-0008) through the public API only: it must agree with the decisions the
/// same API lists, and it must never measure a horizon that has no later close.
/// </summary>
public sealed class OutcomesTests(VolmageddonEngine fixture) : IClassFixture<VolmageddonEngine>
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_outcomes_record_counts_the_same_extreme_days_as_the_decision_journal_and_leaves_the_last_close_unmeasured()
    {
        var client = fixture.Engine.Client(Role.Read);

        var report = await client.GetForwardOutcomesAsync(null, null, "equity", Token);
        var decisions = await client.ListDecisionsAsync(null, null, Token);

        // One state per New York date: the last decision recorded for it.
        var lastPerDate = decisions
            .GroupBy(decision => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(decision.Decided_at, NewYork).DateTime))
            .Select(day => day.OrderBy(decision => decision.Decided_at).Last())
            .ToArray();
        // The fixture stamps its history on business days, so NYSE holidays among them are left out and counted.
        Assert.Equal(("equity", "VIX"), (report.Context, report.Reference_instrument));
        Assert.Equal(lastPerDate.Length, report.Days + report.Excluded_days);
        Assert.InRange(report.Excluded_days, 1, 60);
        Assert.Equal((0, 0), (report.Legacy_days, report.Other_instrument_days));
        Assert.InRange(report.Deploy_days, 1, lastPerDate.Count(decision => decision.Outcome == DecisionOutcome.DEPLOY && decision.State is "extreme_high" or "extreme_low"));
        Assert.Equal(new DateOnly(2018, 2, 5), report.Last_date);
        Assert.Equal([1, 5, 10, 21], report.Horizons.Select(horizon => horizon.Trading_days));

        // 5 February 2018 is the last close: no 1-day change exists for it, so one date fewer is measured.
        Assert.Equal(report.Days - 1, report.Horizons.First().All_count);
        Assert.Equal(report.Days - 21, report.Horizons.Last().All_count);
        Assert.Contains(report.States, state => state.State == "extreme_high");
    }

    /// <summary>
    /// The Outcomes record page (slice 1) shows the counts the API returns, dates in yyyy-MM-dd
    /// (the server culture once printed 1/4/2016), and the in-sample caveat beside the figures.
    /// </summary>
    [Fact]
    public async Task UI_outcomes_record_shows_the_API_counts_with_ISO_dates_and_the_in_sample_caveat()
    {
        var engine = fixture.Engine;
        var report = await engine.Client(Role.Read).GetForwardOutcomesAsync(null, null, "equity", Token);
        using var http = engine.Http(Role.Read);
        var text = PageText(await http.GetStringAsync(new Uri(engine.ApiBase, "/outcomes?context=equity"), Token));

        Assert.Contains($"Decision days {Count(report.Days)}", text, StringComparison.Ordinal);
        Assert.Contains($"{report.First_date!.Value:yyyy-MM-dd} → 2018-02-05", text, StringComparison.Ordinal);
        Assert.Contains($"{Count(report.Excluded_days)} left out", text, StringComparison.Ordinal);
        Assert.Contains($"All checks passed {Count(report.Deploy_days)}", text, StringComparison.Ordinal);
        foreach (var horizon in report.Horizons)
        {
            Assert.Contains($"{horizon.Trading_days} trading day", text, StringComparison.Ordinal);
            Assert.Contains($"{Count(horizon.Deploy_count)} of {Count(horizon.All_count)}", text, StringComparison.Ordinal);
            if (horizon.Deploy_revert_share is { } share)
            {
                Assert.Contains((share * 100).ToString("N0", CultureInfo.InvariantCulture) + "%", text, StringComparison.Ordinal);
            }
        }

        Assert.Contains("not a tradable edge", text, StringComparison.Ordinal);
        Assert.Contains("34 to 41 percent", text, StringComparison.Ordinal);
        Assert.Contains("Extremely stretched", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_context_is_a_404()
    {
        var missing = await Assert.ThrowsAnyAsync<PrimeScoreApiException>(() =>
            fixture.Engine.Client(Role.Read).GetForwardOutcomesAsync(null, null, "no-such-market", Token));

        Assert.Equal(404, missing.StatusCode);
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string PageText(string html) => Regex.Replace(
        System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant)),
        @"\s+", " ", RegexOptions.CultureInvariant);
}
