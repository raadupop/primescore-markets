namespace PrimeScore.Modules.Catalysts.Sources.Eia;

/// <summary>Bound from <c>Sources:EiaCalendar</c>; disabled unless <c>Enabled</c> is true.</summary>
internal sealed class EiaCalendarOptions() : CalendarOptions(Section, "https://www.eia.gov/")
{
    public const string Section = "Sources:EiaCalendar";

    public const string SourceName = "EiaCalendar";
}
