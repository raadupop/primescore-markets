namespace PrimeScore.SharedKernel;

/// <summary>The four independent signal source categories (SRS SIG-001).</summary>
public enum SourceCategory
{
    MarketData,
    Macroeconomic,
    Geopolitical,
    CrossAssetFlow,
}

public static class SourceCategoryNames
{
    /// <summary>The contract and classifier spelling, e.g. <c>MARKET_DATA</c>.</summary>
    public static string ToWireName(this SourceCategory category) => category switch
    {
        SourceCategory.MarketData => "MARKET_DATA",
        SourceCategory.Macroeconomic => "MACROECONOMIC",
        SourceCategory.Geopolitical => "GEOPOLITICAL",
        SourceCategory.CrossAssetFlow => "CROSS_ASSET_FLOW",
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null),
    };

    public static bool TryParseWireName(string? text, out SourceCategory category)
    {
        foreach (var candidate in Enum.GetValues<SourceCategory>())
        {
            if (string.Equals(candidate.ToWireName(), text, StringComparison.Ordinal))
            {
                category = candidate;
                return true;
            }
        }

        category = default;
        return false;
    }
}
