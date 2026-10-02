using System.Globalization;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Claims;

/// <summary>
/// The weekly claims release derived by rule (ADR-0011), since DOL's schedule page refuses
/// automated clients: Thursday 08:30 New York, or the Wednesday before when a federal holiday is
/// observed on that Thursday. Each release is keyed by the Saturday that ends its claims week, five
/// days before the standard Thursday (<c>CLAIMS:2026-09-26</c>), so a date DOL publishes later can
/// correct the rule's row as the same catalyst. The source URL is a rule identifier, not a page.
/// </summary>
internal static class ClaimsRule
{
    public const string SourceUrl = "rule:claims-thursday-0830-federal-holiday-wednesday";

    public const string Derivation = "rule";

    public const string Title = "Unemployment Insurance Weekly Claims";

    public static readonly TimeOnly ReleaseTime = new(8, 30);

    /// <summary>The release for every standard Thursday from <paramref name="from"/> through <paramref name="to"/> (New York dates).</summary>
    public static IReadOnlyList<ObservedCatalyst> Releases(DateOnly from, DateOnly to)
    {
        var rows = new List<ObservedCatalyst>();
        for (var thursday = from.AddDays(((int)DayOfWeek.Thursday - (int)from.DayOfWeek + 7) % 7); thursday <= to; thursday = thursday.AddDays(7))
        {
            var weekEnding = thursday.AddDays(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var release = FederalHolidays.IsObserved(thursday) ? thursday.AddDays(-1) : thursday;
            rows.Add(new ObservedCatalyst(
                CatalystFamily.Claims, MarketTime.AtNewYork(release, ReleaseTime), true, Title, "week ending " + weekEnding, "CLAIMS:" + weekEnding,
                null, false, CatalystStatus.Scheduled, Derivation));
        }

        return rows;
    }
}
