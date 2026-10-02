using PrimeScore.Api.Contracts;
using Module = PrimeScore.Modules.Analytics.Contracts;

namespace PrimeScore.Engine.Host.Api;

/// <summary>The outcomes record between the Analytics module view and the contract (brief §5 rule 4).</summary>
internal static class OutcomeDtos
{
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
