using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PrimeScore.Ledger;

/// <summary>
/// The open connection and transaction of one append. A projection obtains its module's
/// context here; changes are saved after each entry and committed with the ledger rows.
/// </summary>
public sealed class ProjectionScope : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly Dictionary<Type, DbContext> _contexts = [];

    internal ProjectionScope(SqliteConnection connection, SqliteTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
    }

    /// <summary>The module's context, created once per append on the shared transaction.</summary>
    public TContext Context<TContext>(string migrationsHistoryTable, Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(create);
        if (_contexts.TryGetValue(typeof(TContext), out var existing))
        {
            return (TContext)existing;
        }

        var context = create(EngineDatabase.ContextOptions<TContext>(_connection, migrationsHistoryTable));
        context.Database.UseTransaction(_transaction);
        _contexts[typeof(TContext)] = context;
        return context;
    }

    internal async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var context in _contexts.Values)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var context in _contexts.Values)
        {
            await context.DisposeAsync().ConfigureAwait(false);
        }

        _contexts.Clear();
    }
}
