namespace PrimeScore.Modules.Catalysts.Sources.Bea;

/// <summary>Bound from <c>Sources:BeaCalendar</c>; disabled unless <c>Enabled</c> is true.</summary>
internal sealed class BeaCalendarOptions() : CalendarOptions(Section, "https://www.bea.gov/")
{
    public const string Section = "Sources:BeaCalendar";

    public const string SourceName = "BeaCalendar";
}
