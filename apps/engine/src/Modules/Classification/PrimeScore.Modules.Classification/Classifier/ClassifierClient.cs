using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrimeScore.Modules.Classification.Contracts;

namespace PrimeScore.Modules.Classification.Classifier;

/// <summary>A classifier answer that passed validation (field names as in the classifier contract).</summary>
internal sealed record ClassifierAnswer(
    double Score,
    string ScoreType,
    double Certainty,
    double? HistorySufficiency,
    double? TemporalRelevance,
    string ClassificationMethod,
    string? EventTaxonomy,
    string ReasoningTrace,
    JsonObject ComputedMetrics)
{
    public const string InsufficientHistory = "insufficient_history";

    private static readonly string[] Markers = ["window_degenerate", "unknown_indicator", "fit_rejected"];

    /// <summary>
    /// CLS-009 and fit markers set to true in <see cref="ComputedMetrics"/>, plus
    /// <c>insufficient_history</c> when the classifier could not rank at all (it reports the
    /// history length and a null rank).
    /// </summary>
    public IReadOnlyList<string> Flags
    {
        get
        {
            var flags = Markers
                .Where(marker => ComputedMetrics[marker] is JsonValue value && value.TryGetValue<bool>(out var set) && set)
                .ToList();
            if (ComputedMetrics.ContainsKey("history_length") && ComputedMetrics.ContainsKey("ecdf_rank") && ComputedMetrics["ecdf_rank"] is null)
            {
                flags.Add(InsufficientHistory);
            }

            return flags;
        }
    }
}

/// <summary>Either a validated answer or the reason there is none.</summary>
internal sealed record ClassifierOutcome(ClassifierAnswer? Answer, UnavailableReason? Reason, int? HttpStatus, string? Detail)
{
    public static ClassifierOutcome Unavailable(UnavailableReason reason, int? status, string detail) => new(null, reason, status, detail);
}

/// <summary>
/// <c>POST /classify</c>. The engine treats the classifier as an untrusted boundary: every
/// answer is validated against the classifier contract before it is recorded (SRS CLS-004).
/// Only an error in the classifier's own shape (<c>{"detail": ...}</c>) is taken as its
/// verdict; any other status (a proxy, a wrong port, 404, 429) is treated as no answer and
/// retried.
/// </summary>
internal sealed class ClassifierClient(IHttpClientFactory httpClientFactory)
{
    private static readonly string[] ScoreTypes = ["ANOMALY_DETECTION", "EVENT_ASSESSMENT"];
    private static readonly string[] Methods = ["RULE_BASED", "AI_MODEL"];

    public async Task<ClassifierOutcome> ClassifyAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(ClassifierOptions.HttpClientName);
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(new Uri("classify", UriKind.Relative), request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            return ClassifierOutcome.Unavailable(UnavailableReason.ClassifierUnreachable, null, exception.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ClassifierOutcome.Unavailable(UnavailableReason.ClassifierUnreachable, null, "The classifier did not answer before the timeout.");
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var verdict = ClassifierDetail(body);
            return response.StatusCode switch
            {
                HttpStatusCode.OK => Validate(body),
                HttpStatusCode.NotImplemented when verdict is not null => ClassifierOutcome.Unavailable(UnavailableReason.RouteNotImplemented, status, verdict),
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity when verdict is not null =>
                    ClassifierOutcome.Unavailable(UnavailableReason.ClassifierRejected, status, verdict),
                _ => ClassifierOutcome.Unavailable(UnavailableReason.ClassifierUnreachable, status, verdict ?? $"HTTP {status}: {Truncate(body)}"),
            };
        }
    }

    /// <summary>Checks every field the classifier contract requires; any violation rejects the whole answer.</summary>
    internal static ClassifierOutcome Validate(string body)
    {
        JsonObject answer;
        try
        {
            answer = JsonNode.Parse(body) as JsonObject ?? throw new JsonException("not an object");
        }
        catch (JsonException exception)
        {
            return Invalid($"response is not a JSON object ({exception.Message})");
        }

        var problems = new List<string>();
        var score = Number(answer, "score", -1, 1, problems, required: true);
        var scoreType = Text(answer, "score_type", ScoreTypes, problems);
        var certainty = Number(answer, "certainty", 0, 1, problems, required: true);
        var history = Number(answer, "history_sufficiency", 0, 1, problems, required: false);
        var temporal = Number(answer, "temporal_relevance", 0, 1, problems, required: false);
        var method = Text(answer, "classification_method", Methods, problems);
        var trace = answer["reasoning_trace"] is JsonValue traceValue && traceValue.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
        if (trace is null)
        {
            problems.Add("reasoning_trace: required non-empty string");
        }

        if (answer["computed_metrics"] is not JsonObject metrics)
        {
            problems.Add("computed_metrics: required object");
            metrics = [];
        }

        if (scoreType == "EVENT_ASSESSMENT" && score < 0)
        {
            problems.Add("score: EVENT_ASSESSMENT scores lie in [0, 1]");
        }

        var taxonomy = answer["event_taxonomy"] is JsonValue taxonomyValue && taxonomyValue.TryGetValue<string>(out var taxonomyText) ? taxonomyText : null;
        return problems.Count > 0
            ? Invalid(string.Join("; ", problems))
            : new ClassifierOutcome(
                new ClassifierAnswer(score!.Value, scoreType!, certainty!.Value, history, temporal, method!, taxonomy, trace!, (JsonObject)metrics.DeepClone()),
                null, 200, null);
    }

    private static ClassifierOutcome Invalid(string detail) =>
        ClassifierOutcome.Unavailable(UnavailableReason.InvalidResponse, 200, detail);

    private static double? Number(JsonObject answer, string name, double min, double max, List<string> problems, bool required)
    {
        var node = answer[name];
        if (node is null)
        {
            if (required)
            {
                problems.Add($"{name}: required number");
            }

            return null;
        }

        if (node is not JsonValue value || !value.TryGetValue<double>(out var number) || !double.IsFinite(number) || number < min || number > max)
        {
            problems.Add($"{name}: must be a number in [{min}, {max}]");
            return null;
        }

        return number;
    }

    private static string? Text(JsonObject answer, string name, string[] allowed, List<string> problems)
    {
        if (answer[name] is JsonValue value && value.TryGetValue<string>(out var text) && allowed.Contains(text, StringComparer.Ordinal))
        {
            return text;
        }

        problems.Add($"{name}: must be one of {string.Join(", ", allowed)}");
        return null;
    }

    /// <summary>The classifier's error text (FastAPI <c>detail</c>, a string or a validation list); null for any other body.</summary>
    private static string? ClassifierDetail(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject error && error.Count == 1)
            {
                return error["detail"] switch
                {
                    JsonValue detail when detail.TryGetValue<string>(out var text) => text,
                    JsonArray list => Truncate(list.ToJsonString()),
                    _ => null,
                };
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string Truncate(string body) => body.Length <= 300 ? body : body[..300];
}
