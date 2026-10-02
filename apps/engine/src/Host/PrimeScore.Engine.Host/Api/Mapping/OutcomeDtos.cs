using PrimeScore.Api.Contracts;
using Module = PrimeScore.Modules.Analytics.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>The outcomes record between the Analytics module view and the contract (brief §5 rule 4).</summary>
internal static class OutcomeDtos
{
    public static Module.PositionInput ToModule(PositionScenarioRequest request) => new(
        request.Underlying.ToString(),
        request.Expiry,
        request.Legs.Select(leg => new Module.PositionLeg(leg.Right.ToString(), leg.Strike, leg.Quantity)).ToArray(),
        request.Entry_cost,
        request.Rate ?? 0.04,
        request.Dividend_yield ?? 0.013);

    public static PositionScenarioReport From(Module.PositionScenarioReport report) => new()
    {
        Error = report.Error,
        As_of = report.AsOf,
        Underlying_level = report.UnderlyingLevel,
        Implied_volatility = report.ImpliedVolatility,
        Days_to_expiry = report.DaysToExpiry,
        Value = report.Value,
        Delta = report.Delta,
        Gamma = report.Gamma,
        Vega = report.Vega,
        Theta = report.Theta,
        Entry_cost = report.EntryCost,
        Max_loss_at_expiry = report.MaxLossAtExpiry,
        Unlimited_loss = report.UnlimitedLoss,
        Events = report.Events.Select(item => new PositionEventScenarios
        {
            Catalyst_id = item.CatalystId,
            // The family's wire name is the contract's member name (FOMC, CPI, ...).
            Family = Enum.Parse<CatalystFamily>(item.Family),
            Scheduled_at = item.ScheduledAt,
            Event_close_date = item.EventCloseDate,
            Days_from_as_of = item.DaysFromAsOf,
            Decay_to_event = item.DecayToEvent,
            Scenarios = item.Scenarios,
            Mean_move_part = item.MeanMovePart,
            Mean_volatility_part = item.MeanVolatilityPart,
            Mean_total = item.MeanTotal,
            Median_total = item.MedianTotal,
            Loss_share = item.LossShare,
            Worst = item.Worst,
            Best = item.Best,
        }).ToList(),
        Computed_at = report.ComputedAt,
    };

    public static CatalystOutcomeReport From(Module.CatalystOutcomeReport report, CatalystFamily family) => new()
    {
        Family = family,
        Reference_instruments = report.ReferenceInstruments.ToList(),
        Events = report.Events.Select(row => new CatalystOutcome
        {
            Catalyst_id = row.CatalystId,
            Scheduled_at = row.ScheduledAt,
            Time_announced = row.TimeAnnounced,
            Read_date = row.ReadDate,
            Event_close_date = row.EventCloseDate,
            Window_end_date = row.WindowEndDate,
            Window_open = row.WindowOpen,
            Ratio_before = row.RatioBefore,
            Priced_move = row.PricedMove,
            Actual_move = row.ActualMove,
            Inside_priced_range = row.InsidePricedRange,
            Below_straddle_estimate = row.BelowStraddleEstimate,
            Event_day_move = row.EventDayMove,
            Volatility_change = row.VolatilityChange,
            Other_events_in_window = row.OtherEventsInWindow,
            Previously_examined = row.PreviouslyExamined,
            Never_rescheduled = row.NeverRescheduled,
        }).ToList(),
        Window_open = report.WindowOpen,
        Before_live_start = report.BeforeLiveStart,
        Missing_closes = report.MissingCloses,
        All = From(report.All),
        Latest_12 = From(report.Latest12),
        Computed_at = report.ComputedAt,
    };

    private static CatalystOutcomeSummary From(Module.CatalystOutcomeSummary summary) => new()
    {
        Count = summary.Count,
        Median_priced_move = summary.MedianPricedMove,
        Median_abs_actual_move = summary.MedianAbsActualMove,
        Inside_priced_range = summary.InsidePricedRange,
        Below_straddle_estimate = summary.BelowStraddleEstimate,
        Median_abs_event_day_move = summary.MedianAbsEventDayMove,
        Median_volatility_change = summary.MedianVolatilityChange,
        Volatility_fell = summary.VolatilityFell,
    };

    public static ForwardOutcomesReport From(Module.ForwardOutcomesReport report) => new()
    {
        Context = report.Context,
        Reference_instrument = report.ReferenceInstrument,
        First_date = report.FirstDate,
        Last_date = report.LastDate,
        Days = report.Days,
        Deploy_days = report.DeployDays,
        Legacy_days = report.LegacyDays,
        Excluded_days = report.ExcludedDays,
        Other_instrument_days = report.OtherInstrumentDays,
        Horizons = report.Horizons.Select(row => new HorizonOutcome
        {
            Trading_days = row.TradingDays,
            Deploy_count = row.DeployCount,
            All_count = row.AllCount,
            Deploy_median_abs_change = row.DeployMedianAbsChange,
            All_median_abs_change = row.AllMedianAbsChange,
            Deploy_revert_share = row.DeployRevertShare,
            Deploy_revert_share_low = row.DeployRevertShareLow,
            Deploy_revert_share_high = row.DeployRevertShareHigh,
            All_up_share = row.AllUpShare,
            Deploy_mean_change_toward_median = row.DeployMeanChangeTowardMedian,
        }).ToList(),
        States = report.States.Select(row => new StateOutcome
        {
            State = row.State,
            Count = row.Count,
            Median_change_5 = row.MedianChange5,
            Median_abs_change_5 = row.MedianAbsChange5,
            Up_share_5 = row.UpShare5,
            Median_change_21 = row.MedianChange21,
            Median_abs_change_21 = row.MedianAbsChange21,
            Up_share_21 = row.UpShare21,
        }).ToList(),
        Computed_at = report.ComputedAt,
    };
}
