namespace PrimeScore.Modules.Catalysts.Sources.Bls;

/// <summary>
/// Bound from <c>Sources:BlsCalendar</c>; disabled unless <c>Enabled</c> is true and
/// <see cref="CalendarOptions.UserAgent"/> names a contact (an e-mail address or a URL): BLS
/// terms reserve the right to block robots without contact information (ADR-0011).
/// </summary>
internal sealed class BlsCalendarOptions() : CalendarOptions(Section, "https://www.bls.gov/")
{
    public const string Section = "Sources:BlsCalendar";

    public const string SourceName = "BlsCalendar";

    /// <summary>First archived year read (through the year of the earliest per-release row); 0 reads none.</summary>
    public int HistoryFromYear { get; set; } = 2013;

    public override string? DisabledReason()
    {
        if (base.DisabledReason() is { } reason)
        {
            return reason;
        }

        if (!UserAgent.Contains('@', StringComparison.Ordinal) && !UserAgent.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return $"{Section}:UserAgent must name a contact (an e-mail address or a URL); BLS may block robots without one";
        }

        return HistoryFromYear is 0 or (>= 2000 and <= 2100) ? null : $"{Section}:HistoryFromYear must be 0 (no history) or a year from 2000";
    }
}
