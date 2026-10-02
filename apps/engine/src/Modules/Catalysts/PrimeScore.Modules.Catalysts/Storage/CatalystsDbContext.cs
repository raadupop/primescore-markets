using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;

namespace PrimeScore.Modules.Catalysts.Storage;

/// <summary>
/// Catalysts' read table: <c>cat_events</c>, one row per vintage of each catalyst, never deleted.
/// The latest vintage carries <c>is_current</c>; earlier vintages keep the schedule history that
/// the never-rescheduled split and point-in-time questions need.
/// </summary>
internal sealed class CatalystsDbContext(DbContextOptions<CatalystsDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_catalysts";

    public DbSet<CatalystEventRow> Events => Set<CatalystEventRow>();

    public static CatalystsDbContext Create(DbContextOptions<CatalystsDbContext> options) => new(options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CatalystEventRow>(row =>
        {
            row.ToTable("cat_events");
            row.HasKey(e => new { e.CatalystId, e.Vintage });
            row.Property(e => e.CatalystId).HasColumnName("catalyst_id");
            row.Property(e => e.Vintage).HasColumnName("vintage");
            row.Property(e => e.Family).HasColumnName("family").IsRequired();
            row.Property(e => e.Title).HasColumnName("title").IsRequired();
            row.Property(e => e.ReferencePeriod).HasColumnName("reference_period");
            row.Property(e => e.SourceKey).HasColumnName("source_key");
            row.Property(e => e.Status).HasColumnName("status").IsRequired();
            row.Property(e => e.ScheduledAtMs).HasColumnName("scheduled_at_ms");
            row.Property(e => e.ScheduledDate).HasColumnName("scheduled_date").IsRequired();
            row.Property(e => e.TimeAnnounced).HasColumnName("time_announced");
            row.Property(e => e.Sep).HasColumnName("sep");
            row.Property(e => e.Tentative).HasColumnName("tentative");
            row.Property(e => e.Change).HasColumnName("change");
            row.Property(e => e.CountedReschedule).HasColumnName("counted_reschedule");
            row.Property(e => e.FirstAnnouncedAtMs).HasColumnName("first_announced_at_ms");
            row.Property(e => e.RecordedAtMs).HasColumnName("recorded_at_ms");
            row.Property(e => e.ActualAtMs).HasColumnName("actual_at_ms");
            row.Property(e => e.Adapter).HasColumnName("adapter").IsRequired();
            row.Property(e => e.SourceKind).HasColumnName("source_kind").IsRequired();
            row.Property(e => e.SourceUrl).HasColumnName("source_url").IsRequired();
            row.Property(e => e.RetrievedAtMs).HasColumnName("retrieved_at_ms");
            row.Property(e => e.FileSha256).HasColumnName("file_sha256");
            row.Property(e => e.Derivation).HasColumnName("derivation");
            row.Property(e => e.Backfilled).HasColumnName("backfilled");
            row.Property(e => e.IsCurrent).HasColumnName("is_current");
            row.Property(e => e.Sequence).HasColumnName("ledger_sequence");
            row.Property(e => e.LedgerHash).HasColumnName("ledger_hash").IsRequired();
            row.HasIndex(e => e.Sequence).IsUnique().HasDatabaseName("ux_cat_events_sequence");
            row.HasIndex(e => new { e.IsCurrent, e.ScheduledAtMs }).HasDatabaseName("ix_cat_events_current_time");
            row.HasIndex(e => new { e.Family, e.ScheduledAtMs }).HasDatabaseName("ix_cat_events_family_time");
            row.HasIndex(e => new { e.Family, e.SourceKey }).HasDatabaseName("ix_cat_events_source_key");
            row.HasIndex(e => e.SourceUrl).HasDatabaseName("ix_cat_events_source_url");
        });
    }
}

/// <summary>One vintage of one catalyst, projected from <c>CatalystScheduled</c> or <c>CatalystRescheduled</c>.</summary>
internal sealed class CatalystEventRow
{
    public string CatalystId { get; set; } = "";

