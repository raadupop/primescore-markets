using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PrimeScore.Ledger;
using PrimeScore.Modules.Configuration.Settings;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Configuration.Storage;

/// <summary>Configuration's read table: one row per settings version, projected from <c>ConfigurationChanged</c>.</summary>
internal sealed class ConfigurationDbContext(DbContextOptions<ConfigurationDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__ef_history_configuration";

    public DbSet<SettingsRow> Versions => Set<SettingsRow>();

    public static ConfigurationDbContext Create(DbContextOptions<ConfigurationDbContext> options) => new(options);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingsRow>(version =>
        {
            version.ToTable("cfg_versions");
            version.HasKey(row => row.Version);
            version.Property(row => row.Version).HasColumnName("version").ValueGeneratedNever();
            version.Property(row => row.Sequence).HasColumnName("ledger_sequence");
            version.Property(row => row.RecordedAtMs).HasColumnName("recorded_at_ms");
            version.Property(row => row.ChangedBy).HasColumnName("changed_by").IsRequired();
            version.Property(row => row.Reason).HasColumnName("reason").IsRequired();
            version.Property(row => row.Settings).HasColumnName("settings").IsRequired();
            version.Property(row => row.Changes).HasColumnName("changes").IsRequired();
            version.HasIndex(row => row.Sequence).IsUnique().HasDatabaseName("ux_cfg_versions_sequence");
        });
    }
}

internal sealed class SettingsRow
{
    public int Version { get; set; }

    public long Sequence { get; set; }

    public long RecordedAtMs { get; set; }

    public string ChangedBy { get; set; } = "";

    public string Reason { get; set; } = "";

    public string Settings { get; set; } = "";

    public string Changes { get; set; } = "[]";
}

/// <summary>
/// Migrates the configuration table and, on a database without settings, records version 1
/// with the seeded defaults (brief §6). Runs after the ledger and before the modules that
/// compute with the settings.
/// </summary>
internal sealed class ConfigurationSchema(EngineDatabase database, SettingsWriter writer) : IEngineSchema
{
    public string Name => "configuration";

    public int Order => 50;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using (var context = new ConfigurationDbContext(database.ContextOptions<ConfigurationDbContext>(ConfigurationDbContext.HistoryTable)))
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        await writer.SeedAsync(DefaultSettings.Create(), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Opens read-only views of the configuration table (no change tracking).</summary>
internal sealed class ConfigurationReadStore(EngineDatabase database)
{
    public ConfigurationDbContext Open()
    {
        var context = new ConfigurationDbContext(database.ContextOptions<ConfigurationDbContext>(ConfigurationDbContext.HistoryTable));
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        return context;
    }
}

/// <summary>Used by the EF migrations tool only.</summary>
internal sealed class ConfigurationDesignTimeFactory : IDesignTimeDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext(string[] args) =>
        new(new EngineDatabase(Path.Combine(Path.GetTempPath(), "primescore-design.db"))
            .ContextOptions<ConfigurationDbContext>(ConfigurationDbContext.HistoryTable));
}
