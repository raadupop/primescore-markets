using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Sources.Claims;
using PrimeScore.Modules.Ingestion.Contracts;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>The claims release derived by rule, and the adapter's window around the pinned clock (2026-09-29T12:00Z).</summary>
public sealed class ClaimsRuleTests : CatalystDatabase
{
    [Theory]
    [InlineData("2026-10-01", "2026-10-01T12:30:00Z", "CLAIMS:2026-09-26")] // an ordinary Thursday: 08:30 EDT = 12:30Z
    [InlineData("2026-11-26", "2026-11-25T13:30:00Z", "CLAIMS:2026-11-21")] // Thanksgiving: Wednesday, 08:30 EST = 13:30Z
    [InlineData("2025-12-25", "2025-12-24T13:30:00Z", "CLAIMS:2025-12-20")] // Christmas on a Thursday
    [InlineData("2026-01-01", "2025-12-31T13:30:00Z", "CLAIMS:2025-12-27")] // New Year's Day on a Thursday
    [InlineData("2025-06-19", "2025-06-18T12:30:00Z", "CLAIMS:2025-06-14")] // Juneteenth on a Thursday
    [InlineData("2020-06-18", "2020-06-18T12:30:00Z", "CLAIMS:2020-06-13")] // before Juneteenth was a federal holiday
    public void Thursday_08_30_New_York_or_the_Wednesday_before_a_federal_holiday(string thursday, string release, string key)
    {
        var date = DateOnly.Parse(thursday, System.Globalization.CultureInfo.InvariantCulture);

        var row = Assert.Single(ClaimsRule.Releases(date, date));

        Assert.Equal(DateTimeOffset.Parse(release, System.Globalization.CultureInfo.InvariantCulture), row.ScheduledAt);
        Assert.Equal(key, row.SourceKey);
        Assert.Equal(CatalystFamily.Claims, row.Family);
        Assert.Equal("rule", row.Derivation);
        Assert.True(row.TimeAnnounced);
    }

    [Fact]
    public async Task The_adapter_derives_the_Thursdays_from_420_days_before_to_63_days_after_today()
    {
        var result = await Adapter().PullAsync(Token);

        // Today (New York) 2026-09-29. Window start 2026-09-29 − 420 days = 2025-08-05 (a Tuesday), so the first
        // Thursday is 2025-08-07; end 2026-09-29 + 63 days = 2026-12-01, so the last is 2026-11-26.
        // 2025-08-07 → 2026-08-07 is 365 days, + 24 + 30 + 31 + 26 = 476 days = 68 weeks: 69 Thursdays.
        Assert.Equal(new SourceRunCounts(69, 0, 0, 0, 0), result.Counts);
        Assert.Equal("derived by rule; 69 new, 0 rescheduled, 0 unchanged.", result.Note);
        Assert.Equal(69, result.ItemsAttempted);
        Assert.Empty(result.ItemErrors);

        var claims = await ListAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, CatalystFamily.Claims);
        Assert.Equal(69, claims.Count);

        // Claims week ending Saturday 2025-08-02, released Thursday 2025-08-07: before the clock, so back-filled.
        var first = claims[0];
        Assert.Equal("CLAIMS-2025-08-07", first.CatalystId);
        Assert.Equal("week ending 2025-08-02", first.ReferencePeriod);
        Assert.True(first.Backfilled);
        Assert.Null(first.NeverRescheduled);

        // Claims week ending 2026-11-21: Thanksgiving Thursday 2026-11-26, so Wednesday 2026-11-25 08:30 EST = 13:30Z.
        var last = claims[^1];
        Assert.Equal("CLAIMS-2026-11-25", last.CatalystId);
        Assert.Equal("week ending 2026-11-21", last.ReferencePeriod);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 13, 30, 0, TimeSpan.Zero), last.ScheduledAt);
        Assert.Equal("rule", last.Derivation);
        Assert.True(last.NeverRescheduled);

        // No page was fetched: the URL is the rule's identifier and there is no file hash.
        Assert.Equal(
            new CatalystSourceView("ClaimsCalendar", CatalystSourceKind.Rule, "rule:claims-thursday-0830-federal-holiday-wednesday", Now, null),
            last.Source);
    }

    [Fact]
    public async Task A_second_run_on_the_same_day_writes_nothing()
    {
        var adapter = Adapter();
        await adapter.PullAsync(Token);
        var head = await HeadAsync();

        var again = await adapter.PullAsync(Token);

        Assert.Equal(new SourceRunCounts(0, 69, 0, 0, 0), again.Counts);
        Assert.Equal(head, await HeadAsync());
    }

    private ClaimsCalendarAdapter Adapter() =>
        ActivatorUtilities.CreateInstance<ClaimsCalendarAdapter>(Services, Options.Create(new ClaimsCalendarOptions { Enabled = true }));
}
