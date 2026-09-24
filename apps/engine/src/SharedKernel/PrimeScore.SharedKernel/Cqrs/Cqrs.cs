namespace PrimeScore.SharedKernel.Cqrs;

/// <summary>
/// Marker for command results. Acknowledgements carry identifiers and status only, never
/// domain state (brief §5 rule 5); the structural suite checks their member types.
/// </summary>
public interface ICommandAck;

public interface ICommand<TAck>
    where TAck : ICommandAck;

public interface ICommandHandler<in TCommand, TAck>
    where TCommand : ICommand<TAck>
    where TAck : ICommandAck
{
    Task<TAck> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

/// <summary>A read. Query handlers never write (brief §5 rule 5).</summary>
public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}
