using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Recording;
using PrimeScore.Modules.Ingestion.Sources;
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

/// <summary>Queues an on-demand pull of a registered adapter; the scheduler runs it in the background.</summary>
internal sealed class RequestSourcePullHandler(IEnumerable<ISourceAdapter> adapters, SourceRuntime runtime)
    : ICommandHandler<RequestSourcePull, SourcePullAck>
{
    public Task<SourcePullAck> HandleAsync(RequestSourcePull command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var adapter = adapters.FirstOrDefault(candidate => string.Equals(candidate.Name, command.Source, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            return Task.FromResult(new SourcePullAck(false, $"Unknown source '{command.Source}'."));
        }

        if (adapter.DisabledReason is { } reason)
        {
            return Task.FromResult(new SourcePullAck(false, reason));
        }

        var state = runtime.For(adapter.Name);
        return Task.FromResult(state.TryRequest()
            ? new SourcePullAck(true, state.Running ? "A pull is running; another will follow it." : null)
            : new SourcePullAck(false, "Pull requests are already queued."));
    }
}
