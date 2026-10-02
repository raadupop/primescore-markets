using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrimeScore.Ledger;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.Modules.Ingestion.Sources;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Tests;

/// <summary>
/// The module's registrations under the scope validation that <c>WebApplication</c> applies in
/// Development: a singleton adapter holding a scoped dependency would fail here, not on the operator's machine.
/// </summary>
public sealed class IngestionRegistrationTests
{
    [Fact]
    public async Task Adapters_and_the_scheduler_resolve_with_scope_validation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Engine:DatabasePath"] = Path.Combine(Path.GetTempPath(), "primescore-registration-tests", Guid.NewGuid().ToString("N"), "engine.db"),
        }).Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddSharedKernel(configuration)
            .AddLedger(configuration)
            .AddIngestionModule(configuration);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Contains(provider.GetServices<ISourceAdapter>(), adapter => adapter.Name == "FRED");
        Assert.Single(provider.GetServices<IHostedService>().OfType<SourceScheduler>());
    }
}
