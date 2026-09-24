using Microsoft.Extensions.DependencyInjection;

namespace PrimeScore.SharedKernel.Messaging;

/// <summary>
/// A fact one module publishes for others. Event types live in the publishing module's
/// contracts assembly; handlers live in consuming modules (brief §5 rule 1).
/// </summary>
public interface IIntegrationEvent
{
    CorrelationId CorrelationId { get; }
}

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}

public interface IIntegrationEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}

/// <summary>
/// Synchronous, in-process dispatch in registration order. The pipeline runs one signal at a
/// time (brief §6), so every handler completes before the publisher returns.
/// </summary>
internal sealed class InProcessIntegrationEventPublisher(IServiceProvider services) : IIntegrationEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent
    {
        foreach (var handler in services.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
