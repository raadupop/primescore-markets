using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Ingestion;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Classification.Tests;

/// <summary>The classification run against real modules, a temporary database and a scripted classifier.</summary>
public sealed class ClassificationRunnerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Day1 = new(2018, 2, 1, 21, 15, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-classification-tests", Guid.NewGuid().ToString("N"));
    private readonly ScriptedClassifier _classifier = new();
    private ServiceProvider _services = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "consensus"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
            ["Consensus:Directory"] = Path.Combine(_directory, "consensus"),
            ["Classifier:BaseUrl"] = "http://classifier.test/",
        }).Build();
        var services = new ServiceCollection()
            .AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration)
            .AddClassificationModule(configuration);
        services.AddHttpClient("classifier").ConfigurePrimaryHttpMessageHandler(() => _classifier);
        _services = services.BuildServiceProvider();
        var registry = _services.GetRequiredService<PrimeScore.SharedKernel.Registry.IndicatorRegistry>();
        _classifier.TargetLength = symbol => registry.TryGetSymbol(symbol, out var entry) ? entry.IndicatorClass.ReferenceWindowLength : 0;
        await EngineDatabaseInitializer.InitializeAsync(_services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Each_signal_is_classified_against_only_the_values_observed_before_it()
    {
        // Ingestion classifies the batch before it returns (SRS NFR-001).
        await IngestAsync(Market("VIX", 11.0, Day1), Market("VIX", 12.0, Day1.AddDays(1)), Market("VIX", 30.0, Day1.AddDays(4)));

        Assert.Equal(3, (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Count(view => view.Available));
        var windows = _classifier.Requests.Select(request => request["reference_window"]!["values"]!.AsArray().Select(value => value!.GetValue<double>()).ToArray()).ToArray();
        Assert.Equal([Array.Empty<double>(), [11.0], [11.0, 12.0]], windows);
        Assert.Equal("2018-02-02T21:15:00+00:00", _classifier.Requests[2]["reference_window"]!["last_update"]!.GetValue<string>());
    }

    [Fact]
    public async Task An_assessment_carries_the_correlation_id_of_its_signal()
    {
        await IngestAsync(Market("VIX", 11.0, Day1));

        var signal = (await Query<GetSignals, SignalPage>(new GetSignals(new SignalFilter()))).Signals.Single();
        var assessment = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.Equal(signal.CorrelationId, assessment.CorrelationId);
        Assert.Equal(signal.SignalId, assessment.SignalId);
    }

    [Fact]
    public async Task A_classifier_outage_reuses_the_last_real_assessment_with_its_staleness_and_is_retried_later()
    {
        await IngestAsync(Market("VIX", 11.0, Day1));
        _classifier.Down = true;
        await IngestAsync(Market("VIX", 12.0, Day1.AddDays(1)));

        var again = await RunAsync();
        var fallback = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments(AsOf: Day1.AddDays(1)))).First();

        Assert.Equal(0, again.Fallbacks + again.Unavailable + again.Classified);
        Assert.True(fallback.IsFallback);
        Assert.Equal(86400, fallback.StalenessSeconds);
        Assert.Equal(0.25, fallback.Score);

        _classifier.Down = false;
        var recovered = await RunAsync();
        var latest = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments(AsOf: Day1.AddDays(1)))).First();

        Assert.Equal(1, recovered.Classified);
        Assert.False(latest.IsFallback);
    }

    [Fact]
    public async Task Without_an_earlier_assessment_an_outage_is_recorded_as_unavailable_not_silence()
    {
        _classifier.Down = true;
        await IngestAsync(Market("OVX", 35.0, Day1));

        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.False(outcome.Available);
        Assert.Equal(UnavailableReason.ClassifierUnreachable, outcome.Reason);
    }

    [Fact]
    public async Task Three_consecutive_unreachable_answers_stop_the_run_and_leave_the_rest_pending()
    {
        _classifier.Down = true;
        await IngestAsync(Enumerable.Range(0, 6).Select(day => Market("GVZ", 20 + day, Day1.AddDays(day))).ToArray());
        Assert.Equal(3, _classifier.Requests.Count);

        var ack = await RunAsync();

        Assert.Equal(6, _classifier.Requests.Count);
        Assert.Equal(3, ack.Remaining);
    }

    [Fact]
    public async Task A_macro_print_without_sourced_consensus_is_awaiting_consensus_and_never_sent()
    {
        await IngestAsync(Macro("dol:INITIAL_CLAIMS", 219000, new DateTimeOffset(2026, 4, 9, 12, 30, 0, TimeSpan.Zero)));

        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.Empty(_classifier.Requests);
        Assert.Equal(UnavailableReason.AwaitingConsensus, outcome.Reason);
    }

    [Fact]
    public async Task A_macro_print_with_consensus_is_sent_with_it_and_prior_surprises_form_the_window()
    {
        await File.WriteAllLinesAsync(Path.Combine(_directory, "consensus", "INITIAL_CLAIMS.csv"),
        [
            "release_date,actual_source,consensus,consensus_source,consensus_url,retrieved_at",
            "2026-03-26,DOL,215000,Survey,https://example.org/a,2026-09-25T09:00:00Z",
            "2026-04-09,DOL,210000,Survey,https://example.org/b,2026-09-25T09:00:00Z",
        ], Token);
        await IngestAsync(
            Macro("dol:INITIAL_CLAIMS", 211000, new DateTimeOffset(2026, 3, 26, 12, 30, 0, TimeSpan.Zero)),
            Macro("dol:INITIAL_CLAIMS", 224000, new DateTimeOffset(2026, 4, 2, 12, 30, 0, TimeSpan.Zero)),
            Macro("dol:INITIAL_CLAIMS", 219000, new DateTimeOffset(2026, 4, 9, 12, 30, 0, TimeSpan.Zero)));

        var request = _classifier.Requests.Last();

        Assert.Equal(2, _classifier.Requests.Count);
        Assert.Equal(210000, request["structured_payload"]!["expected"]!.GetValue<double>());
        // Only the 2026-03-26 print has a consensus row: |211000 − 215000| = 4000. 2026-04-02 is skipped, not guessed.
        Assert.Equal([4000.0], request["reference_window"]!["values"]!.AsArray().Select(value => value!.GetValue<double>()));
        var awaiting = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single(view => view.ObservedAt.Day == 2);
        Assert.Equal(UnavailableReason.AwaitingConsensus, awaiting.Reason);
    }

    [Fact]
    public async Task A_geopolitical_signal_is_recorded_as_route_not_implemented_and_not_retried()
    {
        _classifier.NotImplemented = true;
        await IngestAsync("""{"source_category":"GEOPOLITICAL","source_identifier":"curated:x","timestamp":"2022-02-24T03:00:00Z","payload_type":"STRUCTURED","structured_payload":{"event_region":"Europe","event_type":"strike","severity_estimate":0.9,"actors":["RU"]}}""");

        await RunAsync();
        await RunAsync();
        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        // An empty window: the request never depends on the classifier's own bootstrap state (503 until ready).
        Assert.Empty(Assert.Single(_classifier.Requests)["reference_window"]!["values"]!.AsArray());
        Assert.Equal(UnavailableReason.RouteNotImplemented, outcome.Reason);
    }

    [Fact]
    public async Task After_the_breaker_trips_a_later_signal_gets_its_fallback_locally_without_a_call()
    {
        await IngestAsync(Market("VIX", 11.0, Day1));
        var original = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();
        _classifier.Down = true;

        // Three flows fail first (observation order), then VIX is not sent at all.
        await IngestAsync(
            Flow("SPX/TLT", -0.2, Day1.AddDays(1)),
            Flow("SPX/GLD", 0.1, Day1.AddDays(1)),
            Flow("SPX/DXY", 0.3, Day1.AddDays(1)),
            Market("VIX", 12.0, Day1.AddDays(2)));
        var vix = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments(Instrument: "VIX"))).First();

        Assert.Equal(4, _classifier.Requests.Count);
        Assert.Equal(Day1.AddDays(2), vix.ObservedAt);
        Assert.True(vix.IsFallback);
        Assert.Equal(original.AssessmentId, vix.FallbackOf);
        Assert.Equal(2 * 86400, vix.StalenessSeconds);
        Assert.Contains("Not sent", vix.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(404, "<html><body>Not Found</body></html>")]
    [InlineData(404, """{"detail":"Not Found"}""")]
    [InlineData(400, """{"error":"bad request at the proxy"}""")]
    [InlineData(429, "")]
    [InlineData(503, """{"detail":"bootstrapping"}""")]
    public async Task A_status_outside_the_classifier_contract_is_no_answer_and_is_retried(int status, string body)
    {
        _classifier.Respond = _ => Reply(status, body);
        await IngestAsync(Market("OVX", 35.0, Day1));
        var failed = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        _classifier.Respond = null;
        var retried = await RunAsync();

        Assert.Equal(UnavailableReason.ClassifierUnreachable, failed.Reason);
        Assert.Equal(1, retried.Classified);
        Assert.Equal(2, _classifier.Requests.Count);
    }

    [Theory]
    [InlineData(422, """{"detail":[{"loc":["body","reference_window","values",0],"msg":"Input should be a finite number","type":"finite_number"}]}""", "finite_number")]
    [InlineData(400, """{"detail":"current_value must be positive"}""", "current_value must be positive")]
    public async Task A_request_the_classifier_rejects_in_its_own_error_shape_is_final(int status, string body, string detail)
    {
        _classifier.Respond = _ => Reply(status, body);
        await IngestAsync(Market("OVX", 35.0, Day1));

        _classifier.Respond = null;
        await RunAsync();
        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.Single(_classifier.Requests);
        Assert.Equal(UnavailableReason.ClassifierRejected, outcome.Reason);
        Assert.Contains(detail, outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_ranked_against_a_window_other_than_the_one_sent_is_invalid()
    {
        // The engine sends an empty window (expected sufficiency 0); 1.0 means the classifier used its own history.
        _classifier.ReportedSufficiency = 1.0;
        await IngestAsync(Market("VIX", 11.0, Day1));

        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.False(outcome.Available);
        Assert.Equal(UnavailableReason.InvalidResponse, outcome.Reason);
        Assert.Contains("does not match the 0-value reference window", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_could_not_rank_is_accepted_with_the_insufficient_history_flag()
    {
        _classifier.ReportedSufficiency = 0.0;
        _classifier.Metrics = """{"history_length":0,"ecdf_rank":null}""";
        await IngestAsync(Market("VIX", 11.0, Day1));

        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.True(outcome.Available);
        Assert.Equal(["insufficient_history"], outcome.Flags);
    }

    [Fact]
    public async Task Free_text_is_recorded_as_route_not_implemented_without_a_call()
    {
        await IngestAsync("""{"source_category":"GEOPOLITICAL","source_identifier":"wire","timestamp":"2022-02-24T03:00:00Z","payload_type":"UNSTRUCTURED","unstructured_payload":{"text":"Troops cross the border","language":"en"}}""");

        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.Empty(_classifier.Requests);
        Assert.Equal(UnavailableReason.RouteNotImplemented, outcome.Reason);
    }

    [Fact]
    public async Task A_macro_print_outside_the_registry_is_an_unknown_indicator_and_final()
    {
        await IngestAsync(Macro("econ:MYSTERY_INDEX", 1.5, new DateTimeOffset(2026, 4, 9, 12, 30, 0, TimeSpan.Zero)));

        var again = await RunAsync();
        var outcome = (await Query<GetAssessments, IReadOnlyList<AssessmentView>>(new GetAssessments())).Single();

        Assert.Empty(_classifier.Requests);
        Assert.Equal(UnavailableReason.UnknownIndicator, outcome.Reason);
        Assert.Equal(0, again.Unavailable);
    }

    private static HttpResponseMessage Reply(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private async Task<ClassifyPendingAck> RunAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<ClassifyPendingSignals, ClassifyPendingAck>>()
            .HandleAsync(new ClassifyPendingSignals(), Token);
    }

    private async Task IngestAsync(params string[] documents)
    {
        using var scope = _services.CreateScope();
        var ack = await scope.ServiceProvider.GetRequiredService<ICommandHandler<IngestSignals, IngestSignalsAck>>()
            .HandleAsync(new IngestSignals(documents.Select(document => JsonDocument.Parse(document).RootElement.Clone()).ToArray(), "test"), Token);
        Assert.Equal(documents.Length, ack.AcceptedCount);
    }

    private async Task<TResult> Query<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query, Token);
    }

    private static string Market(string instrument, double value, DateTimeOffset at)
    {
        var time = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return $$$"""{"source_category":"MARKET_DATA","source_identifier":"test","timestamp":"{{{time}}}","payload_type":"STRUCTURED","structured_payload":{"asset_class":"equity_index","instrument":"{{{instrument}}}","metric_type":"IMPLIED_VOLATILITY","value":{{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"unit":"points","observed_at":"{{{time}}}"}}""";
    }

    private static string Macro(string source, double value, DateTimeOffset at)
    {
        var time = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return $$$"""{"source_category":"MACROECONOMIC","source_identifier":"{{{source}}}","timestamp":"{{{time}}}","payload_type":"STRUCTURED","structured_payload":{"indicator_type":"EMPLOYMENT","region":"US","value":{{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"release_date":"{{{time}}}"}}""";
    }

    private static string Flow(string pair, double value, DateTimeOffset at)
    {
        var time = at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        return $$$"""{"source_category":"CROSS_ASSET_FLOW","source_identifier":"desk:corr","timestamp":"{{{time}}}","payload_type":"STRUCTURED","structured_payload":{"flow_type":"CORRELATION_BREAKDOWN","asset_pair":"{{{pair}}}","value":{{{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}}},"baseline_value":-0.6,"lookback_period":"90d"}}""";
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>
    /// Answers every classification with severity 0.25 and certainty 0.8, or fails as instructed.
    /// Like the real classifier, a market-data answer reports <c>history_sufficiency = min(1, sent / N_L)</c>.
    /// </summary>
    private sealed class ScriptedClassifier : HttpMessageHandler
    {
        public Func<string, int> TargetLength { get; set; } = _ => 0;

        public bool Down { get; set; }

        public bool NotImplemented { get; set; }

        /// <summary>Replaces the answer for as long as it is set.</summary>
        public Func<JsonObject, HttpResponseMessage>? Respond { get; set; }

        /// <summary>Overrides the reported history_sufficiency (e.g. a classifier that ignored the window sent).</summary>
        public double? ReportedSufficiency { get; set; }

        public string Metrics { get; set; } = "{}";

        public List<JsonObject> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                Requests.Add(new JsonObject());
                throw new HttpRequestException("connection refused (scripted)");
            }

            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());
            if (Respond is { } respond)
            {
                return respond(Requests[^1]);
            }

            if (NotImplemented)
            {
                return new HttpResponseMessage(HttpStatusCode.NotImplemented) { Content = new StringContent("""{"detail":"route not implemented"}""", Encoding.UTF8, "application/json") };
            }

            var body = Requests[^1];
            var sufficiency = 0.8;
            if (body["source_category"]?.GetValue<string>() == "MARKET_DATA" && TargetLength(body["structured_payload"]!["symbol"]!.GetValue<string>()) is > 0 and var length)
            {
                sufficiency = Math.Round(Math.Min(1.0, body["reference_window"]!["values"]!.AsArray().Count / (double)length), 4);
            }

            var answer = "{\"score\":0.25,\"score_type\":\"ANOMALY_DETECTION\",\"certainty\":0.8,\"history_sufficiency\":"
                + (ReportedSufficiency ?? sufficiency).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"temporal_relevance\":1.0,\"event_taxonomy\":null,\"classification_method\":\"RULE_BASED\",\"reasoning_trace\":\"scripted\",\"computed_metrics\":"
                + Metrics + "}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}
