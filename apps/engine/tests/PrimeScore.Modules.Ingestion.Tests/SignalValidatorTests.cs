using System.Text.Json;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Validation;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Tests;

public sealed class SignalValidatorTests
{
    private static readonly SignalValidator Validator = new(
        IndicatorRegistryLoader.Load(IndicatorRegistryLoader.ResolvePath(null, AppContext.BaseDirectory)),
        new TestClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void A_complete_market_data_signal_becomes_a_candidate_for_its_instrument()
    {
        var candidate = Assert.IsType<SignalCandidate>(Validate(MarketData()));

        Assert.Equal(SourceCategory.MarketData, candidate.Category);
        Assert.Equal("VIX", candidate.Instrument);
        Assert.Equal(37.32, candidate.Value);
        Assert.Equal(new DateTimeOffset(2018, 2, 5, 21, 15, 0, TimeSpan.Zero), candidate.ObservedAt);
        Assert.Equal("api", candidate.Provenance.Provider);
        Assert.Equal("tester", candidate.Provenance.SubmittedBy);
    }

    [Fact]
    public void A_macro_signal_naming_a_registry_symbol_after_its_source_maps_to_that_symbol()
    {
        var candidate = Assert.IsType<SignalCandidate>(Validate("""
            {"source_category":"MACROECONOMIC","source_identifier":"econ:cpi_yoy","timestamp":"2022-07-13T12:30:00Z","payload_type":"STRUCTURED",
             "structured_payload":{"indicator_type":"INFLATION","region":"US","value":9.1,"prior_value":8.6,"release_date":"2022-07-13T12:30:00Z"}}
            """));

        Assert.Equal("CPI_YOY", candidate.Instrument);
        Assert.Equal("INFLATION", candidate.Variant);
        Assert.Equal(9.1, candidate.Value);
    }

    [Fact]
    public void The_fred_prefix_is_reserved_for_the_engines_adapter() =>
        AssertRejected(MarketData().Replace("cboe_vix", "FRED:VIXCLS", StringComparison.Ordinal), "source_identifier: the 'fred:' prefix is reserved");

    [Theory]
    [InlineData("fred:VIXCLS", "fred:")]
    [InlineData("cboe:VIX", "cboe:")]
    [InlineData("CBOE:VIX", "cboe:")]
    [InlineData("cboe-vx:VX", "cboe-vx:")]
    [InlineData("bls:CPI", "bls:")]
    [InlineData("bea:GDP", "bea:")]
    [InlineData("cal:FOMC", "cal:")]
    [InlineData("nowcast:CPI", "nowcast:")]
    [InlineData("pm:FOMC", "pm:")]
    [InlineData("gdelt:Europe", "gdelt:")]
    [InlineData("gpr:GPR", "gpr:")]
    [InlineData("usgs:quake", "usgs:")]
    public void Source_prefixes_of_the_engines_adapters_are_reserved(string source, string prefix) =>
        AssertRejected(MarketData().Replace("cboe_vix", source, StringComparison.Ordinal), $"source_identifier: the '{prefix}' prefix is reserved for the engine's source adapters");

    [Fact]
    public void A_source_name_that_only_resembles_a_reserved_prefix_is_accepted() =>
        Assert.IsType<SignalCandidate>(Validate(MarketData()));

    [Fact]
    public void A_registry_symbol_in_any_case_is_stored_in_the_registrys_spelling_and_the_variant_carries_metric_and_tenor()
    {
        var candidate = Assert.IsType<SignalCandidate>(Validate(MarketData()
            .Replace("\"instrument\":\"VIX\"", "\"instrument\":\"vix\"", StringComparison.Ordinal)
            .Replace("\"metric_type\":\"IMPLIED_VOLATILITY\"", "\"metric_type\":\"TERM_STRUCTURE\",\"tenor\":\"1M\"", StringComparison.Ordinal)));

        Assert.Equal("VIX", candidate.Instrument);
        Assert.Equal("TERM_STRUCTURE:1M", candidate.Variant);
    }

    [Theory]
    [InlineData("\"value\":37.32", "\"value\":1e999", "structured_payload.value: must be a finite number")]
    [InlineData("\"unit\":\"points\"", "\"unit\":\"points\",\"note\":-1e400", "structured_payload.note: must be a finite number")]
    [InlineData("\"unit\":\"points\"", "\"unit\":\"points\",\"tags\":[1,2e308]", "structured_payload.tags[1]: must be a finite number")]
    [InlineData("\"unit\":\"points\"", "\"unit\":\"\\ud800\"", "structured_payload.unit: is not valid Unicode text")]
    public void Values_the_ledger_cannot_store_are_rejected_wherever_they_appear(string original, string replacement, string expected)
    {
        var rejection = Assert.IsType<SignalRejection>(Validate(MarketData().Replace(original, replacement, StringComparison.Ordinal)));

        Assert.Contains(rejection.Errors, error => error.StartsWith(expected, StringComparison.Ordinal));
        Assert.Contains(replacement, rejection.Raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_texts_from_one_source_at_one_instant_have_distinct_variants()
    {
        static string Text(string text) =>
            $$$"""{"source_category":"GEOPOLITICAL","source_identifier":"wire","timestamp":"2022-02-24T03:00:00Z","payload_type":"UNSTRUCTURED","unstructured_payload":{"text":"{{{text}}}","language":"en"}}""";

        var first = Assert.IsType<SignalCandidate>(Validate(Text("Troops cross the border")));
        var second = Assert.IsType<SignalCandidate>(Validate(Text("Airports closed")));

        Assert.Equal(first.Instrument, second.Instrument);
        Assert.NotEqual(first.Variant, second.Variant);
        Assert.StartsWith("text:", first.Variant, StringComparison.Ordinal);
    }

    [Fact]
    public void A_curated_geopolitical_event_is_labelled_human_curated_and_carries_no_measured_value()
    {
        var candidate = Assert.IsType<SignalCandidate>(Validate("""
            {"source_category":"GEOPOLITICAL","source_identifier":"curated:srs-validation-set","timestamp":"2022-02-24T03:00:00Z","payload_type":"STRUCTURED",
             "structured_payload":{"event_region":"Europe","event_type":"military_strike","severity_estimate":0.9,"actors":["RU","UA"],"escalation_level":"ACTION"}}
            """));

        Assert.Equal("HUMAN_CURATED", candidate.Provenance.Provider);
        Assert.Equal("GEO:Europe", candidate.Instrument);
        Assert.Null(candidate.Value);
    }

    [Fact]
    public void A_cross_asset_flow_signal_is_keyed_by_its_asset_pair()
    {
        var candidate = Assert.IsType<SignalCandidate>(Validate("""
            {"source_category":"CROSS_ASSET_FLOW","source_identifier":"desk:corr","timestamp":"2020-02-24T21:00:00Z","payload_type":"STRUCTURED",
             "structured_payload":{"flow_type":"CORRELATION_BREAKDOWN","asset_pair":"SPX/TLT","value":-0.2,"baseline_value":-0.6,"lookback_period":"90d"}}
            """));

        Assert.Equal("SPX/TLT", candidate.Instrument);
        Assert.Equal(-0.2, candidate.Value);
    }

    [Theory]
    [InlineData("""{"source_identifier":"x","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED","structured_payload":{}}""", "source_category: required")]
    [InlineData("""{"source_category":"WEATHER","source_identifier":"x","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED","structured_payload":{}}""", "source_category: 'WEATHER' is not one of")]
    [InlineData("""{"source_category":"MARKET_DATA","source_identifier":"","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED","structured_payload":{}}""", "source_identifier: must not be empty")]
    [InlineData("""{"source_category":"MARKET_DATA","source_identifier":"x","timestamp":"2018-02-05T21:15:00","payload_type":"STRUCTURED","structured_payload":{}}""", "timestamp: must be an ISO 8601 date-time with an explicit offset")]
    [InlineData("""{"source_category":"MARKET_DATA","source_identifier":"x","timestamp":"2027-01-01T00:00:00Z","payload_type":"STRUCTURED","structured_payload":{}}""", "timestamp: is in the future")]
    [InlineData("""{"source_category":"MARKET_DATA","source_identifier":"x","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED"}""", "structured_payload: required object")]
    [InlineData("""{"source_category":"MARKET_DATA","source_identifier":"x","timestamp":"2018-02-05T21:15:00Z","payload_type":"UNSTRUCTURED","unstructured_payload":{"language":"en"}}""", "unstructured_payload.text: required string")]
    [InlineData("[1,2]", "signal: must be a JSON object")]
    public void Malformed_envelopes_are_rejected_with_the_field_named(string json, string expected) =>
        AssertRejected(json, expected);

    [Theory]
    [InlineData("\"value\":37.32", "\"value\":\"high\"", "structured_payload.value: required number")]
    [InlineData("\"metric_type\":\"IMPLIED_VOLATILITY\"", "\"metric_type\":\"VIBES\"", "structured_payload.metric_type: 'VIBES' is not one of")]
    [InlineData("\"observed_at\":\"2018-02-05T21:15:00Z\"", "\"observed_at\":\"2018-02-06T21:15:00Z\"", "structured_payload.observed_at: must equal the signal timestamp")]
    [InlineData("\"instrument\":\"VIX\",", "", "structured_payload.instrument: required string")]
    [InlineData("\"metric_type\":\"IMPLIED_VOLATILITY\"", "\"metric_type\":\"TERM_STRUCTURE\"", "structured_payload.tenor: required when metric_type is TERM_STRUCTURE")]
    public void Malformed_market_data_payloads_are_rejected(string original, string replacement, string expected) =>
        AssertRejected(MarketData().Replace(original, replacement, StringComparison.Ordinal), expected);

    [Theory]
    [InlineData("""{"event_region":"Europe","event_type":"strike","severity_estimate":1.5,"actors":["RU"]}""", "structured_payload.severity_estimate: must lie in [0, 1]")]
    [InlineData("""{"event_region":"Europe","event_type":"strike","severity_estimate":0.5,"actors":"RU"}""", "structured_payload.actors: required array of strings")]
    [InlineData("""{"event_region":"Europe","event_type":"strike","severity_estimate":0.5,"actors":["RU"],"escalation_level":"PANIC"}""", "structured_payload.escalation_level: 'PANIC' is not one of")]
    public void Malformed_geopolitical_payloads_are_rejected(string payload, string expected) =>
        AssertRejected($$"""{"source_category":"GEOPOLITICAL","source_identifier":"curated:x","timestamp":"2022-02-24T03:00:00Z","payload_type":"STRUCTURED","structured_payload":{{payload}}}""", expected);

    [Fact]
    public void A_macro_print_without_a_value_is_rejected_unless_it_is_a_central_bank_statement()
    {
        AssertRejected("""
            {"source_category":"MACROECONOMIC","source_identifier":"dol:INITIAL_CLAIMS","timestamp":"2026-09-24T12:30:00Z","payload_type":"STRUCTURED",
             "structured_payload":{"indicator_type":"EMPLOYMENT","region":"US","release_date":"2026-09-24T12:30:00Z"}}
            """, "structured_payload.value: required number unless indicator_type is CENTRAL_BANK_STATEMENT");

        Assert.IsType<SignalCandidate>(Validate("""
            {"source_category":"MACROECONOMIC","source_identifier":"curated:fomc","timestamp":"2024-12-18T19:00:00Z","payload_type":"STRUCTURED",
             "structured_payload":{"indicator_type":"CENTRAL_BANK_STATEMENT","region":"US","value":null,"release_date":"2024-12-18T19:00:00Z"}}
            """));
    }

    [Fact]
    public void Every_error_of_a_signal_is_reported_not_only_the_first()
    {
        var rejection = Assert.IsType<SignalRejection>(Validate("""{"source_category":"MARKET_DATA","payload_type":"STRUCTURED","structured_payload":{"value":"x"}}"""));

        Assert.Contains(rejection.Errors, error => error.StartsWith("source_identifier:", StringComparison.Ordinal));
        Assert.Contains(rejection.Errors, error => error.StartsWith("timestamp:", StringComparison.Ordinal));
        Assert.Contains(rejection.Errors, error => error.StartsWith("structured_payload.value:", StringComparison.Ordinal));
        Assert.True(rejection.Errors.Count >= 5);
    }

    private static string MarketData() => """
        {"source_category":"MARKET_DATA","source_identifier":"cboe_vix","timestamp":"2018-02-05T21:15:00Z","payload_type":"STRUCTURED",
         "structured_payload":{"asset_class":"equity_index","instrument":"VIX","metric_type":"IMPLIED_VOLATILITY","value":37.32,"unit":"points","observed_at":"2018-02-05T21:15:00Z"}}
        """;

    private static object Validate(string json) => Validator.Validate(JsonDocument.Parse(json).RootElement.Clone(), "tester");

    private static void AssertRejected(string json, string expectedError)
    {
        var rejection = Assert.IsType<SignalRejection>(Validate(json));
        Assert.Contains(rejection.Errors, error => error.StartsWith(expectedError, StringComparison.Ordinal));
    }
}

internal sealed class TestClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}
