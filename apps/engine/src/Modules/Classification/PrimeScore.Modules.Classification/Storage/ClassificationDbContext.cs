using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;

namespace PrimeScore.Modules.Classification.Storage;

/// <summary>
/// Classification's read tables, each a projection of ledger entries that carries its ledger
/// sequence: one row per assessment outcome (recorded or unavailable), per composite and per
/// dislocation.
/// </summary>
internal sealed class ClassificationDbContext(DbContextOptions<ClassificationDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_classification";

    public DbSet<AssessmentRow> Assessments => Set<AssessmentRow>();

    public DbSet<CompositeRow> Composites => Set<CompositeRow>();

    public DbSet<DislocationRow> Dislocations => Set<DislocationRow>();

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

        modelBuilder.Entity<CompositeRow>(composite =>
        {
            composite.ToTable("cls_composites");
            composite.HasKey(row => row.CompositeId);
            composite.Property(row => row.CompositeId).HasColumnName("composite_id");
            composite.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            composite.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            composite.Property(row => row.ConfigVersion).HasColumnName("config_version");
            composite.Property(row => row.Context).HasColumnName("context").IsRequired();
            composite.Property(row => row.TriggerAssessmentId).HasColumnName("trigger_assessment_id").IsRequired();
            composite.Property(row => row.TriggerSignalId).HasColumnName("trigger_signal_id").IsRequired();
            composite.Property(row => row.AsOfMs).HasColumnName("as_of_ms");
            composite.Property(row => row.ComputedAtMs).HasColumnName("computed_at_ms");
            composite.Property(row => row.Score).HasColumnName("score");
            composite.Property(row => row.WeightingSchemeId).HasColumnName("weighting_scheme_id").IsRequired();
            composite.Property(row => row.Aggregation).HasColumnName("aggregation").IsRequired();
            composite.Property(row => row.Contributing).HasColumnName("contributing").IsRequired();
            composite.Property(row => row.Absent).HasColumnName("absent").IsRequired();
            composite.HasIndex(row => new { row.Context, row.AsOfMs, row.Sequence }).HasDatabaseName("ix_cls_composites_context_time");
            composite.HasIndex(row => new { row.Context, row.TriggerAssessmentId }).HasDatabaseName("ix_cls_composites_trigger");
        });

        modelBuilder.Entity<DislocationRow>(dislocation =>
        {
            dislocation.ToTable("cls_dislocations");
            dislocation.HasKey(row => row.DislocationId);
            dislocation.Property(row => row.DislocationId).HasColumnName("dislocation_id");
            dislocation.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            dislocation.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            dislocation.Property(row => row.ConfigVersion).HasColumnName("config_version");
            dislocation.Property(row => row.Context).HasColumnName("context").IsRequired();
            dislocation.Property(row => row.CompositeId).HasColumnName("composite_id").IsRequired();
            dislocation.Property(row => row.AsOfMs).HasColumnName("as_of_ms");
            dislocation.Property(row => row.ComputedAtMs).HasColumnName("computed_at_ms");
            dislocation.Property(row => row.CompositeScore).HasColumnName("composite_score");
            dislocation.Property(row => row.ReferenceInstrument).HasColumnName("reference_instrument").IsRequired();
            dislocation.Property(row => row.MarketObservedIv).HasColumnName("market_observed_iv");
            dislocation.Property(row => row.IvObservedAtMs).HasColumnName("iv_observed_at_ms");
            dislocation.Property(row => row.IvSignalId).HasColumnName("iv_signal_id").IsRequired();
            dislocation.Property(row => row.Regime).HasColumnName("regime").IsRequired();
            dislocation.Property(row => row.RegimePercentile).HasColumnName("regime_percentile");
            dislocation.Property(row => row.RegimeHistory).HasColumnName("regime_history");
            dislocation.Property(row => row.SensitivityFactor).HasColumnName("sensitivity_factor");
            dislocation.Property(row => row.SignalImpliedIv).HasColumnName("signal_implied_iv");
            dislocation.Property(row => row.DislocationValue).HasColumnName("dislocation_value");
            dislocation.Property(row => row.Threshold).HasColumnName("threshold");
            dislocation.Property(row => row.ThresholdBreached).HasColumnName("threshold_breached");
            dislocation.HasIndex(row => new { row.Context, row.AsOfMs, row.Sequence }).HasDatabaseName("ix_cls_dislocations_context_time");
            dislocation.HasIndex(row => row.CompositeId).HasDatabaseName("ix_cls_dislocations_composite");
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
