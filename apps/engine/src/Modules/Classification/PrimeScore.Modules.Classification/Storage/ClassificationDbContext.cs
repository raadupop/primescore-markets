using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;

namespace PrimeScore.Modules.Classification.Storage;

/// <summary>
/// Classification's read tables, each a projection of ledger entries that carries its ledger
/// sequence: one row per assessment outcome (recorded or unavailable).
/// </summary>
internal sealed class ClassificationDbContext(DbContextOptions<ClassificationDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_classification";

    public DbSet<AssessmentRow> Assessments => Set<AssessmentRow>();

    public static ClassificationDbContext Create(DbContextOptions<ClassificationDbContext> options) => new(options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssessmentRow>(assessment =>
        {
            assessment.ToTable("cls_assessments");
            assessment.HasKey(row => row.AssessmentId);
            assessment.Property(row => row.AssessmentId).HasColumnName("assessment_id");
            assessment.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            assessment.Property(row => row.SignalId).HasColumnName("signal_id").IsRequired();
            assessment.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            assessment.Property(row => row.Category).HasColumnName("category").IsRequired();
            assessment.Property(row => row.Instrument).HasColumnName("instrument").IsRequired();
            assessment.Property(row => row.Variant).HasColumnName("variant").IsRequired();
            assessment.Property(row => row.ObservedAtMs).HasColumnName("observed_at_ms");
            assessment.Property(row => row.AssessedAtMs).HasColumnName("assessed_at_ms");
            assessment.Property(row => row.Available).HasColumnName("available");
            assessment.Property(row => row.Reason).HasColumnName("reason");
            assessment.Property(row => row.Detail).HasColumnName("detail");
            assessment.Property(row => row.HttpStatus).HasColumnName("http_status");
            assessment.Property(row => row.Score).HasColumnName("score");
            assessment.Property(row => row.ScoreType).HasColumnName("score_type");
            assessment.Property(row => row.Certainty).HasColumnName("certainty");
            assessment.Property(row => row.HistorySufficiency).HasColumnName("history_sufficiency");
            assessment.Property(row => row.TemporalRelevance).HasColumnName("temporal_relevance");
            assessment.Property(row => row.ClassificationMethod).HasColumnName("classification_method");
            assessment.Property(row => row.EventTaxonomy).HasColumnName("event_taxonomy");
            assessment.Property(row => row.IsFallback).HasColumnName("is_fallback");
            assessment.Property(row => row.StalenessSeconds).HasColumnName("staleness_seconds");
            assessment.Property(row => row.FallbackOf).HasColumnName("fallback_of");
            assessment.Property(row => row.Flags).HasColumnName("flags").IsRequired();
            assessment.Property(row => row.ReasoningTrace).HasColumnName("reasoning_trace");
            assessment.Property(row => row.ComputedMetrics).HasColumnName("computed_metrics");
            assessment.Property(row => row.ReferenceWindowLength).HasColumnName("reference_window_length");
            assessment.Property(row => row.Consensus).HasColumnName("consensus");
            assessment.HasIndex(row => new { row.SignalId, row.Sequence }).HasDatabaseName("ix_cls_assessments_signal");
            assessment.HasIndex(row => new { row.Instrument, row.Category, row.Variant, row.ObservedAtMs }).HasDatabaseName("ix_cls_assessments_series_time");
            assessment.HasIndex(row => row.ObservedAtMs).HasDatabaseName("ix_cls_assessments_time");
        });
    }
}

internal sealed class AssessmentRow
{
    public string AssessmentId { get; set; } = "";

    public long Sequence { get; set; }

    public string SignalId { get; set; } = "";

    public string CorrelationId { get; set; } = "";

    public string Category { get; set; } = "";

    public string Instrument { get; set; } = "";

    public string Variant { get; set; } = "";

    public long ObservedAtMs { get; set; }

    public long AssessedAtMs { get; set; }

    public bool Available { get; set; }

    public string? Reason { get; set; }

    public string? Detail { get; set; }

    public int? HttpStatus { get; set; }

    public double? Score { get; set; }

    public string? ScoreType { get; set; }

    public double? Certainty { get; set; }

    public double? HistorySufficiency { get; set; }

    public double? TemporalRelevance { get; set; }

    public string? ClassificationMethod { get; set; }

    public string? EventTaxonomy { get; set; }

    public bool IsFallback { get; set; }

    public double? StalenessSeconds { get; set; }

    public string? FallbackOf { get; set; }

    public string Flags { get; set; } = "[]";

    public string? ReasoningTrace { get; set; }

    public string? ComputedMetrics { get; set; }

    public int ReferenceWindowLength { get; set; }

    public string? Consensus { get; set; }
}

internal sealed class ClassificationSchema(EngineDatabase database) : IEngineSchema
{
    public string Name => "classification";

    public int Order => 200;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = new ClassificationDbContext(database.ContextOptions<ClassificationDbContext>(ClassificationDbContext.HistoryTable));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Opens read-only views of classification tables (no change tracking).</summary>
internal sealed class ClassificationReadStore(EngineDatabase database)
{
    public ClassificationDbContext Open()
    {
        var context = new ClassificationDbContext(database.ContextOptions<ClassificationDbContext>(ClassificationDbContext.HistoryTable));
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }
}

/// <summary>Used by the EF migrations tool only.</summary>
internal sealed class ClassificationDesignTimeFactory : IDesignTimeDbContextFactory<ClassificationDbContext>
{
    public ClassificationDbContext CreateDbContext(string[] args) =>
        new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
            .ContextOptions<ClassificationDbContext>(ClassificationDbContext.HistoryTable));
}
