using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.Modules.Decision.Storage;

/// <summary>Ledger payload of <see cref="LedgerKinds.DecisionMade"/> (SRS DEC-001 to DEC-003, AUD-001).</summary>
/// <param name="DislocationSequence">Ledger sequence of the dislocation decided on: the decision stage's cursor.</param>
/// <param name="ConditionsConfigured">The deploy conditions as configured (name, operator, threshold) when the decision was made.</param>
internal sealed record DecisionMadePayload(
    Guid DecisionId,
    string Context,
    DecisionOutcome Outcome,
    string Scenario,
    Guid CompositeId,
    double CompositeScore,
    IReadOnlyList<string> ContributingCategories,
    Guid DislocationId,
    long DislocationSequence,
    double DislocationValue,
    double DislocationThreshold,
    string ReferenceInstrument,
    double MarketObservedIv,
    double SignalImpliedIv,
    Guid TriggerSignalId,
    DateTimeOffset AsOf,
    IReadOnlyList<Configuration.Contracts.DeployCondition> ConditionsConfigured,
    IReadOnlyList<ConditionEvaluation> Conditions,
    IReadOnlyList<DecisionSignal> TopContributing,
    IReadOnlyList<DecisionSignal> Dissenting,
    string Explanation);

/// <summary>Decision's read table: one row per decision, projected from <c>DecisionMade</c>.</summary>
internal sealed class DecisionDbContext(DbContextOptions<DecisionDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_decision";

    public DbSet<DecisionRow> Decisions => Set<DecisionRow>();

    public static DecisionDbContext Create(DbContextOptions<DecisionDbContext> options) => new(options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DecisionRow>(decision =>
        {
            decision.ToTable("dec_decisions");
            decision.HasKey(row => row.DecisionId);
            decision.Property(row => row.DecisionId).HasColumnName("decision_id");
            decision.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            decision.Property(row => row.CorrelationId).HasColumnName("correlation_id").IsRequired();
            decision.Property(row => row.ConfigVersion).HasColumnName("config_version");
            decision.Property(row => row.Context).HasColumnName("context").IsRequired();
            decision.Property(row => row.Outcome).HasColumnName("outcome").IsRequired();
            decision.Property(row => row.Scenario).HasColumnName("scenario").IsRequired();
            decision.Property(row => row.CompositeId).HasColumnName("composite_id").IsRequired();
            decision.Property(row => row.CompositeScore).HasColumnName("composite_score");
            decision.Property(row => row.DislocationId).HasColumnName("dislocation_id").IsRequired();
            decision.Property(row => row.DislocationSequence).HasColumnName("dislocation_sequence");
            decision.Property(row => row.DislocationValue).HasColumnName("dislocation_value");
            decision.Property(row => row.DislocationThreshold).HasColumnName("dislocation_threshold");
            decision.Property(row => row.ReferenceInstrument).HasColumnName("reference_instrument").IsRequired();
            decision.Property(row => row.MarketObservedIv).HasColumnName("market_observed_iv");
            decision.Property(row => row.SignalImpliedIv).HasColumnName("signal_implied_iv");
            decision.Property(row => row.TriggerSignalId).HasColumnName("trigger_signal_id").IsRequired();
            decision.Property(row => row.AsOfMs).HasColumnName("as_of_ms");
            decision.Property(row => row.RecordedAtMs).HasColumnName("recorded_at_ms");
            decision.Property(row => row.Payload).HasColumnName("payload").IsRequired();
            decision.HasIndex(row => row.DislocationSequence).IsUnique().HasDatabaseName("ux_dec_decisions_dislocation");
            decision.HasIndex(row => new { row.Context, row.AsOfMs, row.Sequence }).HasDatabaseName("ix_dec_decisions_context_time");
            decision.HasIndex(row => row.AsOfMs).HasDatabaseName("ix_dec_decisions_time");
            decision.HasIndex(row => row.CorrelationId).HasDatabaseName("ix_dec_decisions_correlation");
            decision.HasIndex(row => row.Sequence).IsUnique().HasDatabaseName("ux_dec_decisions_sequence");
        });
    }
}

