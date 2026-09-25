namespace PrimeScore.Engine.Host.Components.Shared;

/// <summary>One stacked panel of a <see cref="SeriesChart"/>: one series on its own y-scale (never a second axis).</summary>
/// <param name="Values">One value per chart date; null where there is none (the line breaks).</param>
/// <param name="Color">Series colour (validated pair: composite <c>#5b8ef0</c>, dislocation <c>#d8692f</c>).</param>
/// <param name="Minimum">Lower bound of the y-domain; widened to include every value and reference line.</param>
public sealed record SeriesPanel(
    string Name,
    string Color,
    IReadOnlyList<double?> Values,
    Func<double, string> Format,
    double Minimum,
    double Maximum,
    IReadOnlyList<ReferenceLine>? References = null);

/// <summary>A recessive dashed line, such as a threshold; a non-empty label names it in the legend.</summary>
public sealed record ReferenceLine(double Value, string Label);

/// <summary>A shaded run of dates across every panel, such as a volatility regime.</summary>
/// <param name="Kind">CSS modifier: <c>high</c> (solid wash) or <c>low</c> (hatched).</param>
public sealed record ShadedRun(int First, int Last, string Kind);

/// <summary>An extra tooltip and table column, such as the observed IV or the regime.</summary>
public sealed record ExtraColumn(string Name, Func<int, string> Value);
