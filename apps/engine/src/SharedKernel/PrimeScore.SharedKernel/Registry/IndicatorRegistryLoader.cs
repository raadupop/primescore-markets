using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PrimeScore.SharedKernel.Registry;

/// <summary>
/// Reads <c>infra/registry.yaml</c> with the classifier's validation rules: unknown keys are
/// errors, <c>N_L ≥ 278</c> when set, and the fallback family matches the presence of <c>N_L</c>.
/// </summary>
public static class IndicatorRegistryLoader
{
    public const int MinimumLongHorizon = 278;

    private static readonly string[] DeviationKinds = ["pct_change", "surprise_yoy", "corr_delta"];
    private static readonly string[] FallbackFamilies = ["gaussian", "log_gaussian", "none"];
    private static readonly string[] Cadences = ["business_day", "calendar_day"];
    private static readonly string[] Providers = ["fred", "finnhub", "twelve_data"];
    private static readonly string[] Derivations = ["pct_change_yoy", "none"];

    public static IndicatorRegistry Load(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Indicator registry not found at '{fullPath}'.", fullPath);
        }

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        RegistryDocument document;
        try
        {
            document = deserializer.Deserialize<RegistryDocument>(File.ReadAllText(fullPath))
                ?? throw new InvalidDataException("Registry root must be a mapping.");
        }
        catch (YamlException exception)
        {
            throw new InvalidDataException($"Registry '{fullPath}' is invalid: {exception.Message}", exception);
        }

        var classes = new Dictionary<string, IndicatorClass>(StringComparer.Ordinal);
        foreach (var (name, body) in document.Classes)
        {
            classes[name] = ToIndicatorClass(name, body);
        }

        var symbols = new Dictionary<string, RegisteredSymbol>(StringComparer.Ordinal);
        foreach (var (symbol, body) in document.Symbols)
        {
            if (body.Class is null || !classes.TryGetValue(body.Class, out var indicatorClass))
            {
                throw new InvalidDataException($"Symbol '{symbol}' references unknown class '{body.Class}'.");
            }

            symbols[symbol] = new RegisteredSymbol(symbol, indicatorClass, ToProviderMapping(symbol, body.Bootstrap));
        }

        return new IndicatorRegistry(fullPath, classes, symbols);
    }

    /// <summary>
    /// Resolves the registry file: an explicit path (relative to <paramref name="baseDirectory"/>),
    /// otherwise the nearest <c>infra/registry.yaml</c> above <paramref name="baseDirectory"/>.
    /// </summary>
    public static string ResolvePath(string? configuredPath, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.GetFullPath(configuredPath, baseDirectory);
        }

        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "infra", "registry.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"No infra/registry.yaml found above '{baseDirectory}'. Set Registry:Path.");
    }

    private static IndicatorClass ToIndicatorClass(string name, ClassDocument body)
    {
        if (!SourceCategoryNames.TryParseWireName(body.SourceCategory, out var category))
        {
            throw Invalid(name, $"source_category '{body.SourceCategory}' is not a source category");
        }

        if (body.N is not > 0)
        {
            throw Invalid(name, "N must be a positive integer");
        }

        var family = body.SeverityFallbackFamily ?? "none";
        Require(name, "deviation_kind", body.DeviationKind, DeviationKinds);
        Require(name, "severity_fallback_family", family, FallbackFamilies);
        var cadence = body.Cadence ?? "calendar_day";
        Require(name, "cadence", cadence, Cadences);
        if (body.ExpectedFrequencySeconds is not > 0)
        {
            throw Invalid(name, "expected_frequency_seconds must be positive");
        }

        if (body.LongHorizonN is { } longHorizon)
        {
            if (longHorizon < MinimumLongHorizon)
            {
                throw Invalid(name, $"N_L={longHorizon} is below the SRS floor of {MinimumLongHorizon}");
            }

            if (family != "none")
            {
                throw Invalid(name, "severity_fallback_family must be 'none' when N_L is set");
            }
        }
        else if (family == "none")
        {
            throw Invalid(name, "severity_fallback_family must be 'gaussian' or 'log_gaussian' when N_L is null");
        }

        return new IndicatorClass(
            name, category, body.N.Value, body.LongHorizonN, body.DeviationKind!, family,
            body.ExpectedFrequencySeconds.Value, cadence);
    }

    private static ProviderMapping? ToProviderMapping(string symbol, BootstrapDocument? body)
    {
        if (body is null)
        {
            return null;
        }

        Require(symbol, "bootstrap.provider", body.Provider, Providers);
        var derive = body.Derive ?? "none";
        Require(symbol, "bootstrap.derive", derive, Derivations);
        if (string.IsNullOrWhiteSpace(body.SeriesId))
        {
            throw Invalid(symbol, "bootstrap.series_id is required");
        }

        return new ProviderMapping(body.Provider!, body.SeriesId, derive, body.Verified);
    }

    private static void Require(string owner, string field, string? value, string[] allowed)
    {
        if (value is null || !allowed.Contains(value, StringComparer.Ordinal))
        {
            throw Invalid(owner, $"{field} '{value}' is not one of {string.Join(", ", allowed)}");
        }
    }

    private static InvalidDataException Invalid(string owner, string reason) =>
        new($"Registry entry '{owner}': {reason}.");

    // YamlDotNet needs mutable, public-settable types; they stay private to this loader.
    private sealed class RegistryDocument
    {
        public Dictionary<string, ClassDocument> Classes { get; set; } = [];

        public Dictionary<string, SymbolDocument> Symbols { get; set; } = [];
    }

    private sealed class ClassDocument
    {
        public string? SourceCategory { get; set; }

        [YamlMember(Alias = "N", ApplyNamingConventions = false)]
        public int? N { get; set; }

        [YamlMember(Alias = "N_L", ApplyNamingConventions = false)]
        public int? LongHorizonN { get; set; }

        public string? DeviationKind { get; set; }

        public string? SeverityFallbackFamily { get; set; }

        public long? ExpectedFrequencySeconds { get; set; }

        public string? Cadence { get; set; }
    }

    private sealed class SymbolDocument
    {
        public string? Class { get; set; }

        public BootstrapDocument? Bootstrap { get; set; }
    }

    private sealed class BootstrapDocument
    {
        public string? Provider { get; set; }

        public string? SeriesId { get; set; }

        public string? Derive { get; set; }

        public bool Verified { get; set; }
    }
}
