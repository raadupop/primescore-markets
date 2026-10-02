using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrimeScore.Modules.Classification.Classifier;
using PrimeScore.Modules.Classification.Consensus;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Classification.Pipeline;

/// <summary>One earlier macro surprise in a print's reference window, with the consensus row behind it.</summary>
internal sealed record MacroSurprise(DateOnly ReleaseDate, Guid SignalId, double Actual, ConsensusUsed Consensus, double Magnitude);

/// <summary>What one classification attempt produced, with the inputs it was computed on.</summary>
/// <param name="MacroWindow">For a macro print, the earlier surprises its window was built from (their consensus rows can change later).</param>
internal sealed record ClassificationAttempt(
    ClassifierOutcome Outcome,
    int ReferenceWindowLength,
    DateTimeOffset? ReferenceWindowLastUpdate,
    ConsensusUsed? Consensus,
    IReadOnlyList<MacroSurprise>? MacroWindow = null);

/// <summary>
/// First New York date on which adapter-recorded signals are assessed (<c>Classification:AdapterHistoryFrom</c>,
/// default 2011-01-01, the start of the history already classified from FRED). Earlier adapter rows are recorded and
/// feed reference windows; assessing them needs a research registration first (ADR-0010). Read once at startup; an
/// invalid value stops the start rather than classifying history the operator meant to exclude.
/// </summary>
internal sealed record AdapterHistory(DateOnly From)
{
    public const string Key = "Classification:AdapterHistoryFrom";

    public static AdapterHistory Parse(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? new(new DateOnly(2011, 1, 1))
            : DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
                ? new(from)
                : throw new InvalidOperationException($"{Key} must be a date in yyyy-MM-dd form.");
}

/// <summary>
/// Builds one point-in-time classifier request per signal: the reference window holds only
/// observations strictly before the signal (brief §8), a macro print is sent only with a
/// sourced consensus row for its release date, and nothing missing is ever filled in.
/// Adapter-recorded signals first pass <see cref="AdapterOutcomeAsync"/> (ADR-0010).
/// </summary>
internal sealed class SignalClassifier(
    ClassifierClient client,
    ConsensusBook consensus,
    IndicatorRegistry registry,
    IQueryHandler<GetObservationSeries, ObservationSeries> series,
    AdapterHistory adapterHistory)
{
    /// <summary>Prior prints examined per required macro surprise, so gaps in consensus coverage still leave a window.</summary>
    private const int MacroLookback = 4;

    /// <summary>Points read for the same-date check: one per variant of that date (a macro series spans variants).</summary>
    private const int SameDayPoints = 4;

    /// <summary>
    /// The local outcome of an adapter-recorded signal (reserved source prefix, provider not <c>api</c>), or null when it
    /// goes to the classifier. In order: observed before <c>Classification:AdapterHistoryFrom</c> (default 2011-01-01);
    /// market data outside the indicator registry (context series such as VIX9D, which the classifier does not rank);
    /// a series that already has an earlier-recorded observation on the same New York date (e.g. <c>fred:VIXCLS</c>
    /// before <c>cboe:VIX</c>), so one close is neither assessed twice nor corroborates itself in a composite.
    /// API submissions keep their behaviour (SRS CLS-009). No classifier call is made.
    /// </summary>
    public async Task<ClassificationAttempt?> AdapterOutcomeAsync(SignalView signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.PayloadType != "STRUCTURED" || !SourcePrefixes.IsAdapterRecorded(signal.SourceIdentifier, signal.Provenance.Provider))
        {
            return null;
        }

        var date = MarketTime.NewYorkDate(signal.ObservedAt);
        var from = adapterHistory.From;
        if (date < from)
        {
            return Unavailable(UnavailableReason.OutsideClassifiedHistory,
                string.Create(CultureInfo.InvariantCulture, $"Recorded before {from:yyyy-MM-dd}; kept as reference history, not assessed (ADR-0010)."));
        }

        if (signal.Category == SourceCategory.MarketData && !registry.TryGetSymbol(signal.Instrument, out _))
        {
            return Unavailable(UnavailableReason.UnknownIndicator,
                $"'{signal.Instrument}' was recorded by a source adapter as a context series; the classifier ranks indicator-registry symbols only (ADR-0010).");
        }

        if (signal.Category == SourceCategory.Geopolitical)
        {
            return null;
        }

        // Rows recorded before this one, on its New York date. A macro series is its indicator (the variant also
        // names the reference period); other series are narrowed to the variant, as their reference windows are.
        var variant = signal.Category == SourceCategory.Macroeconomic ? null : signal.Variant;
        var sameDay = await series.HandleAsync(
            new GetObservationSeries(signal.Instrument, signal.Category, variant, MarketTime.AtNewYork(date.AddDays(1), TimeOnly.MinValue),
                SameDayPoints, signal.LedgerSequence - 1), cancellationToken).ConfigureAwait(false);
        var first = sameDay.Points.Where(point => MarketTime.NewYorkDate(point.ObservedAt) == date).MinBy(point => point.LedgerSequence);
        return first is null
            ? null
            : Unavailable(UnavailableReason.DuplicateObservation,
                string.Create(CultureInfo.InvariantCulture, $"Signal {first.SignalId} was recorded first for {date:yyyy-MM-dd} and is the series of record (ADR-0010)."));
    }

