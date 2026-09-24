using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PrimeScore.Ledger.Storage;

/// <summary>
/// Schema owner for the ledger, the pipeline log projection and verification results. Reads
/// and writes use parameterized SQL in <see cref="SqliteLedger"/>; this context defines the
/// tables and their migrations only.
/// </summary>
internal sealed class LedgerDbContext(DbContextOptions<LedgerDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_ledger";

    public DbSet<LedgerRow> Entries => Set<LedgerRow>();

    public DbSet<PipelineLogRow> PipelineLog => Set<PipelineLogRow>();

    public DbSet<VerificationRow> Verifications => Set<VerificationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LedgerRow>(entry =>
        {
            entry.ToTable("ledger");
            entry.HasKey(row => row.Sequence);
            entry.Property(row => row.Sequence).HasColumnName("sequence").ValueGeneratedNever();
            entry.Property(row => row.RecordedAt).HasColumnName("recorded_at").IsRequired();
            entry.Property(row => row.Kind).HasColumnName("kind").IsRequired();
            entry.Property(row => row.EntityId).HasColumnName("entity_id").IsRequired();
            entry.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            entry.Property(row => row.ConfigVersion).HasColumnName("config_version");
            entry.Property(row => row.Summary).HasColumnName("summary").IsRequired();
            entry.Property(row => row.Payload).HasColumnName("payload").IsRequired();
            entry.Property(row => row.PrevHash).HasColumnName("prev_hash").IsRequired();
            entry.Property(row => row.Hash).HasColumnName("hash").IsRequired();
            entry.HasIndex(row => row.Kind).HasDatabaseName("ix_ledger_kind");
            entry.HasIndex(row => row.EntityId).HasDatabaseName("ix_ledger_entity_id");
            entry.HasIndex(row => row.CorrelationId).HasDatabaseName("ix_ledger_correlation_id");
        });

        modelBuilder.Entity<PipelineLogRow>(log =>
        {
            log.ToTable("obs_log");
            log.HasKey(row => row.Id);
            log.Property(row => row.Id).HasColumnName("id");
            log.Property(row => row.Sequence).HasColumnName("sequence");
            log.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            log.Property(row => row.Stage).HasColumnName("stage").IsRequired();
            log.Property(row => row.Timestamp).HasColumnName("timestamp").IsRequired();
            log.Property(row => row.Message).HasColumnName("message").IsRequired();
            log.Property(row => row.Kind).HasColumnName("kind").IsRequired();
            log.Property(row => row.EntityId).HasColumnName("entity_id").IsRequired();
            log.Property(row => row.ConfigVersion).HasColumnName("config_version");
            log.HasIndex(row => row.CorrelationId).HasDatabaseName("ix_obs_log_correlation_id");
        });

        modelBuilder.Entity<VerificationRow>(verification =>
        {
            verification.ToTable("ledger_verifications");
            verification.HasKey(row => row.Id);
            verification.Property(row => row.Id).HasColumnName("id");
            verification.Property(row => row.VerifiedAt).HasColumnName("verified_at").IsRequired();
            verification.Property(row => row.Ok).HasColumnName("ok");
            verification.Property(row => row.EntriesChecked).HasColumnName("entries_checked");
            verification.Property(row => row.HeadSequence).HasColumnName("head_sequence");
            verification.Property(row => row.HeadHash).HasColumnName("head_hash").IsRequired();
            verification.Property(row => row.FirstInvalidSequence).HasColumnName("first_invalid_sequence");
            verification.Property(row => row.Reason).HasColumnName("reason");
        });
    }
}

internal sealed class LedgerRow
{
    public long Sequence { get; set; }

    public string RecordedAt { get; set; } = "";

    public string Kind { get; set; } = "";

    public string EntityId { get; set; } = "";

    public string CorrelationId { get; set; } = "";

    public int ConfigVersion { get; set; }

    public string Summary { get; set; } = "";

    public string Payload { get; set; } = "";

    public string PrevHash { get; set; } = "";

    public string Hash { get; set; } = "";
}

internal sealed class PipelineLogRow
{
    public long Id { get; set; }

    public long Sequence { get; set; }

    public string CorrelationId { get; set; } = "";

    public string Stage { get; set; } = "";

    public string Timestamp { get; set; } = "";

    public string Message { get; set; } = "";

    public string Kind { get; set; } = "";

    public string EntityId { get; set; } = "";

    public int ConfigVersion { get; set; }
}

internal sealed class VerificationRow
{
    public long Id { get; set; }

    public string VerifiedAt { get; set; } = "";

    public bool Ok { get; set; }

    public long EntriesChecked { get; set; }

    public long HeadSequence { get; set; }

    public string HeadHash { get; set; } = "";

    public long? FirstInvalidSequence { get; set; }

    public string? Reason { get; set; }
}

/// <summary>Used by the EF migrations tool only.</summary>
internal sealed class LedgerDesignTimeFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args) =>
        new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
            .ContextOptions<LedgerDbContext>(LedgerDbContext.HistoryTable));
}
