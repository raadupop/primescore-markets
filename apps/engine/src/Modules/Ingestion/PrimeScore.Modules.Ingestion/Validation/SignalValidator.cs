using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Validation;

/// <summary>
/// Structural validation of one submitted signal against the contract's <c>SignalInput</c> and
/// its category payload (SRS SIG-002). Every error is reported as <c>field: problem</c>; a
/// signal with any error is rejected whole and nothing about it reaches classification.
/// </summary>
internal sealed partial class SignalValidator(IndicatorRegistry registry, IClock clock)
{
    public const string CuratedPrefix = "curated:";

    public const string AdapterPrefix = "fred:";

    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset Earliest = new(1990, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] MetricTypes = ["IMPLIED_VOLATILITY", "REALISED_VOLATILITY", "SKEW", "TERM_STRUCTURE", "PRICE"];
    private static readonly string[] MacroIndicatorTypes = ["INTEREST_RATE", "YIELD_CURVE", "EMPLOYMENT", "INFLATION", "GDP", "CENTRAL_BANK_STATEMENT"];
    private static readonly string[] EscalationLevels = ["THREAT", "ACTION", "ESCALATION", "DE_ESCALATION"];
    private static readonly string[] FlowTypes = ["CORRELATION_BREAKDOWN", "FUND_FLOW", "LIQUIDITY_INDEX", "VOLATILITY_SPREAD"];

    public object Validate(JsonElement document, string submittedBy)
    {
        var errors = new List<string>();
        if (document.ValueKind != JsonValueKind.Object)
        {
            return new SignalRejection(null, ["signal: must be a JSON object"], document.GetRawText());
        }

        // Every value anywhere in the document must be storable, including fields the engine does
        // not read: the ledger stores the payload canonically and cannot hold 1e999 or a lone
        // UTF-16 surrogate. Such a signal is rejected on its own; the rest of its batch is not.
        RejectUnstorableValues(document, "", errors);
        if (errors.Count > 0)
        {
            return new SignalRejection(null, errors, document.GetRawText());
        }

        var category = RequiredEnum(document, "source_category", ["MARKET_DATA", "MACROECONOMIC", "GEOPOLITICAL", "CROSS_ASSET_FLOW"], errors);
        var source = RequiredText(document, "source_identifier", errors, maxLength: 200);
        if (source is not null && source.StartsWith(AdapterPrefix, StringComparison.OrdinalIgnoreCase))
        {
            // The adapter's rows are only the adapter's: it resumes from them and the Sources page counts them.
            errors.Add($"source_identifier: the '{AdapterPrefix}' prefix is reserved for the engine's FRED adapter; use another source name");
            source = null;
        }
        var timestamp = RequiredTimestamp(document, "timestamp", errors);
        var payloadType = RequiredEnum(document, "payload_type", ["STRUCTURED", "UNSTRUCTURED"], errors);
        if (timestamp is { } time && time > clock.UtcNow + FutureTolerance)
        {
            errors.Add("timestamp: is in the future; a signal can only be recorded once observed");
        }

        string? instrument = null;
        string? variant = null;
        double? value = null;
        JsonElement payload = default;
        if (payloadType == "STRUCTURED" && category is not null)
        {
            if (!document.TryGetProperty("structured_payload", out payload) || payload.ValueKind != JsonValueKind.Object)
            {
                errors.Add("structured_payload: required object when payload_type is STRUCTURED");
            }
            else
            {
                (instrument, variant, value) = category switch
                {
                    "MARKET_DATA" => MarketData(payload, timestamp, errors),
                    "MACROECONOMIC" => Macroeconomic(payload, timestamp, source, errors),
                    "GEOPOLITICAL" => Geopolitical(payload, errors),
                    _ => CrossAssetFlow(payload, errors),
                };
            }
        }
        else if (payloadType == "UNSTRUCTURED")
        {
            if (!document.TryGetProperty("unstructured_payload", out payload) || payload.ValueKind != JsonValueKind.Object)
            {
                errors.Add("unstructured_payload: required object when payload_type is UNSTRUCTURED");
            }
            else
            {
                variant = Unstructured(payload, errors);
                instrument = source;
            }
        }

        if (errors.Count > 0 || category is null || source is null || timestamp is null || payloadType is null || instrument is null || variant is null)
        {
            return new SignalRejection(source, errors, document.GetRawText());
        }

        SourceCategoryNames.TryParseWireName(category, out var parsedCategory);
        var provenance = source.StartsWith(CuratedPrefix, StringComparison.Ordinal)
            ? new SignalProvenance("HUMAN_CURATED", SubmittedBy: submittedBy, Note: "Curated event submitted through the ingestion API.")
            : new SignalProvenance("api", SubmittedBy: submittedBy);
        return new SignalCandidate(parsedCategory, source, instrument, variant, timestamp.Value, payloadType, value, payload.Clone(), provenance);
    }

