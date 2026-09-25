using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.Modules.Configuration;
using PrimeScore.Modules.Configuration.Contracts;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;
using PrimeScore.SharedKernel.Messaging;

namespace PrimeScore.Modules.Decision.Tests;

/// <summary>The decision stage over a real ledger and configuration, fed by scripted dislocations.</summary>
public sealed class DecisionRunnerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Day = new(2018, 2, 5, 21, 15, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "primescore-decision-tests", Guid.NewGuid().ToString("N"));
    private readonly ScriptedAggregates _aggregates = new();
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
            .AddDecisionModule(configuration)
            .AddSingleton<IQueryHandler<GetAggregatesAfter, IReadOnlyList<AggregateRecord>>>(_aggregates)
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
    public async Task Each_dislocation_gets_exactly_one_decision_under_its_correlation_id_and_a_rerun_records_nothing()
    {
        var first = _aggregates.Add(sequence: 11, composite: 0.9992, dislocation: 18.645072, at: Day);
        _aggregates.Add(sequence: 21, composite: 0.3, dislocation: 5.0, at: Day.AddDays(1));

        var ack = await PublishAndCountAsync();
        var again = await CommandAsync();
        _aggregates.Add(sequence: 31, composite: -0.8849, dislocation: -8.132231, at: Day.AddDays(2));
        var third = await CommandAsync();
        var decisions = await Query<GetDecisions, IReadOnlyList<DecisionView>>(new GetDecisions());

        Assert.Equal((1, 1), (ack.Deploy, ack.Idle));
        Assert.Equal((0, 0), (again.Deploy, again.Idle));
        Assert.Equal((1, 0), (third.Deploy, third.Idle));
        Assert.Equal([Day.AddDays(2), Day.AddDays(1), Day], decisions.Select(decision => decision.AsOf));
        var volmageddon = decisions.Single(decision => decision.AsOf == Day);
        Assert.Equal((first.Dislocation.CorrelationId, 1, DecisionOutcome.Deploy, "vol-expansion"),
            (volmageddon.CorrelationId, volmageddon.ConfigVersion.Value, volmageddon.Outcome, volmageddon.Scenario));
        Assert.Equal("vol-compression", decisions[0].Scenario);
    }

    [Fact]
    public async Task The_audit_trail_lists_every_decision_in_ledger_order_with_its_event_type()
    {
        _aggregates.Add(sequence: 11, composite: 0.9992, dislocation: 18.645072, at: Day);
        _aggregates.Add(sequence: 21, composite: 0.3, dislocation: 5.0, at: Day.AddDays(1));
        await CommandAsync();

        var audit = await Query<GetAuditEntries, IReadOnlyList<AuditEntryView>>(new GetAuditEntries());
        var idle = await Query<GetAuditEntries, IReadOnlyList<AuditEntryView>>(new GetAuditEntries(EventType: AuditEventType.IdleDecision));

        Assert.Equal([AuditEventType.DeployDecision, AuditEventType.IdleDecision], audit.Select(entry => entry.EventType));
        Assert.True(audit[0].SequenceNumber < audit[1].SequenceNumber);
        Assert.Contains("\"composite_score\":0.3", Assert.Single(idle).InputSnapshot, StringComparison.Ordinal);
        Assert.Contains("\"outcome\":\"IDLE\"", idle[0].OutputSnapshot, StringComparison.Ordinal);

        // From and To bound the recording time (the fixed clock, 2026-09-25 12:00 UTC), not the 2018 observation times.
        Assert.Equal(2, (await Query<GetAuditEntries, IReadOnlyList<AuditEntryView>>(new GetAuditEntries(From: new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero)))).Count);
        Assert.Empty(await Query<GetAuditEntries, IReadOnlyList<AuditEntryView>>(new GetAuditEntries(To: new DateTimeOffset(2018, 12, 31, 0, 0, 0, TimeSpan.Zero))));
        Assert.Empty(await Query<GetAuditEntries, IReadOnlyList<AuditEntryView>>(new GetAuditEntries(From: new DateTimeOffset(2026, 9, 25, 12, 0, 0, 1, TimeSpan.Zero))));
    }

    [Fact]
    public async Task Changed_deploy_conditions_apply_from_the_next_decision_which_names_the_new_version()
    {
        _aggregates.Add(sequence: 11, composite: 0.6, dislocation: 5.0, at: Day);
        await CommandAsync();
        using (var scope = _services.CreateScope())
        {
            var ack = await scope.ServiceProvider.GetRequiredService<ICommandHandler<SetDeployConditions, SettingsChangeAck>>()
                .HandleAsync(new SetDeployConditions([new DeployCondition(DeployConditionNames.CompositeScore, ">=", 0.7)], "test"), Token);
            Assert.Equal(2, ack.Version);
        }

        _aggregates.Add(sequence: 21, composite: 0.6, dislocation: 5.0, at: Day.AddDays(1));
        await CommandAsync();
        var decisions = await Query<GetDecisions, IReadOnlyList<DecisionView>>(new GetDecisions());

        Assert.Equal((DecisionOutcome.Idle, 2, 0.7), (decisions[0].Outcome, decisions[0].ConfigVersion.Value, decisions[0].Conditions.Single(condition => condition.Name == "composite_score").Required));
        Assert.Equal((DecisionOutcome.Deploy, 1), (decisions[1].Outcome, decisions[1].ConfigVersion.Value));
    }

    private async Task<MakePendingDecisionsAck> PublishAndCountAsync()
    {
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(new AggregatesRecorded(2, CorrelationId.New()), Token);
        var decisions = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetDecisions, IReadOnlyList<DecisionView>>>().HandleAsync(new GetDecisions(), Token);
        return new MakePendingDecisionsAck(decisions.Count(decision => decision.Outcome == DecisionOutcome.Deploy), decisions.Count(decision => decision.Outcome == DecisionOutcome.Idle));
    }

    private async Task<MakePendingDecisionsAck> CommandAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<MakePendingDecisions, MakePendingDecisionsAck>>().HandleAsync(new MakePendingDecisions(), Token);
    }

    private async Task<TResult> Query<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().HandleAsync(query, Token);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Dislocations with one confirmed VIX close each, served by sequence like the classification query.</summary>
    private sealed class ScriptedAggregates : IQueryHandler<GetAggregatesAfter, IReadOnlyList<AggregateRecord>>
    {
        private readonly List<AggregateRecord> _records = [];

        public AggregateRecord Add(long sequence, double composite, double dislocation, DateTimeOffset at)
        {
            var correlation = CorrelationId.New();
            var close = new ConfirmedAssessment(Guid.NewGuid(), Guid.NewGuid(), sequence - 3, SourceCategory.MarketData, "VIX", at, composite, 1.0, IsFallback: false);
            var compositeView = new CompositeView(Guid.NewGuid(), sequence - 1, correlation, new ConfigVersion(1), "equity", composite, "srs_default", "MaxConfirmedWeighted",
                [new CategoryContribution(SourceCategory.MarketData, 0.3, 1, 0, composite, composite, null, null, [close.AssessmentId], [])],
                [], close.SignalId, close.AssessmentId, at, at);
            var dislocationView = new DislocationView(Guid.NewGuid(), sequence, correlation, new ConfigVersion(1), "equity", compositeView.CompositeId, composite,
                "VIX", 20, at, Guid.NewGuid(), "normal", 0.5, 1260, 0.75, 20 + dislocation, dislocation, 1.5, Math.Abs(dislocation) >= 1.5, at, at);
            var record = new AggregateRecord(compositeView, dislocationView, [close]);
            _records.Add(record);
            return record;
        }

        public Task<IReadOnlyList<AggregateRecord>> HandleAsync(GetAggregatesAfter query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AggregateRecord>>(_records
                .Where(record => record.Dislocation.LedgerSequence > query.AfterSequence)
                .OrderBy(record => record.Dislocation.LedgerSequence)
                .Take(query.Take)
                .ToArray());
    }
}
