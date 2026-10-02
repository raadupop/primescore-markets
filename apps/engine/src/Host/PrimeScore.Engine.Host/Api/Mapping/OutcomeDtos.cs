using PrimeScore.Api.Contracts;
using Module = PrimeScore.Modules.Analytics.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>The outcomes record between the Analytics module view and the contract (brief §5 rule 4).</summary>
internal static class OutcomeDtos
{
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
