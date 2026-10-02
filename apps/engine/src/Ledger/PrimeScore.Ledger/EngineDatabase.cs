using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PrimeScore.Ledger;

/// <summary>
/// The engine's single SQLite file (WAL mode). The ledger and every module's read tables live
/// here; each module owns its tables through its own <see cref="DbContext"/> and migrations.
/// </summary>
public sealed class EngineDatabase
{
    public EngineDatabase(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        FullPath = Path.GetFullPath(fullPath);
        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    public string FullPath { get; }

    public string ConnectionString { get; }

    public SqliteConnection CreateConnection() => new(ConnectionString);

    /// <summary>
    /// Closes this file's idle pooled connections only. <see cref="SqliteConnection.ClearAllPools"/> also closes
    /// connections that other databases in the same process are opening (parallel test classes failed with a
    /// disposed sqlite3 handle).
    /// </summary>
    public void ClearPool()
    {
        using var connection = CreateConnection();
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>Options for a context that opens its own connection (reads, migrations).</summary>
    public DbContextOptions<TContext> ContextOptions<TContext>(string migrationsHistoryTable)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite(ConnectionString, sqlite => sqlite.MigrationsHistoryTable(migrationsHistoryTable))
            .Options;

    /// <summary>Options bound to an open connection, for projections inside an append.</summary>
    public static DbContextOptions<TContext> ContextOptions<TContext>(DbConnection connection, string migrationsHistoryTable)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connection, sqlite => sqlite.MigrationsHistoryTable(migrationsHistoryTable))
            .Options;
}

/// <summary>One schema owner (the ledger or a module). Applied in <see cref="Order"/> at startup.</summary>
public interface IEngineSchema
{
    string Name { get; }

    int Order { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

public static class EngineDatabaseInitializer
{
    /// <summary>Creates the database directory, enables WAL and applies every schema's migrations.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        var database = services.GetRequiredService<EngineDatabase>();
        var directory = Path.GetDirectoryName(database.FullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using (var connection = database.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL;";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var schema in services.GetServices<IEngineSchema>().OrderBy(schema => schema.Order))
        {
            await schema.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