    private (string?, string?, double?) MarketData(JsonElement payload, DateTimeOffset? timestamp, List<string> errors)
    {
        RequiredText(payload, "asset_class", errors, "structured_payload.", 64);
        var instrument = CanonicalSymbol(RequiredText(payload, "instrument", errors, "structured_payload.", 64));
        var metric = RequiredEnum(payload, "metric_type", MetricTypes, errors, "structured_payload.");
        var value = RequiredNumber(payload, "value", errors, "structured_payload.");
        RequiredText(payload, "unit", errors, "structured_payload.", 32);
        var tenor = OptionalText(payload, "tenor", errors, "structured_payload.");
        if (metric == "TERM_STRUCTURE" && tenor is null)
        {
            errors.Add("structured_payload.tenor: required when metric_type is TERM_STRUCTURE");
        }

        MatchesTimestamp(payload, "observed_at", timestamp, errors);
        return (instrument, tenor is null ? metric : $"{metric}:{tenor}", value);
    }

    private (string?, string?, double?) Macroeconomic(JsonElement payload, DateTimeOffset? timestamp, string? source, List<string> errors)
    {
        var indicator = RequiredEnum(payload, "indicator_type", MacroIndicatorTypes, errors, "structured_payload.");
        RequiredText(payload, "region", errors, "structured_payload.", 32);
        double? value = null;
        if (!payload.TryGetProperty("value", out var raw) || raw.ValueKind == JsonValueKind.Null)
        {
            if (indicator is not null and not "CENTRAL_BANK_STATEMENT")
            {
                errors.Add("structured_payload.value: required number unless indicator_type is CENTRAL_BANK_STATEMENT");
            }
        }
        else
        {
            value = RequiredNumber(payload, "value", errors, "structured_payload.");
        }

        if (payload.TryGetProperty("prior_value", out var prior) && prior.ValueKind is not JsonValueKind.Null)
        {
            RequiredNumber(payload, "prior_value", errors, "structured_payload.");
        }

        MatchesTimestamp(payload, "release_date", timestamp, errors);
        return (source is null ? null : MacroInstrument(source), indicator, value);
    }

    private static (string?, string?, double?) Geopolitical(JsonElement payload, List<string> errors)
    {
        var region = RequiredText(payload, "event_region", errors, "structured_payload.", 64);
        var eventType = RequiredText(payload, "event_type", errors, "structured_payload.", 64);
        if (RequiredNumber(payload, "severity_estimate", errors, "structured_payload.") is { } severity && severity is < 0 or > 1)
        {
            errors.Add("structured_payload.severity_estimate: must lie in [0, 1]");
        }

        if (!payload.TryGetProperty("actors", out var actors) || actors.ValueKind != JsonValueKind.Array)
        {
            errors.Add("structured_payload.actors: required array of strings");
        }
        else if (actors.EnumerateArray().Any(actor => actor.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(actor.GetString())))
        {
            errors.Add("structured_payload.actors: every actor must be a non-empty string");
        }

        OptionalEnum(payload, "escalation_level", EscalationLevels, errors, "structured_payload.");

        // Geopolitical severity is source-provided; the engine does not treat it as a measured value.
        return (region is null ? null : $"GEO:{region}", eventType, null);
    }

    private static (string?, string?, double?) CrossAssetFlow(JsonElement payload, List<string> errors)
    {
        var flow = RequiredEnum(payload, "flow_type", FlowTypes, errors, "structured_payload.");
        var pair = OptionalText(payload, "asset_pair", errors, "structured_payload.");
        var value = RequiredNumber(payload, "value", errors, "structured_payload.");
        RequiredNumber(payload, "baseline_value", errors, "structured_payload.");
        RequiredText(payload, "lookback_period", errors, "structured_payload.", 32);
        return (pair ?? flow, flow, value);
    }

