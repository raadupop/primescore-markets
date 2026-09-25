using Microsoft.Extensions.Options;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.Modules.Ingestion.Sources.Fred;
using PrimeScore.Modules.Ingestion.Validation;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Ingestion.Features;

/// <summary>Validates each submitted signal, records accepted and rejected ones, and announces the batch.</summary>
internal sealed class IngestSignalsHandler(
    SignalValidator validator,
    SignalRecorder recorder,
    IIntegrationEventPublisher publisher) : ICommandHandler<IngestSignals, IngestSignalsAck>
{
    public async Task<IngestSignalsAck> HandleAsync(IngestSignals command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var items = command.Documents.Select(document => validator.Validate(document, command.SubmittedBy)).ToArray();
        var outcomes = await recorder.RecordAsync(items, cancellationToken).ConfigureAwait(false);
        var recorded = outcomes.Where(outcome => outcome.Status == RecordStatus.Recorded).Select(outcome => outcome.Id).ToArray();
        if (recorded.Length > 0)
        {
            await publisher.PublishAsync(new SignalBatchAccepted(CorrelationId.New(), recorded), cancellationToken).ConfigureAwait(false);
        }

        // Resubmitting an identical signal is accepted as the recorded one. A different payload for
        // a recorded key is a conflict: it is refused, never merged, and the first stays.
        return new IngestSignalsAck(outcomes
            .Select(outcome => new SignalIngestResult(
                outcome.Id,
                Accepted: outcome.Status is RecordStatus.Recorded or RecordStatus.Duplicate,
                Duplicate: outcome.Status == RecordStatus.Duplicate,
                outcome.Errors))
            .ToArray());
    }
}

/// <summary>Queues an on-demand pull; the scheduler runs it in the background.</summary>
internal sealed class RequestSourcePullHandler(SourcePullQueue queue, IOptions<FredOptions> options)
    : ICommandHandler<RequestSourcePull, SourcePullAck>
{
    public Task<SourcePullAck> HandleAsync(RequestSourcePull command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!string.Equals(command.Source, FredOptions.SourceName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SourcePullAck(false, $"Unknown source '{command.Source}'."));
        }

        if (options.Value.DisabledReason() is { } reason)
        {
            return Task.FromResult(new SourcePullAck(false, reason));
        }

        return Task.FromResult(queue.TryRequest(FredOptions.SourceName)
            ? new SourcePullAck(true, queue.Running ? "A pull is running; another will follow it." : null)
            : new SourcePullAck(false, "Pull requests are already queued."));
    }
}
