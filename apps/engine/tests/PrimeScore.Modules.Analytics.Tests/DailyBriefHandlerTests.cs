using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Features;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary><see cref="GetDailyBriefHandler"/> (ADR-0016): the days-in-state run, the grid rule and the composition, against stubs.</summary>
public sealed class DailyBriefHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BriefDate = new(2026, 10, 1); // Thursday

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Days_in_state_count_back_over_trading_days_until_the_state_changes()
    {
        // Thu 10-01 normal (two decisions: the later counts), Wed 09-30 normal, Tue 09-29 normal, Mon 09-28 high.
        var decisions = new[]
        {
            Decision(new DateOnly(2026, 10, 1), 20, "normal", 16.4),
            Decision(new DateOnly(2026, 10, 1), 18, "high", 99),
            Decision(new DateOnly(2026, 9, 30), 20, "normal"),
            Decision(new DateOnly(2026, 9, 29), 20, "normal"),
            Decision(new DateOnly(2026, 9, 28), 20, "high"),
            Decision(new DateOnly(2026, 9, 25), 20, "high"),
        };

        var state = GetDailyBriefHandler.State(decisions, BriefDate);

        Assert.Equal(("normal", 3, "high", 16.4), (state.State, state.DaysInState, state.PreviousState, state.Level));
    }

    [Fact]
    public void A_trading_day_without_a_decision_ends_the_run_without_a_previous_state()
    {
        // 09-30 has no decision; the weekend before 09-28 is skipped by the trading calendar, not counted as a gap.
        var decisions = new[] { Decision(BriefDate, 20, "extreme_high"), Decision(new DateOnly(2026, 9, 29), 20, "extreme_high") };

        var state = GetDailyBriefHandler.State(decisions, BriefDate);

        Assert.Equal((1, (string?)null), (state.DaysInState, state.PreviousState));
    }

    [Fact]
    public void The_grid_ranks_the_latest_close_among_its_prior_closes_with_the_published_state_rule()
    {
        // 300 prior closes 10, 11, ..., 309; the brief-date close 290 is at or above 281 of them: 0.9367, tail 0.0633 > 0.05 → high.
        var closes = Enumerable.Range(0, 300).Select(index => (new DateOnly(2025, 1, 1).AddDays(index), 10.0 + index))
            .Append((BriefDate, 290.0)).Append((BriefDate.AddDays(1), 1.0)).ToArray();

        var row = GetDailyBriefHandler.GridRow("VIX", closes, BriefDate);
        Assert.Equal((BriefDate, 290.0, 300, "high"), (row.Date!.Value, row.Close!.Value, row.PriorCloses, row.State));
        Assert.Equal(281 / 300.0, row.Percentile!.Value, 12);

        // Above every prior close → percentile 1, extreme_high; fewer than 252 prior closes → unknown.
        Assert.Equal("extreme_high", GetDailyBriefHandler.GridRow("VIX", [.. closes[..300], (BriefDate, 999.0)], BriefDate).State);
        Assert.Equal("unknown", GetDailyBriefHandler.GridRow("VIX", [.. closes[..100], (BriefDate, 999.0)], BriefDate).State);
        Assert.Equal((null, "unknown"), (GetDailyBriefHandler.GridRow("GVZ", [], BriefDate).Date, GetDailyBriefHandler.GridRow("GVZ", [], BriefDate).State));
    }

    [Fact]
    public async Task The_brief_lists_releases_ahead_with_their_record_what_just_passed_and_what_is_new()
    {
        var calendar = new[]
        {
            Catalyst(CatalystFamily.Nfp, BriefDate, new TimeOnly(8, 30), firstSeen: Now.AddDays(-30)),             // just passed (closes 10-01)
            Catalyst(CatalystFamily.Cpi, new DateOnly(2026, 10, 14), new TimeOnly(8, 30), firstSeen: Now.AddDays(-30)), // 9 trading days ahead
            Catalyst(CatalystFamily.Fomc, new DateOnly(2026, 10, 28), new TimeOnly(14, 0), firstSeen: Now.AddHours(-2)), // beyond 10 trading days; new
            Catalyst(CatalystFamily.Wpsr, new DateOnly(2026, 10, 7), new TimeOnly(10, 30), firstSeen: Now.AddDays(-30)), // ahead, oil
        };
        var handler = new GetDailyBriefHandler(new StubSettings(), new StubDecisions(), new StubSeries(), new StubCalendar(calendar), new StubRatios(), new StubRecord(), new FixedClock(Now));

        var brief = await handler.HandleAsync(new GetDailyBrief("equity"), Token);

        Assert.Null(brief.Error);
        Assert.Equal((BriefDate, "VIX"), (brief.BriefDate!.Value, brief.ReferenceInstrument));
        Assert.Equal(["WPSR-2026-10-07", "CPI-2026-10-14"], brief.Ahead.Select(item => item.CatalystId));
        Assert.Equal(-0.1, brief.Ahead[1].Ratio);
        Assert.Equal(["CPI", "WPSR"], brief.Records.Select(record => record.Family));
        Assert.Equal((false, true), (brief.Records[0].Oil, brief.Records[1].Oil));
        Assert.Equal(7, brief.Records[0].Latest12.InsidePricedRange);
        Assert.Equal("NFP-2026-10-01", Assert.Single(brief.Passed).Outcome.CatalystId);
        Assert.Equal(("FOMC-2026-10-28", 1), (Assert.Single(brief.NewOnCalendar).CatalystId, brief.NewOnCalendarTotal));
        // VIX, the equity reference, is the state line, not a grid row.
        Assert.Equal(["VXN", "RVX", "VVIX", "OVX", "GVZ"], brief.Grid.Select(row => row.Instrument));
        Assert.All(brief.Grid, row => Assert.Equal("unknown", row.State)); // two closes each: no history

        var unknown = await handler.HandleAsync(new GetDailyBrief("crypto"), Token);
        Assert.Contains("Unknown market", unknown.Error, StringComparison.Ordinal);
    }

    private static DecisionView Decision(DateOnly date, int hour, string state, double level = 20) =>
        new(Guid.NewGuid(), 1, CorrelationId.New(), new ConfigVersion(3), "equity", DecisionOutcome.Idle, state, Guid.NewGuid(), 0, Guid.NewGuid(), 0, 0,
            "VIX", level, level, Guid.NewGuid(), [], [], [], "", MarketTime.AtNewYork(date, new TimeOnly(hour, 15)), Now, 0.5);

    private static CatalystView Catalyst(CatalystFamily family, DateOnly date, TimeOnly time, DateTimeOffset firstSeen)
    {
        var id = $"{family.ToString().ToUpperInvariant()}-{date:yyyy-MM-dd}";
        return new CatalystView(id, family, id, null, MarketTime.AtNewYork(date, time), date, true, CatalystStatus.Scheduled, null, false, 1, true, false, null,
            firstSeen, null, new CatalystSourceView("test", CatalystSourceKind.Listing, "https://example.invalid/", Now, null), 7, "hash");
    }

    /// <summary>Only the equity context matters to the brief: its name and reference instrument.</summary>
    private sealed class StubSettings : IQueryHandler<GetActiveSettings, SettingsVersion>
    {
        public Task<SettingsVersion> HandleAsync(GetActiveSettings query, CancellationToken cancellationToken)
        {
            var equity = new ContextSettings("equity", "VIX", new Dictionary<string, IReadOnlyList<string>> { ["MARKET_DATA"] = ["VIX"] }, 1.5,
                new SensitivityMap(1, 0.75, 0.5), new RegimeRule(RegimeMode.Percentile, 0.3, 0.7, null, null));
            var settings = new EngineSettings(new WeightingSettings("test", new Dictionary<string, double>(), Aggregation.MaxConfirmedWeighted), 0.999,
                new Dictionary<string, WindowSpan>(), new Dictionary<string, WindowSpan>(), [], [equity], "test");
            return Task.FromResult(new SettingsVersion(new ConfigVersion(3), 1, Now, "test", "test", settings, []));
        }
    }

    private sealed class StubDecisions : IQueryHandler<GetDecisions, IReadOnlyList<DecisionView>>
    {
        public Task<IReadOnlyList<DecisionView>> HandleAsync(GetDecisions query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DecisionView>>([Decision(BriefDate, 20, "normal"), Decision(new DateOnly(2026, 9, 30), 20, "high")]);
    }

    private sealed class StubSeries : IQueryHandler<GetObservationSeries, ObservationSeries>
    {
        public Task<ObservationSeries> HandleAsync(GetObservationSeries query, CancellationToken cancellationToken) =>
            Task.FromResult(new ObservationSeries(query.SourcePrefix != "cboe:" ? [] :
            [
                new ObservationPoint(MarketTime.AtNewYork(new DateOnly(2026, 9, 30), new TimeOnly(16, 15)), 18, Guid.NewGuid(), 1),
                new ObservationPoint(MarketTime.AtNewYork(BriefDate, new TimeOnly(16, 15)), 19, Guid.NewGuid(), 2),
            ]));
    }

    private sealed class StubCalendar(IReadOnlyList<CatalystView> catalysts) : IQueryHandler<GetCatalysts, CatalystList>
    {
        public Task<CatalystList> HandleAsync(GetCatalysts query, CancellationToken cancellationToken) => Task.FromResult(new CatalystList(catalysts, Now));
    }

    private sealed class StubRatios : IQueryHandler<GetCatalystRatios, CatalystRatioList>
    {
        public Task<CatalystRatioList> HandleAsync(GetCatalystRatios query, CancellationToken cancellationToken) =>
            Task.FromResult(new CatalystRatioList(query.CatalystIds.Select(id => new CatalystRatioReading(id, BriefDate, -0.1, 1, true, 0.2, 50, 60, 5, 1, 0, [], null)).ToArray(), Now));
    }

    private sealed class StubRecord : IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport>
    {
        public Task<CatalystOutcomeReport> HandleAsync(GetCatalystOutcomes query, CancellationToken cancellationToken)
        {
            var summary = new CatalystOutcomeSummary(12, 0.025, 0.014, 7, 6, 0.005, -0.5, 8);
            IReadOnlyList<CatalystOutcome> rows = query.Family == "NFP"
                ? [new("NFP-2026-10-01", Now, true, new DateOnly(2026, 9, 30), BriefDate, new DateOnly(2026, 10, 9), true, -0.1, 0.024, null, null, null, 0.004, -1.2, 1, false, true)]
                : [];
            return Task.FromResult(new CatalystOutcomeReport(query.Family, [], rows, 0, 0, 0, summary, summary, Now));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