    /// <summary>Validates free text; its variant is a content hash, so distinct texts at one instant do not collide.</summary>
    private static string? Unstructured(JsonElement payload, List<string> errors)
    {
        var text = RequiredText(payload, "text", errors, "unstructured_payload.", 100_000);
        var language = RequiredText(payload, "language", errors, "unstructured_payload.", 8);
        if (language is not null && !LanguageCode().IsMatch(language))
        {
            errors.Add("unstructured_payload.language: must be an ISO 639-1 code such as 'en'");
        }

        if (OptionalText(payload, "source_url", errors, "unstructured_payload.") is { } url
            && !(Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https"))
        {
            errors.Add("unstructured_payload.source_url: must be an absolute http(s) URL");
        }

        return text is null ? null : "text:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    private static bool IsWellFormed(JsonElement text)
    {
        try
        {
            text.GetString();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A registry symbol in any letter case becomes the registry's spelling; other instruments are kept as given.</summary>
    private string? CanonicalSymbol(string? instrument) =>
        instrument is null
            ? null
            : registry.Symbols.Keys.FirstOrDefault(symbol => string.Equals(symbol, instrument, StringComparison.OrdinalIgnoreCase)) ?? instrument;

    private static void RejectUnstorableValues(JsonElement element, string path, List<string> errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number when !element.TryGetDouble(out var number) || !double.IsFinite(number):
                errors.Add($"{(path.Length == 0 ? "signal" : path)}: must be a finite number");
                break;
            case JsonValueKind.String when !IsWellFormed(element):
                errors.Add($"{(path.Length == 0 ? "signal" : path)}: is not valid Unicode text");
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    RejectUnstorableValues(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", errors);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    RejectUnstorableValues(item, $"{path}[{index++}]", errors);
                }

                break;
        }
    }

    /// <summary>
    /// A registry symbol names a macro signal: <c>CPI_YOY</c> or <c>econ:CPI_YOY</c> (any source
    /// name before the colon) resolves to <c>CPI_YOY</c>, as does a provider series id the registry
    /// maps (<c>provider:SERIES</c>); anything else keeps its source identifier and will be
    /// classified as an unknown indicator.
    /// </summary>
    private string MacroInstrument(string source)
    {
        if (CanonicalSymbol(source) is { } direct && registry.TryGetSymbol(direct, out _))
        {
            return direct;
        }

        var separator = source.IndexOf(':', StringComparison.Ordinal);
        if (separator > 0)
        {
            var provider = source[..separator];
            var series = source[(separator + 1)..];
            if (CanonicalSymbol(series) is { } named && registry.TryGetSymbol(named, out _))
            {
                return named;
            }

            var mapped = registry.Symbols.Values.FirstOrDefault(symbol =>
                symbol.IndicatorClass.SourceCategory == SourceCategory.Macroeconomic
                && string.Equals(symbol.Bootstrap?.Provider, provider, StringComparison.OrdinalIgnoreCase)
                && string.Equals(symbol.Bootstrap?.SeriesId, series, StringComparison.OrdinalIgnoreCase));
            if (mapped is not null)
            {
                return mapped.Symbol;
            }
        }

        return source;
    }

    private static void MatchesTimestamp(JsonElement payload, string name, DateTimeOffset? timestamp, List<string> errors)
    {
        if (RequiredTimestamp(payload, name, errors, "structured_payload.") is { } time && timestamp is { } signalTime && time != signalTime)
        {
            errors.Add($"structured_payload.{name}: must equal the signal timestamp");
        }
    }

    private static string? RequiredText(JsonElement element, string name, List<string> errors, string prefix = "", int maxLength = 200)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{prefix}{name}: required string");
            return null;
        }

        var text = property.GetString()!;
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add($"{prefix}{name}: must not be empty");
            return null;
        }

        if (text.Length > maxLength)
        {
            errors.Add($"{prefix}{name}: longer than {maxLength} characters");
            return null;
        }

        if (text.Any(char.IsControl) && maxLength <= 200)
        {
            errors.Add($"{prefix}{name}: must not contain control characters");
            return null;
        }

        return text;
    }

    private static string? OptionalText(JsonElement element, string name, List<string> errors, string prefix)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return RequiredText(element, name, errors, prefix);
    }

    private static string? RequiredEnum(JsonElement element, string name, string[] allowed, List<string> errors, string prefix = "")
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{prefix}{name}: required, one of {string.Join(", ", allowed)}");
            return null;
        }

        var text = property.GetString();
        if (!allowed.Contains(text, StringComparer.Ordinal))
        {
            errors.Add($"{prefix}{name}: '{text}' is not one of {string.Join(", ", allowed)}");
            return null;
        }

        return text;
    }

    private static void OptionalEnum(JsonElement element, string name, string[] allowed, List<string> errors, string prefix)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind != JsonValueKind.Null)
        {
            RequiredEnum(element, name, allowed, errors, prefix);
        }
    }

    private static double? RequiredNumber(JsonElement element, string name, List<string> errors, string prefix = "")
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"{prefix}{name}: required number");
            return null;
        }

        if (!property.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            errors.Add($"{prefix}{name}: must be a finite number");
            return null;
        }

        return number;
    }

    private static DateTimeOffset? RequiredTimestamp(JsonElement element, string name, List<string> errors, string prefix = "")
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{prefix}{name}: required date-time string");
            return null;
        }

        var text = property.GetString()!;
        if (!ExplicitOffset().IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            errors.Add($"{prefix}{name}: must be an ISO 8601 date-time with an explicit offset, e.g. 2026-02-27T21:15:00Z");
            return null;
        }

        if (parsed < Earliest)
        {
            errors.Add($"{prefix}{name}: earlier than 1990-01-01");
            return null;
        }

        return parsed;
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex ExplicitOffset();

    [GeneratedRegex("^[a-z]{2}$")]
    private static partial Regex LanguageCode();
}
