using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Configuration.Settings;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Configuration.Tests;

/// <summary>Settings validation (ADR-0004), the audit diff and versioned changes through the ledger (SRS NFR-003).</summary>
public sealed class SettingsTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-configuration-tests", Guid.NewGuid().ToString("N"));
    private ServiceProvider _services = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(_directory, "engine.db"),
        }).Build();
        _services = new ServiceCollection()
            .AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)))
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddConfigurationModule(configuration)
            .BuildServiceProvider();
        await EngineDatabaseInitializer.InitializeAsync(_services, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void The_seeded_defaults_are_valid_and_labelled_uncalibrated()
    {
        var defaults = DefaultSettings.Create();

        Assert.Empty(SettingsValidator.Validate(defaults));
        Assert.Equal("uncalibrated default", defaults.Calibration);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void A_sensitivity_factor_outside_0_to_1_is_rejected_because_it_could_make_the_implied_IV_negative(double factor)
    {
        var settings = WithEquity(context => context with { Sensitivity = context.Sensitivity with { LowVol = factor } });

        Assert.Contains(SettingsValidator.Validate(settings), error => error.StartsWith("contexts.equity.sensitivity.low_vol:", StringComparison.Ordinal));
    }

    [Fact]
    public void Weights_must_name_source_categories_and_be_non_negative()
    {
        var defaults = DefaultSettings.Create();
        var weights = new Dictionary<string, double>(defaults.Weighting.CategoryWeights) { ["WEATHER"] = 0.1, ["MARKET_DATA"] = -0.3 };

        var errors = SettingsValidator.Validate(defaults with { Weighting = defaults.Weighting with { CategoryWeights = weights } });

        Assert.Contains(errors, error => error.StartsWith("weighting.category_weights.WEATHER: 'WEATHER' is not a source category", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("weighting.category_weights.MARKET_DATA: must be a finite number", StringComparison.Ordinal));
    }

    [Fact]
    public void Dropout_tiers_must_ascend_and_only_the_last_may_be_unbounded()
    {
        var defaults = DefaultSettings.Create();

        var errors = SettingsValidator.Validate(defaults with { DropoutSchedule = [new DropoutTier(1800, 0.7), new DropoutTier(300, 0.95), new DropoutTier(900, 0.5)] });

        Assert.Contains(errors, error => error.StartsWith("dropout_schedule[1].below_seconds: tiers must be ascending", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("dropout_schedule: needs at least one tier and the last tier must have no upper bound", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.7, 0.3)]
    [InlineData(0.0, 0.7)]
    [InlineData(0.3, 1.0)]
    public void Percentile_regimes_need_0_below_low_below_high_below_1(double lowBelow, double highAbove)
    {
        var settings = WithEquity(context => context with { Regime = context.Regime with { LowBelow = lowBelow, HighAbove = highAbove } });

        Assert.Contains(SettingsValidator.Validate(settings), error => error.StartsWith("contexts.equity.regime:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_context_expecting_a_category_without_a_weight_or_window_is_rejected()
    {
        var settings = WithEquity(context => context with
        {
            Members = new Dictionary<string, IReadOnlyList<string>>(context.Members) { ["GEOPOLITICAL"] = ["curated"] },
        });
        var weights = settings.Weighting.CategoryWeights.Where(pair => pair.Key != "GEOPOLITICAL").ToDictionary();

        var errors = SettingsValidator.Validate(settings with { Weighting = settings.Weighting with { CategoryWeights = weights } });

        Assert.Contains("contexts.equity.members.GEOPOLITICAL: the weighting scheme has no weight for this category", errors);
    }

    [Fact]
    public void Context_names_are_lower_case_so_the_api_can_match_them_case_insensitively()
    {
        var errors = SettingsValidator.Validate(WithEquity(context => context with { Name = "Equity" }));

        Assert.Contains(errors, error => error.StartsWith("contexts.Equity.name:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_diff_lists_each_changed_leaf_once_with_old_and_new_values_and_addresses_contexts_by_name()
    {
        var before = DefaultSettings.Create();
        var after = WithEquity(context => context with { DislocationThreshold = 2.0 }) with { BypassPercentile = 0.995 };

        var changes = SettingsDiff.Between(before, after);

        Assert.Equal(["bypass_percentile: 0.999 → 0.995", "contexts[equity].dislocation_threshold: 1.5 → 2"], changes);
    }

    [Fact]
    public async Task The_first_start_seeds_version_1_and_each_accepted_change_records_the_next_version_with_its_diff()
    {
        var seeded = await Query<GetActiveSettings, SettingsVersion>(new GetActiveSettings());
        var ack = await Command<SetDislocationSettings, SettingsChangeAck>(new SetDislocationSettings(
            2.0, "OVX", new Dictionary<string, double> { ["high_vol"] = 0.4 }, null, "operator"));
        var unchanged = await Command<SetDislocationSettings, SettingsChangeAck>(new SetDislocationSettings(
            2.0, "OVX", new Dictionary<string, double> { ["high_vol"] = 0.4 }, null, "operator"));
        var active = await Query<GetActiveSettings, SettingsVersion>(new GetActiveSettings());

        Assert.Equal((1, "uncalibrated default"), (seeded.Version.Value, seeded.Settings.Calibration));
        Assert.Equal((true, 2), (ack.Accepted, ack.Version));
        Assert.Equal((true, (int?)null), (unchanged.Accepted, unchanged.Version));
        Assert.Equal(2, active.Version.Value);
        Assert.Equal("operator", active.ChangedBy);
        Assert.Equal(["contexts[oil].dislocation_threshold: 3 → 2", "contexts[oil].sensitivity.high_vol: 0.5 → 0.4"], active.Changes);
        Assert.Equal(2, (await Query<GetSettingsHistory, IReadOnlyList<SettingsVersion>>(new GetSettingsHistory())).Count);
    }

    [Fact]
    public async Task The_SRS_example_sensitivity_map_1_5_1_0_0_6_is_refused_and_no_version_is_recorded()
    {
        var ack = await Command<SetDislocationSettings, SettingsChangeAck>(new SetDislocationSettings(
            1.5, "VIX", new Dictionary<string, double> { ["low_vol"] = 1.5, ["normal"] = 1.0, ["high_vol"] = 0.6 }, null, "operator"));

        Assert.False(ack.Accepted);
        Assert.Contains(ack.Errors, error => error.Contains("sensitivity.low_vol: 1.5 is outside (0, 1]", StringComparison.Ordinal));
        Assert.Equal(1, (await Query<GetActiveSettings, SettingsVersion>(new GetActiveSettings())).Version.Value);
    }

    [Fact]
    public async Task Level_boundaries_switch_the_context_to_level_regimes_and_both_are_required()
    {
        var partial = await Command<SetDislocationSettings, SettingsChangeAck>(new SetDislocationSettings(
            1.5, null, null, new Dictionary<string, double> { ["low_vol_upper"] = 15 }, "operator"));
        var both = await Command<SetDislocationSettings, SettingsChangeAck>(new SetDislocationSettings(
            1.5, null, null, new Dictionary<string, double> { ["low_vol_upper"] = 15, ["high_vol_lower"] = 25 }, "operator"));
        var equity = (await Query<GetActiveSettings, SettingsVersion>(new GetActiveSettings())).Settings.Context("equity")!;

        Assert.Contains("regime_boundaries: both low_vol_upper and high_vol_lower are required", partial.Errors);
        Assert.True(both.Accepted);
        Assert.Equal(new RegimeRule(RegimeMode.Level, 0.30, 0.70, 15, 25), equity.Regime);
    }

    [Fact]
    public async Task A_padded_lower_case_reference_instrument_selects_its_own_context()
    {
        var ack = await Command<SetDislocationSettings, SettingsChangeAck>(new SetDislocationSettings(2.5, " ovx ", null, null, "operator"));
        var settings = (await Query<GetActiveSettings, SettingsVersion>(new GetActiveSettings())).Settings;

        Assert.True(ack.Accepted, string.Join("; ", ack.Errors));
        Assert.Equal(("OVX", 2.5), (settings.Context("oil")!.ReferenceInstrument, settings.Context("oil")!.DislocationThreshold));
        Assert.Equal(("VIX", 1.5), (settings.Context("equity")!.ReferenceInstrument, settings.Context("equity")!.DislocationThreshold));
    }

    [Fact]
    public void Two_contexts_cannot_share_a_reference_instrument()
    {
        var errors = SettingsValidator.Validate(WithEquity(context => context with { ReferenceInstrument = "ovx" }));

        Assert.Contains("contexts: equity and oil share the reference instrument ovx", errors);
    }

    private static EngineSettings WithEquity(Func<ContextSettings, ContextSettings> change)
    {
        var defaults = DefaultSettings.Create();
        return defaults with { Contexts = defaults.Contexts.Select(context => context.Name == "equity" ? change(context) : context).ToArray() };
    }

    private async Task<TResult> Query<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query, Token);
    }

    private async Task<TAck> Command<TCommand, TAck>(TCommand command) where TCommand : ICommand<TAck> where TAck : ICommandAck
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TAck>>().HandleAsync(command, Token);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
