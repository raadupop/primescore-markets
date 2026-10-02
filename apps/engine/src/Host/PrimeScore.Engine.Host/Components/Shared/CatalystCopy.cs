using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Engine.Host.Components.Shared;

/// <summary>Catalyst family names for the Catalyst calendar and the Event record.</summary>
internal static class CatalystCopy
{
    /// <summary>The wire name, the prefix of every canonical id (the contract's rule: upper-case member name).</summary>
    public static string Wire(CatalystFamily family) => family.ToString().ToUpperInvariant();

    public static string FamilyName(CatalystFamily family) => family switch
    {
        CatalystFamily.Fomc => "FOMC decision",
        CatalystFamily.Cpi => "Consumer prices",
        CatalystFamily.Nfp => "Employment Situation",
        CatalystFamily.Claims => "Initial claims",
        CatalystFamily.Gdp => "GDP estimate",
        CatalystFamily.Pce => "Personal income and outlays",
        CatalystFamily.Wpsr => "Weekly petroleum status",
        CatalystFamily.Opec => "OPEC meeting",
        _ => Fmt.Humanize(family.ToString()),
    };

    /// <summary>WPSR and OPEC are measured on OVX only (ADR-0013).</summary>
    public static bool IsOil(CatalystFamily family) => family is CatalystFamily.Wpsr or CatalystFamily.Opec;
}
