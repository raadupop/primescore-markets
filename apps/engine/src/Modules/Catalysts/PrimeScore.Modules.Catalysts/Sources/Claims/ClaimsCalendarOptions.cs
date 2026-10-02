namespace PrimeScore.Modules.Catalysts.Sources.Claims;

/// <summary>
/// Bound from <c>Sources:ClaimsCalendar</c>; disabled unless <c>Enabled</c> is true. The rule
/// fetches nothing, so the page settings (BaseUrl, UserAgent, timeouts, retries) are not used.
/// </summary>
internal sealed class ClaimsCalendarOptions() : CalendarOptions(Section, "https://oui.doleta.gov/")
{
    public const string Section = "Sources:ClaimsCalendar";

    public const string SourceName = "ClaimsCalendar";

    /// <summary>Releases are derived through this many days after today (New York).</summary>
    public int HorizonDays { get; set; } = 63;

    /// <summary>And from this many weeks before today, so a first run covers the recent past.</summary>
    public int LookbackWeeks { get; set; } = 60;

    public override string? DisabledReason() =>
        base.DisabledReason()
        ?? (HorizonDays is < 0 or > 366 ? $"{Section}:HorizonDays must be from 0 to 366"
            : LookbackWeeks is < 0 or > 520 ? $"{Section}:LookbackWeeks must be from 0 to 520"
            : null);
}
