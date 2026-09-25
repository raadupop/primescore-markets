using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;
using static PrimeScore.Acceptance.Api.Harness.SignalDocuments;

namespace PrimeScore.Acceptance.Api;

/// <summary>SRS SIG-001 (four categories) and SIG-002 (per-signal validation with structured errors).</summary>
public sealed class IngestionTests(EngineFixture fixture)
{
    private static readonly DateTimeOffset Base = new(2019, 9, 13, 20, 15, 0, TimeSpan.Zero);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SIG_001_one_batch_carrying_all_four_source_categories_is_accepted()
    {
        var response = await PostAsync(Batch(
            MarketData("VIX", 15.0, Base.AddMinutes(1)),
            Macroeconomic("acceptance:cpi", 2.1, Base.AddMinutes(2)),
            Geopolitical("Middle East", 0.8, Base.AddMinutes(3)),
            CrossAssetFlow("SPX/TLT", -0.2, -0.6, Base.AddMinutes(4))));

        Assert.Equal(4, response.Accepted_count);
        Assert.Equal(0, response.Rejected_count);
        Assert.All(response.Signals, signal => Assert.Equal(ValidationStatus.ACCEPTED, signal.Validation_status));
        Assert.Equal(4, response.Signals.Select(signal => signal.Signal_id).Distinct().Count());
    }

    [Fact]
    public async Task SIG_002_malformed_signals_are_rejected_individually_while_valid_ones_in_the_batch_are_recorded()
    {
        var missingValue = MarketData("VIX", 16.0, Base.AddHours(1));
        ((JsonObject)missingValue["structured_payload"]!).Remove("value");
        var unknownCategory = MarketData("VIX", 16.0, Base.AddHours(2));
        unknownCategory["source_category"] = "WEATHER";
        var noOffset = MarketData("VIX", 16.0, Base.AddHours(3));
        noOffset["timestamp"] = "2019-09-13T23:15:00";
        var severityOutOfRange = Geopolitical("Europe", 1.5, Base.AddHours(4));

        var response = await PostAsync(Batch(
            MarketData("OVX", 35.0, Base.AddHours(5)),
            missingValue,
            unknownCategory,
            noOffset,
            severityOutOfRange,
            JsonValue.Create(42)!));

        Assert.Equal(1, response.Accepted_count);
        Assert.Equal(5, response.Rejected_count);
        var results = response.Signals.ToArray();
        Assert.Equal(ValidationStatus.ACCEPTED, results[0].Validation_status);
        AssertRejected(results[1], "structured_payload.value");
        AssertRejected(results[2], "source_category");
        AssertRejected(results[3], "timestamp");
        AssertRejected(results[4], "structured_payload.severity_estimate");
        AssertRejected(results[5], "signal: must be a JSON object");
    }

    [Fact]
    public async Task Resubmitting_an_identical_observation_returns_the_same_signal_id_and_records_nothing_new()
    {
        var document = MarketData("VXN", 21.5, Base.AddDays(1), source: "acceptance:idempotency");

        var first = await PostAsync(Batch(document.DeepClone()));
        var second = await PostAsync(Batch(document.DeepClone()));

        Assert.Equal(ValidationStatus.ACCEPTED, second.Signals.Single().Validation_status);
        Assert.Equal(first.Signals.Single().Signal_id, second.Signals.Single().Signal_id);
    }

    [Fact]
    public async Task A_different_payload_for_an_observation_already_recorded_is_refused_and_the_first_stays()
    {
        var original = MarketData("VXN", 22.5, Base.AddDays(2), source: "acceptance:conflict");
        var changed = MarketData("VXN", 23.0, Base.AddDays(2), source: "acceptance:conflict");

        var first = await PostAsync(Batch(original));
        var second = await PostAsync(Batch(changed));

        AssertRejected(second.Signals.Single(), "signal: conflicts with recorded signal " + first.Signals.Single().Signal_id);
    }

    [Fact]
    public async Task SIG_002_a_number_the_ledger_cannot_store_rejects_only_its_own_signal()
    {
        var valid = MarketData("VXN", 24.5, Base.AddDays(3), source: "acceptance:overflow").ToJsonString();
        var overflowing = MarketData("VXN", 424242.5, Base.AddDays(4), source: "acceptance:overflow").ToJsonString()
            .Replace("424242.5", "1e999", StringComparison.Ordinal);

        var response = await PostRawAsync($$"""{"signals":[{{valid}},{{overflowing}}]}""");

        Assert.Equal(1, response.Accepted_count);
        Assert.Equal(1, response.Rejected_count);
        AssertRejected(response.Signals.ElementAt(1), "structured_payload.value: must be a finite number");
    }

    [Fact]
    public async Task The_fred_source_prefix_is_reserved_for_the_engines_own_adapter()
    {
        var response = await PostAsync(Batch(MarketData("VIX", 18.0, Base.AddDays(5), source: "fred:VIXCLS")));

        AssertRejected(response.Signals.Single(), "source_identifier: the 'fred:' prefix is reserved");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"signals":"not an array"}""")]
    [InlineData("""{"signals":[]}""")]
    [InlineData("[]")]
    public async Task A_request_that_is_not_an_object_with_a_non_empty_signals_array_is_400(string body)
    {
        using var http = fixture.Engine.Http(Role.Admin);

        using var response = await http.PostAsync(new Uri("admin/signals", UriKind.Relative), new StringContent(body, Encoding.UTF8, "application/json"), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(Token);
        Assert.Equal("bad_request", error!.Error);
    }

    private static void AssertRejected(SignalResult result, string field)
    {
        Assert.Equal(ValidationStatus.REJECTED, result.Validation_status);
        Assert.NotEqual(Guid.Empty, result.Signal_id);
        Assert.Contains(result.Validation_errors!, error => error.StartsWith(field, StringComparison.Ordinal));
    }

    private Task<IngestSignalsResponse> PostAsync(JsonObject batch) => PostRawAsync(batch.ToJsonString());

    private async Task<IngestSignalsResponse> PostRawAsync(string batch)
    {
        using var http = fixture.Engine.Http(Role.Admin);
        using var response = await http.PostAsync(new Uri("admin/signals", UriKind.Relative),
            new StringContent(batch, Encoding.UTF8, "application/json"), Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<IngestSignalsResponse>(ApiJsonOptions.Value, Token))!;
    }
}

internal static class ApiJsonOptions
{
    public static readonly System.Text.Json.JsonSerializerOptions Value = Create();

    private static System.Text.Json.JsonSerializerOptions Create()
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        ApiJson.Configure(options);
        return options;
    }
}
