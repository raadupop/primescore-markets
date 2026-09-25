using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics.Contracts;

namespace PrimeScore.Modules.Analytics.Storage;

internal sealed class ReplayDbContext(DbContextOptions<ReplayDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__analytics_migrations";

    public DbSet<ReplayRow> Replays => Set<ReplayRow>();

    public static ReplayDbContext Create(DbContextOptions<ReplayDbContext> options) => new(options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReplayRow>(replay =>
        {
            replay.ToTable("ana_replays");
            replay.HasKey(row => row.ReplayId);
            replay.Property(row => row.ReplayId).HasColumnName("replay_id");
            replay.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            replay.Property(row => row.Payload).HasColumnName("payload").IsRequired();
            replay.HasIndex(row => row.Sequence).IsUnique();
        });
    }
}

internal sealed class ReplayRow
{
    public string ReplayId { get; set; } = "";

    public long Sequence { get; set; }

    public string Payload { get; set; } = "{}";
}

internal sealed class ReplayProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind == LedgerKinds.ReplayRun;

    public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        var view = record.PayloadAs<ReplayView>() with { LedgerSequence = record.Sequence, RecordedAt = record.RecordedAt };
        scope.Context<ReplayDbContext>(ReplayDbContext.HistoryTable, ReplayDbContext.Create).Replays.Add(new ReplayRow
        {
            ReplayId = record.EntityId.ToString("D"),
            Sequence = record.Sequence,
            Payload = SharedKernel.Json.CanonicalJson.Serialize(view),
        });
        return Task.CompletedTask;
    }
}

internal sealed class ReplaySchema(EngineDatabase database) : IEngineSchema
{
    public string Name => "analytics";

    public int Order => 400;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = new ReplayDbContext(database.ContextOptions<ReplayDbContext>(ReplayDbContext.HistoryTable));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class ReplayReadStore(EngineDatabase database)
{
    public ReplayDbContext Open()
    {
        var context = new ReplayDbContext(database.ContextOptions<ReplayDbContext>(ReplayDbContext.HistoryTable));
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }
}

internal sealed class ReplayDesignTimeFactory : IDesignTimeDbContextFactory<ReplayDbContext>
{
    public ReplayDbContext CreateDbContext(string[] args) => new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
        .ContextOptions<ReplayDbContext>(ReplayDbContext.HistoryTable));
}
