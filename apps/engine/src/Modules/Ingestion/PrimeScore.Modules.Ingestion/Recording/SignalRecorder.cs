using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Storage;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Ingestion.Recording;

/// <summary>A signal that passed validation (or was built by a source adapter), ready to record.</summary>
/// <param name="Variant">Separates series that share an instrument: metric type, indicator and reference period, event type.</param>
internal sealed record SignalCandidate(
    SourceCategory Category,
    string SourceIdentifier,
    string Instrument,
    string Variant,
    DateTimeOffset ObservedAt,
    string PayloadType,
    double? Value,
    JsonElement Payload,
    SignalProvenance Provenance);

/// <summary>A submission that failed validation, recorded as a structured error record.</summary>
/// <param name="Raw">The submission as received.</param>
internal sealed record SignalRejection(string? SourceIdentifier, IReadOnlyList<string> Errors, string Raw);

internal enum RecordStatus
{
    Recorded,

    /// <summary>The same key with an identical payload was already recorded; nothing was written.</summary>
    Duplicate,

    /// <summary>The same key was already recorded with a different payload; the first stays (a conflict for API callers, a revision for adapters).</summary>
    Revised,

    Rejected,
}

internal sealed record RecordOutcome(Guid Id, RecordStatus Status, IReadOnlyList<string> Errors);

/// <summary>
/// The one write path for signals from the API and from source adapters. Ingestion is
/// serialized here so the idempotency check and the append cannot interleave. The key is
/// (source identifier, instrument, variant, observation time); a recorded key is never overwritten.
/// </summary>
internal sealed class SignalRecorder(ILedger ledger, IngestionReadStore reads) : IDisposable
{
    private const int AppendBatch = 500;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<RecordOutcome>> RecordAsync(IReadOnlyList<object> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = await ExistingKeysAsync(items.OfType<SignalCandidate>().ToArray(), cancellationToken).ConfigureAwait(false);
            var outcomes = new RecordOutcome[items.Count];
            var appends = new List<LedgerAppend>();
            for (var index = 0; index < items.Count; index++)
            {
                outcomes[index] = items[index] switch
                {
                    SignalCandidate candidate => Candidate(candidate, existing, appends),
                    SignalRejection rejection => Rejection(rejection, appends),
                    _ => throw new ArgumentException($"Unsupported item {items[index]?.GetType().Name}.", nameof(items)),
                };
            }

            foreach (var chunk in appends.Chunk(AppendBatch))
            {
                await ledger.AppendAsync(chunk, cancellationToken).ConfigureAwait(false);
            }

            return outcomes;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static RecordOutcome Candidate(SignalCandidate candidate, Dictionary<string, Recorded> existing, List<LedgerAppend> appends)
    {
        string payload;
        LedgerAppend append;
        var signalId = Guid.NewGuid();
        try
        {
            payload = CanonicalJson.Canonicalize(candidate.Payload.GetRawText());
            append = LedgerAppend.Create(
                LedgerKinds.SignalIngested, signalId, CorrelationId.New(), ConfigVersion.None, Summary(candidate),
                new SignalIngestedPayload(
                    signalId, candidate.Category, candidate.SourceIdentifier, candidate.Instrument, candidate.Variant,
                    candidate.ObservedAt.ToUniversalTime(), candidate.PayloadType, candidate.Value, candidate.Payload, candidate.Provenance));
        }
        catch (JsonException exception)
        {
            // Defensive: a payload the ledger cannot store canonically is rejected, not the whole batch.
            return Rejection(new SignalRejection(candidate.SourceIdentifier, [$"signal: cannot be stored ({exception.Message})"], candidate.Payload.GetRawText()), appends);
        }

        var key = Key(candidate.SourceIdentifier, candidate.Instrument, candidate.Variant, candidate.ObservedAt.ToUnixTimeMilliseconds());
        if (existing.TryGetValue(key, out var recorded))
        {
            return string.Equals(recorded.Payload, payload, StringComparison.Ordinal)
                ? new RecordOutcome(recorded.Id, RecordStatus.Duplicate, [])
                : new RecordOutcome(recorded.Id, RecordStatus.Revised,
                    [$"signal: conflicts with recorded signal {recorded.Id}, which has a different payload for the same source, instrument and time"]);
        }

        existing[key] = new Recorded(signalId, payload);
        appends.Add(append);
        return new RecordOutcome(signalId, RecordStatus.Recorded, []);
    }

    private static RecordOutcome Rejection(SignalRejection rejection, List<LedgerAppend> appends)
    {
        var rejectionId = Guid.NewGuid();
        appends.Add(LedgerAppend.Create(
            LedgerKinds.SignalRejected, rejectionId, CorrelationId.New(), ConfigVersion.None,
            $"Rejected signal from {Printable(rejection.SourceIdentifier ?? "unknown source")}: {rejection.Errors.Count} validation error(s)",
            new SignalRejectedPayload(rejectionId, rejection.SourceIdentifier, rejection.Errors, rejection.Raw)));
        return new RecordOutcome(rejectionId, RecordStatus.Rejected, rejection.Errors);
    }

    private async Task<Dictionary<string, Recorded>> ExistingKeysAsync(IReadOnlyList<SignalCandidate> candidates, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, Recorded>(StringComparer.Ordinal);
        await using var context = reads.Open();
        foreach (var group in candidates.GroupBy(candidate => (candidate.SourceIdentifier, candidate.Instrument, candidate.Variant)))
        {
            var times = group.Select(candidate => candidate.ObservedAt.ToUnixTimeMilliseconds()).ToArray();
            var (from, to) = (times.Min(), times.Max());
            var rows = await context.Signals
                .Where(row => row.SourceIdentifier == group.Key.SourceIdentifier && row.Instrument == group.Key.Instrument
                    && row.Variant == group.Key.Variant && row.ObservedAtMs >= from && row.ObservedAtMs <= to)
                .Select(row => new { row.SignalId, row.ObservedAtMs, row.Payload })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                found[Key(group.Key.SourceIdentifier, group.Key.Instrument, group.Key.Variant, row.ObservedAtMs)] =
                    new Recorded(Guid.Parse(row.SignalId), row.Payload);
            }
        }

        return found;
    }

    private static string Key(string source, string instrument, string variant, long observedAtMs) =>
        string.Create(CultureInfo.InvariantCulture, $"{source}\u001F{instrument}\u001F{variant}\u001F{observedAtMs}");

    private static string Summary(SignalCandidate candidate)
    {
        var value = candidate.Value is { } number ? number.ToString("G", CultureInfo.InvariantCulture) : "no value";
        return Printable($"{candidate.Category.ToWireName()} {candidate.Instrument} = {value} observed "
            + $"{candidate.ObservedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC ({candidate.SourceIdentifier})");
    }

    /// <summary>Ledger summaries may not contain control characters (hash field separator).</summary>
    private static string Printable(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }

    private sealed record Recorded(Guid Id, string Payload);
}
