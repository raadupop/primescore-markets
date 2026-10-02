namespace PrimeScore.Modules.Catalysts.Sources.Fed;

/// <summary>Bound from <c>Sources:FedCalendar</c>; disabled unless <c>Enabled</c> is true.</summary>
internal sealed class FedCalendarOptions() : CalendarOptions(Section, "https://www.federalreserve.gov/")
{
    public const string Section = "Sources:FedCalendar";

    public const string SourceName = "FedCalendar";

    /// <summary>First year read from the historical pages (the years before the current page's first panel); 0 reads none.</summary>
    public int HistoryFromYear { get; set; } = 2013;

    public override string? DisabledReason() =>
        base.DisabledReason()
        ?? (HistoryFromYear is 0 or (>= 2000 and <= 2100) ? null : $"{Section}:HistoryFromYear must be 0 (no history) or a year from 2000");
}