    /// <summary>Routes a signal to the classifier; adapter-recorded signals pass <see cref="AdapterOutcomeAsync"/> first.</summary>
    public async Task<ClassificationAttempt> ClassifyAsync(SignalView signal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.PayloadType == "UNSTRUCTURED")
        {
            return Unavailable(UnavailableReason.RouteNotImplemented,
                "Free text needs the language-model route (SRS CLS-003), which is not in v1; the signal is recorded and shown, not classified.");
        }

        using var payload = JsonDocument.Parse(signal.Payload);
        var root = payload.RootElement;

        // Categories the engine keeps no history for still send an (empty) reference window: the
        // engine owns all history (brief §8), so no request depends on the classifier's own state
        // or its bootstrap. The classifier then answers on the route itself (501 where not in v1).
        return signal.Category switch
        {
            SourceCategory.MarketData => await MarketDataAsync(signal, cancellationToken).ConfigureAwait(false),
            SourceCategory.Macroeconomic => await MacroeconomicAsync(signal, cancellationToken).ConfigureAwait(false),
            SourceCategory.Geopolitical => Geopolitical(root) is { } geopolitical
                ? await SendAsync(Request(signal, geopolitical), NoHistory, 0, null, cancellationToken).ConfigureAwait(false)
                : Unavailable(UnavailableReason.ClassifierRejected, "The event has no severity estimate to classify; none is invented."),
            _ => signal.Value is { } price
                ? await SendAsync(Request(signal, new JsonObject
                {
                    ["basket_prices"] = new JsonObject { [signal.Instrument] = price },
                    ["timestamp"] = Iso(signal.ObservedAt),
                }), NoHistory, 0, null, cancellationToken).ConfigureAwait(false)
                : Unavailable(UnavailableReason.ClassifierRejected, "The flow has no numeric value to classify; none is invented."),
        };
    }

    private async Task<ClassificationAttempt> MarketDataAsync(SignalView signal, CancellationToken cancellationToken)
    {
        var length = registry.TryGetSymbol(signal.Instrument, out var symbol) ? symbol.IndicatorClass.ReferenceWindowLength : 0;

        // The same metric of the same instrument only: a PRICE or SKEW row for VIX is not VIX implied volatility.
        var history = await series.HandleAsync(
            new GetObservationSeries(signal.Instrument, SourceCategory.MarketData, signal.Variant, signal.ObservedAt, length), cancellationToken).ConfigureAwait(false);
        var values = history.Points.Select(point => point.Value).ToArray();
        var lastUpdate = history.LastObservedAt;
        var request = Request(signal, new JsonObject
        {
            ["symbol"] = signal.Instrument,
            ["current_value"] = signal.Value,
            ["timestamp"] = Iso(signal.ObservedAt),
        });
        var attempt = await SendAsync(request, Window(values, lastUpdate), values.Length, lastUpdate, cancellationToken).ConfigureAwait(false);
        return length > 0 ? RequireWindowUsed(attempt, values.Length, length) : attempt;
    }

    /// <summary>
    /// A registered market-data answer reports <c>history_sufficiency = min(1, sent / N_L)</c>;
    /// any other value means the classifier ranked against something other than the window the
    /// engine sent (for example a classifier without the reference-window change), which would
    /// be look-ahead. Such an answer is invalid (SRS CLS-004).
    /// </summary>
    private static ClassificationAttempt RequireWindowUsed(ClassificationAttempt attempt, int sent, int length)
    {
        if (attempt.Outcome.Answer is not { } answer || answer.Flags.Contains("unknown_indicator") || answer.Flags.Contains(ClassifierAnswer.InsufficientHistory))
        {
            return attempt;
        }

        var expected = Math.Round(Math.Min(1.0, sent / (double)length), 4);
        return answer.HistorySufficiency is { } reported && Math.Abs(reported - expected) < 0.00005
            ? attempt
            : attempt with
            {
                Outcome = ClassifierOutcome.Unavailable(UnavailableReason.InvalidResponse, 200, string.Create(CultureInfo.InvariantCulture,
                    $"history_sufficiency {answer.HistorySufficiency} does not match the {sent}-value reference window sent (expected {expected}); the classifier did not use the engine's window")),
            };
    }

    private async Task<ClassificationAttempt> MacroeconomicAsync(SignalView signal, CancellationToken cancellationToken)
    {
        if (signal.Value is not { } actual)
        {
            return Unavailable(UnavailableReason.RouteNotImplemented, "A print without a numeric value (e.g. a central bank statement) needs the language-model route (SRS CLS-003), which is not in v1.");
        }

        if (!registry.TryGetSymbol(signal.Instrument, out var symbol))
        {
            return Unavailable(UnavailableReason.UnknownIndicator,
                $"'{signal.Instrument}' is not in the indicator registry, so no consensus file can belong to it; name a registry symbol (e.g. econ:CPI_YOY) to classify the print.");
        }

        var release = MarketTime.NewYorkDate(signal.ObservedAt);
        var row = consensus.Find(signal.Instrument, release);
        if (row is null)
        {
            return Unavailable(UnavailableReason.AwaitingConsensus,
                $"No sourced consensus for {signal.Instrument} released {release:yyyy-MM-dd}; the print is not classified against an invented expectation.");
        }

        // Window: magnitudes of earlier surprises that also have sourced consensus rows, one per
        // release date, the first recorded print where sources overlap.
        var length = symbol.IndicatorClass.ReferenceWindowLength;
        var prior = await series.HandleAsync(
            new GetObservationSeries(signal.Instrument, SourceCategory.Macroeconomic, null, signal.ObservedAt, length * MacroLookback), cancellationToken).ConfigureAwait(false);
        var firsts = prior.Points
            .GroupBy(point => MarketTime.NewYorkDate(point.ObservedAt))
            .Select(group => group.MinBy(point => point.LedgerSequence)!)
            .OrderBy(point => point.ObservedAt)
            .ToArray();
        var surprises = firsts
            .Select(point => (point, expected: consensus.Find(signal.Instrument, MarketTime.NewYorkDate(point.ObservedAt))))
            .Where(pair => pair.expected is not null)
            .Select(pair => (pair.point, surprise: new MacroSurprise(
                MarketTime.NewYorkDate(pair.point.ObservedAt), pair.point.SignalId, pair.point.Value, pair.expected!.ToUsed(),
                Math.Abs(pair.point.Value - pair.expected.Consensus))))
            .TakeLast(length)
            .ToArray();
        var lastUpdate = surprises.Length == 0 ? (DateTimeOffset?)null : surprises[^1].point.ObservedAt;
        var request = Request(signal, new JsonObject
        {
            ["indicator"] = signal.Instrument,
            ["actual"] = actual,
            ["expected"] = row.Consensus,
            ["release_timestamp"] = Iso(signal.ObservedAt),
        });
        var attempt = await SendAsync(request, Window(surprises.Select(pair => pair.surprise.Magnitude).ToArray(), lastUpdate),
            surprises.Length, lastUpdate, cancellationToken, row.ToUsed()).ConfigureAwait(false);
        return attempt with { MacroWindow = surprises.Select(pair => pair.surprise).ToArray() };
    }

    private async Task<ClassificationAttempt> SendAsync(
        JsonObject request,
        JsonObject window,
        int windowLength,
        DateTimeOffset? lastUpdate,
        CancellationToken cancellationToken,
        ConsensusUsed? consensusUsed = null)
    {
        request["reference_window"] = window;
        var outcome = await client.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
        return new ClassificationAttempt(outcome, windowLength, lastUpdate, consensusUsed);
    }

    private static ClassificationAttempt Unavailable(UnavailableReason reason, string detail) =>
        new(ClassifierOutcome.Unavailable(reason, null, detail), 0, null, null);

    private static JsonObject Request(SignalView signal, JsonObject structured) => new()
    {
        ["source_category"] = signal.Category.ToWireName(),
        ["payload_type"] = "STRUCTURED",
        ["structured_payload"] = structured,
    };

    private static JsonObject NoHistory => Window([], null);

    private static JsonObject Window(double[] values, DateTimeOffset? lastUpdate) => new()
    {
        ["values"] = new JsonArray(values.Select(value => (JsonNode)JsonValue.Create(value)).ToArray()),
        ["last_update"] = lastUpdate is { } time ? Iso(time) : null,
    };

    /// <summary>The classifier's geopolitical payload; null when the severity estimate is missing.</summary>
    private static JsonObject? Geopolitical(JsonElement payload) =>
        payload.TryGetProperty("severity_estimate", out var severity) && severity.TryGetDouble(out var value)
            ? new JsonObject
            {
                ["event_type"] = Text(payload, "event_type"),
                ["region"] = Text(payload, "event_region"),
                ["severity_estimate"] = value,
            }
            : null;

    private static string? Text(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Iso(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
}