    public int Vintage { get; set; }

    /// <summary>Wire name (<c>FOMC</c>, <c>CPI</c>, ...).</summary>
    public string Family { get; set; } = "";

    public string Title { get; set; } = "";

    public string? ReferencePeriod { get; set; }

    public string? SourceKey { get; set; }

    /// <summary><see cref="Contracts.CatalystStatus"/> member name.</summary>
    public string Status { get; set; } = "";

    public long ScheduledAtMs { get; set; }

    /// <summary>New York date of the scheduled instant, <c>yyyy-MM-dd</c>.</summary>
    public string ScheduledDate { get; set; } = "";

    public bool TimeAnnounced { get; set; }

    public bool? Sep { get; set; }

    public bool Tentative { get; set; }

    /// <summary><see cref="Contracts.CatalystChange"/> member name; null for vintage 1.</summary>
    public string? Change { get; set; }

    /// <summary>The change is Moved or Status: this vintage counts against never-rescheduled.</summary>
    public bool CountedReschedule { get; set; }

    /// <summary>Vintage 1's ledger time, repeated on every vintage.</summary>
    public long FirstAnnouncedAtMs { get; set; }

    public long RecordedAtMs { get; set; }

    /// <summary>When the release happened; always null until prints are recorded.</summary>
    public long? ActualAtMs { get; set; }

    public string Adapter { get; set; } = "";

    /// <summary><see cref="Contracts.CatalystSourceKind"/> member name.</summary>
    public string SourceKind { get; set; } = "";

    public string SourceUrl { get; set; } = "";

    public long RetrievedAtMs { get; set; }

    public string? FileSha256 { get; set; }

    public string? Derivation { get; set; }

    /// <summary>Vintage 1's back-filled flag, repeated on every vintage.</summary>
    public bool Backfilled { get; set; }

    public bool IsCurrent { get; set; }

    public long Sequence { get; set; }

    public string LedgerHash { get; set; } = "";
}

internal sealed class CatalystsSchema(EngineDatabase database) : IEngineSchema
{
    public string Name => "catalysts";

    /// <summary>After analytics (400); nothing earlier reads catalysts.</summary>
    public int Order => 500;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = new CatalystsDbContext(database.ContextOptions<CatalystsDbContext>(CatalystsDbContext.HistoryTable));
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Opens read-only views of <c>cat_events</c> (no change tracking).</summary>
internal sealed class CatalystsReadStore(EngineDatabase database)
{
    public CatalystsDbContext Open()
    {
        var context = new CatalystsDbContext(database.ContextOptions<CatalystsDbContext>(CatalystsDbContext.HistoryTable));
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }

    /// <summary>
    /// Whether any vintage was read from <paramref name="url"/>. Calendar adapters fetch a historical
    /// page only while this is false, so a failed page is retried and an overlap page is recorded once.
    /// </summary>
    public async Task<bool> HasRecordedFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        await using var context = Open();
        return await context.Events.AnyAsync(row => row.SourceUrl == url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ids among <paramref name="family"/> (all families when null) with a vintage that moved the
    /// catalyst or changed its status: never-rescheduled is false for exactly these.
    /// </summary>
    public static async Task<HashSet<string>> RescheduledIdsAsync(CatalystsDbContext context, string? family, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var rows = context.Events.Where(row => row.CountedReschedule);
        if (family is not null)
        {
            rows = rows.Where(row => row.Family == family);
        }

        var ids = await rows.Select(row => row.CatalystId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        return new HashSet<string>(ids, StringComparer.Ordinal);
    }
}

/// <summary>Used by the EF migrations tool only.</summary>
internal sealed class CatalystsDesignTimeFactory : IDesignTimeDbContextFactory<CatalystsDbContext>
{
    public CatalystsDbContext CreateDbContext(string[] args) =>
        new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
            .ContextOptions<CatalystsDbContext>(CatalystsDbContext.HistoryTable));
}
