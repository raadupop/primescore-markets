using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Configuration;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Tests;

public sealed class ValidationTests
{
    [Fact]
    public async Task All_ten_events_complete_and_persist_honest_not_evaluable_results_without_observations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "primescore-validation", Guid.NewGuid().ToString("N"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(directory, "engine.db"),
        }).Build();
        await using var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSharedKernel(configuration).AddLedger(configuration).AddConfigurationModule(configuration).AddAnalyticsModule(configuration)
            .AddScoped<IQueryHandler<GetSignals, SignalPage>, EmptySignals>()
            .AddScoped<IQueryHandler<GetReplayDecisions, IReadOnlyList<DecisionView>>, EmptyDecisions>()
            .BuildServiceProvider();
        var token = TestContext.Current.CancellationToken;
        try
        {
            await EngineDatabaseInitializer.InitializeAsync(services, token);
            var input = await services.GetRequiredService<ILedgerStatusQuery>().GetAsync(token);
            var ack = await services.GetRequiredService<ICommandHandler<EvaluateValidationEvents, ValidationEvaluationAck>>()
                .HandleAsync(new EvaluateValidationEvents("test", input.HeadSequence, input.HeadHash), token);
            Assert.Equal(10, ack.Completed);
            var report = await services.GetRequiredService<IQueryHandler<GetValidationReport, IReadOnlyList<ValidationEventResult>>>()
                .HandleAsync(new GetValidationReport(), token);
            Assert.Equal(10, report.Count);
            Assert.All(report, item =>
            {
                Assert.NotNull(item.ReplayId);
                Assert.Equal("Not evaluable", item.Verdict);
            });
            Assert.Equal(new DateOnly(2026, 2, 27), report.Single(item => item.Event.Id == "iran-strikes").Event.TargetDate);
            Assert.Equal(new DateOnly(2018, 2, 1), MarketTime.NewYorkDate(report.Single(item => item.Event.Id == "volmageddon").Event.From));
            var head = await services.GetRequiredService<ILedgerStatusQuery>().GetAsync(token);
            Assert.Equal(input.Entries + 10, head.Entries);
            Assert.True((await services.GetRequiredService<ILedgerVerifier>().VerifyAsync(token)).Ok);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
        }
    }

    private sealed class EmptySignals : IQueryHandler<GetSignals, SignalPage>
    {
        public Task<SignalPage> HandleAsync(GetSignals query, CancellationToken cancellationToken) => Task.FromResult(new SignalPage([], 0));
    }

    private sealed class EmptyDecisions : IQueryHandler<GetReplayDecisions, IReadOnlyList<DecisionView>>
    {
        public Task<IReadOnlyList<DecisionView>> HandleAsync(GetReplayDecisions query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DecisionView>>([]);
    }
}
