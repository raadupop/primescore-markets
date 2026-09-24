using System.Collections.Frozen;

namespace PrimeScore.SharedKernel.Registry;

/// <summary>
/// Calibration parameters shared by every symbol of an indicator class (SRS §3). The engine
/// reads the same <c>infra/registry.yaml</c> as the classifier; neither side owns a copy.
/// </summary>
/// <param name="N">Short rolling window for the degeneracy check (CLS-009).</param>
/// <param name="LongHorizonN">Long-horizon ECDF window <c>N_L</c>; null selects the parametric fallback.</param>
public sealed record IndicatorClass(
    string Name,
    SourceCategory SourceCategory,
    int N,
    int? LongHorizonN,
    string DeviationKind,
    string SeverityFallbackFamily,
    long ExpectedFrequencySeconds,
    string Cadence)
{
    /// <summary>Length of the reference window the engine supplies: <c>N_L</c>, or <c>N</c> when <c>N_L</c> is null.</summary>
    public int ReferenceWindowLength => LongHorizonN ?? N;

    public bool IsBusinessDayCadence => string.Equals(Cadence, "business_day", StringComparison.Ordinal);
}

/// <summary>Where a symbol's history comes from. <c>Verified = false</c> means the series id is unconfirmed.</summary>
public sealed record ProviderMapping(string Provider, string SeriesId, string Derive, bool Verified);

public sealed record RegisteredSymbol(string Symbol, IndicatorClass IndicatorClass, ProviderMapping? Bootstrap);

public sealed class IndicatorRegistry
{
    internal IndicatorRegistry(
        string sourcePath,
        IReadOnlyDictionary<string, IndicatorClass> classes,
        IReadOnlyDictionary<string, RegisteredSymbol> symbols)
    {
        SourcePath = sourcePath;
        Classes = classes.ToFrozenDictionary(StringComparer.Ordinal);
        Symbols = symbols.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public string SourcePath { get; }

    public IReadOnlyDictionary<string, IndicatorClass> Classes { get; }

    public IReadOnlyDictionary<string, RegisteredSymbol> Symbols { get; }

    public bool TryGetSymbol(string symbol, out RegisteredSymbol registered) =>
        Symbols.TryGetValue(symbol, out registered!);

    public IEnumerable<RegisteredSymbol> SymbolsWithProvider(string provider) =>
        Symbols.Values
            .Where(symbol => string.Equals(symbol.Bootstrap?.Provider, provider, StringComparison.Ordinal))
            .OrderBy(symbol => symbol.Symbol, StringComparer.Ordinal);
}
