using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;

namespace PrimeScore.Modules.Ingestion.Storage;

/// <summary>
/// Ingestion's tables in the engine database: the signal and rejection projections (rebuilt
/// from ledger entries, each row carrying its ledger sequence) and the operational record of
/// source-adapter runs. Times are stored as Unix milliseconds so SQLite can range-query them.
/// </summary>
internal sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_ingestion";

    public DbSet<SignalRow> Signals => Set<SignalRow>();

    public DbSet<RejectionRow> Rejections => Set<RejectionRow>();

    public DbSet<SourceRunRow> SourceRuns => Set<SourceRunRow>();

    public static IngestionDbContext Create(DbContextOptions<IngestionDbContext> options) => new(options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SignalRow>(signal =>
        {
            signal.ToTable("ing_signals");
            signal.HasKey(row => row.SignalId);
            signal.Property(row => row.SignalId).HasColumnName("signal_id");
            signal.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            signal.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            signal.Property(row => row.Category).HasColumnName("category").IsRequired();
            signal.Property(row => row.SourceIdentifier).HasColumnName("source_identifier").IsRequired();
            signal.Property(row => row.Instrument).HasColumnName("instrument").IsRequired();
            signal.Property(row => row.Variant).HasColumnName("variant").IsRequired();
            signal.Property(row => row.Provider).HasColumnName("provider").IsRequired();
            signal.Property(row => row.ObservedAtMs).HasColumnName("observed_at_ms");
            signal.Property(row => row.RecordedAtMs).HasColumnName("recorded_at_ms");
            signal.Property(row => row.PayloadType).HasColumnName("payload_type").IsRequired();
            signal.Property(row => row.Value).HasColumnName("value");
            signal.Property(row => row.Payload).HasColumnName("payload").IsRequired();
            signal.Property(row => row.Provenance).HasColumnName("provenance").IsRequired();
            signal.HasIndex(row => new { row.SourceIdentifier, row.Instrument, row.Variant, row.ObservedAtMs })
                .IsUnique().HasDatabaseName("ux_ing_signals_key");
            signal.HasIndex(row => new { row.Instrument, row.Category, row.Variant, row.ObservedAtMs }).HasDatabaseName("ix_ing_signals_series_time");
            signal.HasIndex(row => new { row.Provider, row.SourceIdentifier, row.ObservedAtMs }).HasDatabaseName("ix_ing_signals_provider");
            signal.HasIndex(row => new { row.Category, row.ObservedAtMs }).HasDatabaseName("ix_ing_signals_category_time");
            signal.HasIndex(row => row.ObservedAtMs).HasDatabaseName("ix_ing_signals_time");
        });

        modelBuilder.Entity<RejectionRow>(rejection =>
        {
            rejection.ToTable("ing_rejections");
            rejection.HasKey(row => row.RejectionId);
            rejection.Property(row => row.RejectionId).HasColumnName("rejection_id");
            rejection.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            rejection.Property(row => row.RecordedAtMs).HasColumnName("recorded_at_ms");
            rejection.Property(row => row.SourceIdentifier).HasColumnName("source_identifier");
            rejection.Property(row => row.Errors).HasColumnName("errors").IsRequired();
            rejection.Property(row => row.Raw).HasColumnName("raw").IsRequired();
            rejection.HasIndex(row => row.Sequence).HasDatabaseName("ix_ing_rejections_sequence");
        });

        modelBuilder.Entity<SourceRunRow>(run =>
        {
            run.ToTable("ing_source_runs");
            run.HasKey(row => row.Id);
            run.Property(row => row.Id).HasColumnName("id");
            run.Property(row => row.Source).HasColumnName("source").IsRequired();
            run.Property(row => row.StartedAtMs).HasColumnName("started_at_ms");
            run.Property(row => row.FinishedAtMs).HasColumnName("finished_at_ms");
            run.Property(row => row.Succeeded).HasColumnName("succeeded");
            run.Property(row => row.Error).HasColumnName("error");
            run.Property(row => row.Accepted).HasColumnName("accepted");
            run.Property(row => row.Duplicates).HasColumnName("duplicates");
            run.Property(row => row.Revised).HasColumnName("revised");
            run.Property(row => row.Missing).HasColumnName("missing");
            run.Property(row => row.Rejected).HasColumnName("rejected");
            run.HasIndex(row => new { row.Source, row.StartedAtMs }).HasDatabaseName("ix_ing_source_runs_source_time");
        });
    }
}

internal sealed class SignalRow
{
    public string SignalId { get; set; } = "";

    public long Sequence { get; set; }

    public string CorrelationId { get; set; } = "";

    public string Category { get; set; } = "";

    public string SourceIdentifier { get; set; } = "";

    public string Instrument { get; set; } = "";

    /// <summary>Distinguishes series sharing an instrument: metric type, indicator and reference period, event type.</summary>
    public string Variant { get; set; } = "";

    /// <summary>Who produced the value (FRED, api, HUMAN_CURATED); adapters resume only from their own rows.</summary>
    public string Provider { get; set; } = "";

    public long ObservedAtMs { get; set; }

    public long RecordedAtMs { get; set; }

    public string PayloadType { get; set; } = "";

    public double? Value { get; set; }

    public string Payload { get; set; } = "";

    public string Provenance { get; set; } = "";
}

internal sealed class RejectionRow
{
    public string RejectionId { get; set; } = "";

    public long Sequence { get; set; }

    public long RecordedAtMs { get; set; }

    public string? SourceIdentifier { get; set; }

    public string Errors { get; set; } = "";

    public string Raw { get; set; } = "";
}

internal sealed class SourceRunRow
{
    public long Id { get; set; }

    public string Source { get; set; } = "";

    public long StartedAtMs { get; set; }

    public long? FinishedAtMs { get; set; }

    public bool Succeeded { get; set; }

    public string? Error { get; set; }

    public int Accepted { get; set; }

    public int Duplicates { get; set; }

    public int Revised { get; set; }

    public int Missing { get; set; }

    public int Rejected { get; set; }
}

internal sealed class IngestionSchema(EngineDatabase database) : IEngineSchema
{
    public string Name => "ingestion";

    public int Order => 100;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = new IngestionDbContext(database.ContextOptions<IngestionDbContext>(IngestionDbContext.HistoryTable));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Opens read-only views of ingestion tables (no change tracking).</summary>
internal sealed class IngestionReadStore(EngineDatabase database)
{
    public IngestionDbContext Open()
    {
        var context = new IngestionDbContext(database.ContextOptions<IngestionDbContext>(IngestionDbContext.HistoryTable));
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }
}

/// <summary>Used by the EF migrations tool only.</summary>
internal sealed class IngestionDesignTimeFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args) =>
        new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
            .ContextOptions<IngestionDbContext>(IngestionDbContext.HistoryTable));
}
