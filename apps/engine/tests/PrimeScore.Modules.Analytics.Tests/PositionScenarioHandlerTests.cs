using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Features;
using PrimeScore.Modules.Analytics.Positions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>
/// <see cref="GetPositionScenariosHandler"/> (ADR-0014) against stubs. Closes (invented) end Thu 2026-09-24: SPX 4900,
/// VIX9D 18.9, VIX 20, VIX3M 22, VIX6M 23. The FOMC record holds two past meetings: event-day move +1% with VIX9D −6,
/// and −2% with VIX9D +2. Prices come from <see cref="OptionPricing"/>, whose values <see cref="OptionPricingTests"/> checks.
/// </summary>
public sealed class PositionScenarioHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly AsOf = new(2026, 9, 24);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A long SPX 5000 straddle expiring Fri 2026-10-30: 36 days, σ = 0.205102 (OptionPricingTests), one FOMC before expiry.</summary>
    [Fact]
    public async Task A_long_straddle_is_valued_and_the_October_FOMC_is_replayed_with_both_past_meetings()
    {
        var position = new PositionInput("SPX", new DateOnly(2026, 10, 30), [new("C", 5000, 1), new("P", 5000, 1)], EntryCost: 30_000);

        var report = await Handler().HandleAsync(new GetPositionScenarios(position), Token);

        Assert.Null(report.Error);
        Assert.Equal((AsOf, 4900.0, 36), (report.AsOf!.Value, report.UnderlyingLevel!.Value, report.DaysToExpiry!.Value));
        Assert.Equal(0.205102, report.ImpliedVolatility!.Value, 6);
        double Book(double level, double sigma, int days) =>
            100 * (OptionPricing.Price(true, level, 5000, days / 365.0, sigma, 0.04, 0.013) + OptionPricing.Price(false, level, 5000, days / 365.0, sigma, 0.04, 0.013));
        var sigma = report.ImpliedVolatility.Value;
        Assert.Equal(Book(4900, sigma, 36), report.Value!.Value, 6);
        Assert.Equal(Book(4900, sigma, 35) - Book(4900, sigma, 36), report.Theta!.Value, 6);
        Assert.Equal(100 * 2 * OptionPricing.Vega(4900, 5000, 36 / 365.0, sigma, 0.04, 0.013) / 100, report.Vega!.Value, 6);
        // A long straddle loses at most what it cost.
        Assert.Equal((30_000.0, 30_000.0, false), (report.EntryCost!.Value, report.MaxLossAtExpiry!.Value, report.UnlimitedLoss));

        // FOMC-2026-10-28 closes on 10-28, 34 days ahead, leaving 2 days: the VIX9D change applies in full (≤ 9 days).
        // FOMC-2026-12-09 is after expiry; the CPI on 10-14 has no record rows here (n = 0).
        Assert.Equal(["CPI-2026-10-14", "FOMC-2026-10-28"], report.Events.Select(item => item.CatalystId));
        var fomc = report.Events[1];
        Assert.Equal((new DateOnly(2026, 10, 28), 34, 2), (fomc.EventCloseDate, fomc.DaysFromAsOf, fomc.Scenarios));
        var before = Book(4900, sigma, 2);
        Assert.Equal(before - report.Value.Value, fomc.DecayToEvent, 6);
        var first = Book(4900 * 1.01, sigma - 0.06, 2) - before;
        var second = Book(4900 * 0.98, sigma + 0.02, 2) - before;
        Assert.Equal((first + second) / 2, fomc.MeanTotal!.Value, 6);
        Assert.Equal((first + second) / 2, fomc.MedianTotal!.Value, 6);
        Assert.Equal(((Book(4949, sigma, 2) - before) + (Book(4802, sigma, 2) - before)) / 2, fomc.MeanMovePart!.Value, 6);
        Assert.Equal(((Book(4900, sigma - 0.06, 2) - before) + (Book(4900, sigma + 0.02, 2) - before)) / 2, fomc.MeanVolatilityPart!.Value, 6);
        Assert.Equal(Math.Min(first, second), fomc.Worst!.Value, 6);
        Assert.Equal(Math.Max(first, second), fomc.Best!.Value, 6);
        Assert.Equal(new[] { first, second }.Count(total => total < 0) / 2.0, fomc.LossShare!.Value, 12);
        Assert.Equal(0, report.Events[0].Scenarios);
        Assert.Null(report.Events[0].MeanTotal);
    }

    [Fact]
    public async Task SPY_is_valued_at_a_tenth_of_SPX_and_invalid_input_is_explained_not_valued()
    {
        var spy = await Handler().HandleAsync(new GetPositionScenarios(new PositionInput("SPY", new DateOnly(2026, 10, 30), [new("C", 500, 1)])), Token);
        Assert.Equal(490, spy.UnderlyingLevel!.Value, 12);

        foreach (var (position, message) in new (PositionInput, string)[]
        {
            (new("QQQ", new DateOnly(2026, 10, 30), [new("C", 500, 1)]), "SPX or SPY"),
            (new("SPX", new DateOnly(2026, 10, 30), []), "one to 4 legs"),
            (new("SPX", new DateOnly(2026, 10, 30), [new("X", 5000, 1)]), "call (C) or a put (P)"),
            (new("SPX", new DateOnly(2026, 10, 30), [new("C", -1, 1)]), "positive number"),
            (new("SPX", new DateOnly(2026, 10, 30), [new("C", 5000, 0)]), "non-zero"),
            (new("SPX", AsOf, [new("C", 5000, 1)]), "after the latest close (2026-09-24)"),
            (new("SPX", new DateOnly(2030, 1, 18), [new("C", 5000, 1)]), "within three years"),
        })
        {
            var report = await Handler().HandleAsync(new GetPositionScenarios(position), Token);
            Assert.Contains(message, report.Error, StringComparison.Ordinal);
            Assert.Null(report.Value);
            Assert.Equal(AsOf, report.AsOf);
        }
    }

    [Theory]
    [InlineData(-1, 0, -1_000.0, true, null)]           // short call, 1,000 received: unlimited
    [InlineData(0, -1, -1_000.0, false, 499_000.0)]     // short 5000 put, 1,000 received: at zero it owes 500,000
    [InlineData(1, -1, 1_000.0, false, 501_000.0)]      // synthetic long (long call, short put), cost 1,000: at zero −500,000
    [InlineData(1, 1, 30_000.0, false, 30_000.0)]       // long straddle: at most its cost
    public void The_maximum_loss_at_expiry_is_found_at_zero_or_a_strike(int calls, int puts, double cost, bool unlimited, double? maxLoss)
    {
        var legs = new List<PositionLeg>();
        if (calls != 0) { legs.Add(new("C", 5000, calls)); }
        if (puts != 0) { legs.Add(new("P", 5000, puts)); }

        var (loss, isUnlimited) = GetPositionScenariosHandler.MaxLoss(new PositionInput("SPX", new DateOnly(2026, 10, 30), legs), cost);

        Assert.Equal((unlimited, maxLoss), (isUnlimited, loss));
    }

    [Theory]
    [InlineData(-6, 2, -6)]
    [InlineData(-6, 9, -6)]
    [InlineData(-6, 36, -3)]   // √(9/36) = 0.5
    [InlineData(-6, 0, 0)]
    public void A_past_VIX9D_change_is_carried_by_the_square_root_of_time(double change, int days, double expected)
    {
        Assert.Equal(expected, GetPositionScenariosHandler.VolatilityShift(change, days), 12);
    }

    private static GetPositionScenariosHandler Handler()
    {
        var calendar = new[]
        {
            Catalyst(CatalystFamily.Cpi, new DateOnly(2026, 10, 14), new TimeOnly(8, 30)),
            Catalyst(CatalystFamily.Fomc, new DateOnly(2026, 10, 28), new TimeOnly(14, 0)),
            Catalyst(CatalystFamily.Fomc, new DateOnly(2026, 12, 9), new TimeOnly(14, 0)),
            Catalyst(CatalystFamily.Wpsr, new DateOnly(2026, 10, 7), new TimeOnly(10, 30)),
        };
        return new GetPositionScenariosHandler(new StubCalendar(calendar), new StubSeries(), new StubRecord(), new FixedClock(Now));
    }

    private static CatalystView Catalyst(CatalystFamily family, DateOnly date, TimeOnly time)
    {
        var id = $"{family.ToString().ToUpperInvariant()}-{date:yyyy-MM-dd}";
        return new CatalystView(id, family, id, null, MarketTime.AtNewYork(date, time), date, true, CatalystStatus.Scheduled, null, false, 1, true, false, null,
            Now, null, new CatalystSourceView("test", CatalystSourceKind.Listing, "https://example.invalid/", Now, null), 1, "hash");
    }

    private sealed class StubCalendar(IReadOnlyList<CatalystView> catalysts) : IQueryHandler<GetCatalysts, CatalystList>
    {
        public Task<CatalystList> HandleAsync(GetCatalysts query, CancellationToken cancellationToken) => Task.FromResult(new CatalystList(catalysts, Now));
    }

    private sealed class StubSeries : IQueryHandler<GetObservationSeries, ObservationSeries>
    {
        public Task<ObservationSeries> HandleAsync(GetObservationSeries query, CancellationToken cancellationToken)
        {
            double? close = query.SourcePrefix != "cboe:" ? null : query.Instrument switch
            {
                "SPX" => 4900,
                "VIX9D" => 18.9,
                "VIX" => 20,
                "VIX3M" => 22,
                "VIX6M" => 23,
                _ => null,
            };
            return Task.FromResult(new ObservationSeries(close is { } value
                ? [new ObservationPoint(MarketTime.AtNewYork(new DateOnly(2026, 9, 23), new TimeOnly(16, 15)), value * 1.1, Guid.NewGuid(), 1),
                   new ObservationPoint(MarketTime.AtNewYork(AsOf, new TimeOnly(16, 15)), value, Guid.NewGuid(), 2)]
                : []));
        }
    }

    /// <summary>Two past FOMC meetings; any other family has an empty record.</summary>
    private sealed class StubRecord : IQueryHandler<GetCatalystOutcomes, CatalystOutcomeReport>
    {
        public Task<CatalystOutcomeReport> HandleAsync(GetCatalystOutcomes query, CancellationToken cancellationToken)
        {
            var none = new CatalystOutcomeSummary(0, null, null, null, null, null, null, 0);
            IReadOnlyList<CatalystOutcome> rows = query.Family == "FOMC"
                ? [Row("FOMC-2026-09-16", 0.01, -6), Row("FOMC-2026-07-29", -0.02, 2)]
                : [];
            return Task.FromResult(new CatalystOutcomeReport(query.Family, ["VIX9D", "VIX", "SPX"], rows, 0, 0, 0, none, none, Now));
        }

        private static CatalystOutcome Row(string id, double move, double change) =>
            new(id, Now, true, AsOf, AsOf, AsOf, false, 0.1, 0.04, 0.01, true, true, move, change, 0, true, true);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
