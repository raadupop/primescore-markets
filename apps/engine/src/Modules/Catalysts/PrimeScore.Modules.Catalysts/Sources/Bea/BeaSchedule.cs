using System.Globalization;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Catalysts.Recording;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Catalysts.Sources.Bea;

/// <summary>
/// Reads BEA's release calendar feed (ADR-0011): every GDP estimate release (GDP) and every
/// Personal Income and Outlays release (PCE), at the UTC instant the feed states. GDP is keyed by
/// quarter and estimate (<c>GDP:2026Q3:advance</c>; the 2025 <c>initial</c> and <c>updated</c>
/// estimates keep their own words) and PCE by its reference period as written, lower case
/// (<c>PCE:october and november 2025</c>), so a moved release is still the same catalyst. GDP by
/// state, by county or for Puerto Rico and every other release are left out without being counted.
/// A GDP or PCE event whose summary or start does not read is rejected and quoted. Coverage runs
/// from the first to the last event date of the whole feed.
/// </summary>
internal static partial class BeaSchedule
{
    public const string GdpTitle = "Gross Domestic Product";

    public const string PceTitle = "Personal Income and Outlays";

    public static ParsedCalendar Parse(string ics)
    {
        var events = Ics.ReadEvents(ics);
        if (events.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no VEVENT");
        }

        var rows = new List<ObservedCatalyst>();
        var rejected = new List<string>();
        foreach (var calendarEvent in events)
        {
            var summary = CalendarText.Clean(calendarEvent.Text("SUMMARY") ?? "");
            var start = calendarEvent.StartUtc;
            ObservedCatalyst? row;
            if (summary.StartsWith("GDP (", StringComparison.Ordinal) || summary.StartsWith(GdpTitle + ",", StringComparison.Ordinal))
            {
                row = Gdp(summary, start);
            }
            else if (summary.StartsWith(PceTitle + ",", StringComparison.Ordinal))
            {
                row = Pce(summary, start);
            }
            else
            {
                continue;
            }

            if (row is null)
            {
                rejected.Add(CalendarText.Quote($"{summary} {calendarEvent.Properties.GetValueOrDefault("DTSTART")?.RawValue}"));
            }
            else
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            throw CalendarText.LayoutNotRecognised("no GDP or Personal Income and Outlays event reads");
        }

        var dates = events.Select(calendarEvent => calendarEvent.StartUtc).OfType<DateTimeOffset>().Select(MarketTime.NewYorkDate).ToArray();
        return new ParsedCalendar(rows, rejected, [], dates.Min(), dates.Max());
    }

    /// <summary><c>GDP (Advance Estimate), 3rd Quarter 2026</c> or <c>Gross Domestic Product, 4th Quarter and Year 2024 (Advance Estimate)</c>.</summary>
    private static ObservedCatalyst? Gdp(string summary, DateTimeOffset? start)
    {
        var estimate = Estimate().Match(summary);
        var quarter = Quarter().Match(summary);
        if (start is not { } at || !estimate.Success || !quarter.Success)
        {
            return null;
        }

        var word = estimate.Groups["estimate"].Value.ToLowerInvariant();
        var period = string.Create(CultureInfo.InvariantCulture, $"{quarter.Groups["year"].Value} Q{quarter.Groups["quarter"].Value}");
        return Release(CatalystFamily.Gdp, GdpTitle, $"{period} {word} estimate", $"GDP:{period.Replace(" ", "", StringComparison.Ordinal)}:{word}", at);
    }

    /// <summary><c>Personal Income and Outlays, August 2026</c>; the period is kept as written.</summary>
    private static ObservedCatalyst? Pce(string summary, DateTimeOffset? start)
    {
        var period = summary[(PceTitle.Length + 1)..].Trim();
        return start is { } at && period.Length > 0
            ? Release(CatalystFamily.Pce, PceTitle, period, "PCE:" + period.ToLowerInvariant(), at)
            : null;
    }

    private static ObservedCatalyst Release(CatalystFamily family, string title, string period, string key, DateTimeOffset at) =>
        new(family, at, true, title, period, key, null, false, CatalystStatus.Scheduled, null);

    [GeneratedRegex(@"\((?<estimate>[A-Za-z]+) Estimate\)", RegexOptions.CultureInvariant)]
    private static partial Regex Estimate();

    [GeneratedRegex(@"\b(?<quarter>[1-4])(st|nd|rd|th) [Qq]uarter( and [Yy]ear)? (?<year>\d{4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex Quarter();
}
