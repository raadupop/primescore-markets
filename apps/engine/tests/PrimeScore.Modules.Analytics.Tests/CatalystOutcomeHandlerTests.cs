using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Analytics.Features;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>
/// <see cref="GetCatalystOutcomesHandler"/> (ADR-0013) against strict stubs. Closes on every NYSE trading
/// day 2026-01-02 … 2026-09-24 (invented): VIX 20 on even days of the month and 25 on odd days, VIX9D =
/// VIX × 1.2, SPX 5000 except on the dates of <see cref="Spx"/>, OVX 40 except 2026-09-16 (37).
/// √(9/365) = 0.157026; a priced move is 0.30 × 0.157026 = 4.7108% (VIX9D 30) or 0.24 × 0.157026 = 3.7686%
/// (VIX9D 24); the straddle estimate √(2/π) × priced is 3.7587% or 3.0069%.
/// </summary>
public sealed class CatalystOutcomeHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
    private static readonly double Scale = Math.Sqrt(9 / 365.0);

    /// <summary>Event closes (D) and window ends (read date + 9 calendar days) of the six past FOMC meetings.</summary>
    private static readonly Dictionary<DateOnly, double> Spx = new()
    {
        [new(2026, 1, 28)] = 5050, [new(2026, 2, 5)] = 5100,   // M +1.0%, A +2.0%
        [new(2026, 3, 18)] = 4900, [new(2026, 3, 26)] = 4750,  // M −2.0%, A −5.0%
        [new(2026, 4, 29)] = 5025, [new(2026, 5, 7)] = 5175,   // M +0.5%, A +3.5%
        [new(2026, 6, 17)] = 4975, [new(2026, 6, 25)] = 4950,  // M −0.5%, A −1.0%
        [new(2026, 7, 29)] = 5000, [new(2026, 8, 6)] = 5200,   // M  0.0%, A +4.0%
        [new(2026, 9, 16)] = 5010, [new(2026, 9, 24)] = 4900,  // M +0.2%, A −2.0%
    };

    private static readonly DateOnly[] Fomc2026 =
        [new(2026, 1, 28), new(2026, 3, 18), new(2026, 4, 29), new(2026, 6, 17), new(2026, 7, 29), new(2026, 9, 16), new(2026, 10, 28), new(2026, 12, 9)];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_six_past_FOMC_meetings_match_the_hand_calculation()
    {
        var calendar = Fomc2026.Select(date => Catalyst(CatalystFamily.Fomc, date, new TimeOnly(14, 0))).ToArray();
        var report = await Handler(calendar).HandleAsync(new GetCatalystOutcomes("FOMC"), Token);

        // 10-28 and 12-09 have no event close yet: not rows. Newest first.
        Assert.Equal(["FOMC-2026-09-16", "FOMC-2026-07-29", "FOMC-2026-06-17", "FOMC-2026-04-29", "FOMC-2026-03-18", "FOMC-2026-01-28"],
            report.Events.Select(row => row.CatalystId));
        Assert.Equal(["VIX9D", "VIX", "SPX"], report.ReferenceInstruments);
        Assert.Equal((0, 0, 0), (report.WindowOpen, report.BeforeLiveStart, report.MissingCloses));

        // 09-16: read Tue 09-15 (odd, VIX9D 30), event close 09-16 (14:00 is before the 16:00 close; even, VIX9D 24),
        // window end Thu 09-24. A = 4900 / 5000 − 1 = −2.0%: inside 4.71%, below the 3.76% straddle estimate.
        var september = report.Events[0];
        Assert.Equal((new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 24), false),
            (september.ReadDate, september.EventCloseDate, september.WindowEndDate, september.WindowOpen));
        Assert.Equal(0.30 * Scale, september.PricedMove!.Value, 12);
        Assert.Equal(-0.02, september.ActualMove!.Value, 12);
        Assert.Equal(0.002, september.EventDayMove!.Value, 12);
        Assert.Equal(0.2, september.RatioBefore!.Value, 12);
        Assert.Equal(-6, september.VolatilityChange, 12);
        Assert.Equal((true, true, 0, true), (september.InsidePricedRange, september.BelowStraddleEstimate, september.OtherEventsInWindow, september.PreviouslyExamined));

        // 04-29: priced 3.77% (VIX9D 24), A +3.5%: inside, but above the 3.01% straddle estimate.
        var april = report.Events[3];
        Assert.Equal((true, false), (april.InsidePricedRange, april.BelowStraddleEstimate));
        // 07-29: A +4.0% exceeds 3.77%. 03-18: A −5.0% exceeds 4.71%.
        Assert.False(report.Events[1].InsidePricedRange);
        Assert.False(report.Events[4].InsidePricedRange);

        // Inside: 09-16, 06-17, 04-29, 01-28 = 4. Below the estimate: 09-16, 06-17, 01-28 = 3. VIX9D fell on 09-16, 03-18, 01-28.
        // Medians: priced (3.7686% + 4.7108%) / 2; |A| 1, 2, 2, 3.5, 4, 5 → 2.75%; |M| 0, 0.2, 0.5, 0.5, 1, 2 → 0.5%; ΔV −6 ×3, +6 ×3 → 0.
        var all = report.All;
        Assert.Equal((6, 4, 3, 3), (all.Count, all.InsidePricedRange, all.BelowStraddleEstimate, all.VolatilityFell));
        Assert.Equal(0.27 * Scale, all.MedianPricedMove!.Value, 12);
        Assert.Equal(0.0275, all.MedianAbsActualMove!.Value, 12);
        Assert.Equal(0.005, all.MedianAbsEventDayMove!.Value, 12);
        Assert.Equal(0, all.MedianVolatilityChange!.Value, 12);
        Assert.Equal(all, report.Latest12);
        Assert.Equal(Now, report.ComputedAt);
    }

    [Fact]
    public async Task Open_windows_missing_closes_early_events_and_overlapping_events_are_reported_not_filled()
    {
        var calendar = new[]
        {
            // CPI on Tue 09-22 08:30: read 09-21, window end Wed 09-30, after the last close (09-24): open.
            Catalyst(CatalystFamily.Cpi, new DateOnly(2026, 9, 22), new TimeOnly(8, 30)),
            // CPI on Fri 07-10: read Thu 07-09, whose SPX close is removed below: missing.
            Catalyst(CatalystFamily.Cpi, new DateOnly(2026, 7, 10), new TimeOnly(8, 30)),
            // CPI on Thu 09-10 08:30: complete; the FOMC of 09-16 lies in (09-09, 09-18].
            Catalyst(CatalystFamily.Cpi, new DateOnly(2026, 9, 10), new TimeOnly(8, 30)),
            // Before VIX9D's live start.
            Catalyst(CatalystFamily.Cpi, new DateOnly(2013, 9, 17), new TimeOnly(8, 30)),
            Catalyst(CatalystFamily.Fomc, new DateOnly(2026, 9, 16), new TimeOnly(14, 0)),
        };

        var report = await Handler(calendar, withoutSpx: new DateOnly(2026, 7, 9)).HandleAsync(new GetCatalystOutcomes("CPI"), Token);

        Assert.Equal(["CPI-2026-09-22", "CPI-2026-09-10"], report.Events.Select(row => row.CatalystId));
        Assert.Equal((1, 1, 1), (report.WindowOpen, report.BeforeLiveStart, report.MissingCloses));

        var open = report.Events[0];
        Assert.True(open.WindowOpen);
        Assert.Equal(new DateOnly(2026, 9, 30), open.WindowEndDate);
        Assert.Null(open.ActualMove);
        Assert.Null(open.InsidePricedRange);
        Assert.NotNull(open.EventDayMove);

        var september = report.Events[1];
        Assert.Equal((new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 18)), (september.ReadDate, september.EventCloseDate, september.WindowEndDate));
        Assert.Equal(1, september.OtherEventsInWindow);
        Assert.True(september.PreviouslyExamined);
        Assert.Equal(1, report.All.Count);
    }

    [Fact]
    public async Task Oil_families_report_only_the_OVX_change()
    {
        var calendar = new[] { Catalyst(CatalystFamily.Wpsr, new DateOnly(2026, 9, 16), new TimeOnly(10, 30)) };

        var report = await Handler(calendar).HandleAsync(new GetCatalystOutcomes("WPSR"), Token);

        var row = Assert.Single(report.Events);
        Assert.Equal(["OVX"], report.ReferenceInstruments);
        Assert.Equal(-3, row.VolatilityChange, 12);
        Assert.Null(row.PricedMove);
        Assert.Null(row.EventDayMove);
        Assert.False(row.PreviouslyExamined);
        Assert.Equal((1, 1, (double?)-3), (report.All.Count, report.All.VolatilityFell, report.All.MedianVolatilityChange));
        Assert.Null(report.All.InsidePricedRange);
    }

    [Fact]
    public async Task An_unknown_family_is_an_empty_record()
    {
        var report = await Handler([]).HandleAsync(new GetCatalystOutcomes("fomc"), Token);

        Assert.Empty(report.Events);
        Assert.Equal(0, report.All.Count);
    }

    [Theory]
    [InlineData(2026, 9, 16, 14, 0, 2026, 9, 16)]   // FOMC 14:00: that day's close
    [InlineData(2026, 9, 10, 8, 30, 2026, 9, 10)]   // CPI 08:30: that day's close
    [InlineData(2026, 9, 16, 16, 30, 2026, 9, 17)]  // after the close: the next session
    [InlineData(2026, 9, 19, 9, 0, 2026, 9, 21)]    // a Saturday meeting: Monday's close
    [InlineData(2026, 9, 7, 10, 0, 2026, 9, 8)]     // Labor Day: the next session
    public void The_event_close_is_the_first_close_after_the_event(int y, int m, int d, int hour, int minute, int ey, int em, int ed)
    {
        var catalyst = Catalyst(CatalystFamily.Opec, new DateOnly(y, m, d), new TimeOnly(hour, minute));

        Assert.Equal(new DateOnly(ey, em, ed), GetCatalystOutcomesHandler.EventClose(catalyst));
    }

    private static GetCatalystOutcomesHandler Handler(IReadOnlyList<CatalystView> calendar, DateOnly? withoutSpx = null) =>
        new(new StrictCalendar(calendar), new StrictSeries(withoutSpx), new FixedClock(Now));

    private static CatalystView Catalyst(CatalystFamily family, DateOnly date, TimeOnly time)
    {
        var id = $"{family.ToString().ToUpperInvariant()}-{date:yyyy-MM-dd}";
        return new CatalystView(id, family, id, null, MarketTime.AtNewYork(date, time), date, true, CatalystStatus.Scheduled, null, false, 1, true, false, null,
            Now, null, new CatalystSourceView("test", CatalystSourceKind.Listing, "https://example.invalid/", Now, null), 1, "hash");
    }

    private sealed class StrictCalendar(IReadOnlyList<CatalystView> catalysts) : IQueryHandler<GetCatalysts, CatalystList>
    {
        public Task<CatalystList> HandleAsync(GetCatalysts query, CancellationToken cancellationToken) =>
            Task.FromResult(new CatalystList(
                query == new GetCatalysts(DateTimeOffset.MinValue, DateTimeOffset.MaxValue) ? catalysts : [], Now));
    }

    /// <summary>Answers only Cboe-sourced reads of the four series; anything else is empty.</summary>
    private sealed class StrictSeries(DateOnly? withoutSpx) : IQueryHandler<GetObservationSeries, ObservationSeries>
    {
        public Task<ObservationSeries> HandleAsync(GetObservationSeries query, CancellationToken cancellationToken)
        {
            Func<DateOnly, double?>? value = query switch
            {
                { Instrument: "VIX9D", Variant: "IMPLIED_VOLATILITY:9D", Category: SourceCategory.MarketData } => date => Vix(date) * 1.2,
                { Instrument: "VIX", Variant: "IMPLIED_VOLATILITY", Category: SourceCategory.MarketData } => date => Vix(date),
                { Instrument: "SPX", Variant: "basket_observation", Category: SourceCategory.CrossAssetFlow } =>
                    date => date == withoutSpx ? null : Spx.GetValueOrDefault(date, 5000),
                { Instrument: "OVX", Variant: "IMPLIED_VOLATILITY", Category: SourceCategory.MarketData } =>
                    date => date == new DateOnly(2026, 9, 16) ? 37 : 40,
                _ => null,
            };
            if (value is null || query.SourcePrefix != "cboe:" || query.Before != DateTimeOffset.MaxValue)
            {
                return Task.FromResult(new ObservationSeries([]));
            }

            var points = new List<ObservationPoint>();
            for (var date = new DateOnly(2026, 1, 2); date <= new DateOnly(2026, 9, 24); date = date.AddDays(1))
            {
                if (MarketTime.IsTradingDay(date) && value(date) is { } close)
                {
                    points.Add(new ObservationPoint(MarketTime.AtNewYork(date, new TimeOnly(16, 15)), close, Guid.NewGuid(), points.Count + 1));
                }
            }

            return Task.FromResult(new ObservationSeries(points));
        }

        private static double Vix(DateOnly date) => date.Day % 2 == 0 ? 20.0 : 25.0;
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