internal sealed class DecisionRow
{
    public string DecisionId { get; set; } = "";

    public long Sequence { get; set; }

    public string CorrelationId { get; set; } = "";

    public int ConfigVersion { get; set; }

    public string Context { get; set; } = "";

    public string Outcome { get; set; } = "";

    public string Scenario { get; set; } = "";

    public string CompositeId { get; set; } = "";

    public double CompositeScore { get; set; }

    public string DislocationId { get; set; } = "";

    public long DislocationSequence { get; set; }

    public double DislocationValue { get; set; }

    public double DislocationThreshold { get; set; }

    public string ReferenceInstrument { get; set; } = "";

    public double MarketObservedIv { get; set; }

    public double SignalImpliedIv { get; set; }

    public string TriggerSignalId { get; set; } = "";

    public long AsOfMs { get; set; }

    public long RecordedAtMs { get; set; }

    /// <summary>The whole <see cref="DecisionMadePayload"/> as canonical JSON (conditions, signals, explanation).</summary>
    public string Payload { get; set; } = "{}";
}

/// <summary>Maintains <c>dec_decisions</c> inside each append's transaction.</summary>
internal sealed class DecisionProjection : ILedgerProjection
{
    public bool Handles(string kind) => kind == LedgerKinds.DecisionMade;

    public Task ProjectAsync(LedgerRecord record, ProjectionScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(scope);
        var payload = record.PayloadAs<DecisionMadePayload>();
        scope.Context<DecisionDbContext>(DecisionDbContext.HistoryTable, DecisionDbContext.Create).Decisions.Add(new DecisionRow
        {
            DecisionId = payload.DecisionId.ToString("D"),
            Sequence = record.Sequence,
            CorrelationId = record.CorrelationId.ToString(),
            ConfigVersion = record.ConfigVersion.Value,
            Context = payload.Context,
            Outcome = payload.Outcome.ToString(),
            Scenario = payload.Scenario,
            CompositeId = payload.CompositeId.ToString("D"),
            CompositeScore = payload.CompositeScore,
            DislocationId = payload.DislocationId.ToString("D"),
            DislocationSequence = payload.DislocationSequence,
            DislocationValue = payload.DislocationValue,
            DislocationThreshold = payload.DislocationThreshold,
            ReferenceInstrument = payload.ReferenceInstrument,
            MarketObservedIv = payload.MarketObservedIv,
            SignalImpliedIv = payload.SignalImpliedIv,
            TriggerSignalId = payload.TriggerSignalId.ToString("D"),
            AsOfMs = payload.AsOf.ToUnixTimeMilliseconds(),
            RecordedAtMs = record.RecordedAt.ToUnixTimeMilliseconds(),
            Payload = CanonicalJson.Serialize(payload),
        });
        return Task.CompletedTask;
    }
}

internal sealed class DecisionSchema(EngineDatabase database) : IEngineSchema
{
    public string Name => "decision";

    /// <summary>After classification (200), whose dislocations it decides on.</summary>
    public int Order => 300;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = new DecisionDbContext(database.ContextOptions<DecisionDbContext>(DecisionDbContext.HistoryTable));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Opens read-only views of the decision table (no change tracking).</summary>
internal sealed class DecisionReadStore(EngineDatabase database)
{
    public DecisionDbContext Open()
    {
        var context = new DecisionDbContext(database.ContextOptions<DecisionDbContext>(DecisionDbContext.HistoryTable));
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }
}

/// <summary>Used by the EF migrations tool only.</summary>
internal sealed class DecisionDesignTimeFactory : IDesignTimeDbContextFactory<DecisionDbContext>
{
    public DecisionDbContext CreateDbContext(string[] args) =>
        new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
            .ContextOptions<DecisionDbContext>(DecisionDbContext.HistoryTable));
}
