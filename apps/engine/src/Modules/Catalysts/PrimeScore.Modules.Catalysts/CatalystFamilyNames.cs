using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Catalysts;

/// <summary>
/// Wire names of <see cref="CatalystFamily"/> (<c>FOMC</c>, <c>CPI</c>, ...): the prefix of every
/// canonical id and the value stored in <c>cat_events.family</c>. Kept here rather than in the
/// contracts because a string switch there would emit compiler helper types (brief §5 rule 2).
/// </summary>
internal static class CatalystFamilyNames
{
    public static string ToWireName(this CatalystFamily family) => family switch
    {
        CatalystFamily.Fomc => "FOMC",
        CatalystFamily.Cpi => "CPI",
        CatalystFamily.Nfp => "NFP",
        CatalystFamily.Claims => "CLAIMS",
        CatalystFamily.Gdp => "GDP",
        CatalystFamily.Pce => "PCE",
        CatalystFamily.Wpsr => "WPSR",
        CatalystFamily.Opec => "OPEC",
        _ => throw new ArgumentOutOfRangeException(nameof(family), family, "Unknown catalyst family."),
    };

    /// <summary>Exact, upper-case wire names only; anything else is not a family.</summary>
    public static bool TryParse(string? text, out CatalystFamily family)
    {
        foreach (var candidate in Enum.GetValues<CatalystFamily>())
        {
            if (string.Equals(text, candidate.ToWireName(), StringComparison.Ordinal))
            {
                family = candidate;
                return true;
            }
        }

        family = default;
        return false;
    }

    public static CatalystFamily Parse(string text) =>
        TryParse(text, out var family) ? family : throw new FormatException($"'{text}' is not a catalyst family.");
}
