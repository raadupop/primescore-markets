[assembly: AssemblyFixture(typeof(PrimeScore.Acceptance.Api.Harness.EngineFixture))]

namespace PrimeScore.Acceptance.Api.Harness;

/// <summary>
/// One engine and classifier shared by tests that do not depend on an empty or specific
/// ledger. Tests that need isolation start their own <see cref="RunningEngine"/>.
/// </summary>
public sealed class EngineFixture : IAsyncLifetime
{
    private RunningEngine? _engine;

    public RunningEngine Engine => _engine ?? throw new InvalidOperationException("Engine not started.");

    public async ValueTask InitializeAsync() => _engine = await RunningEngine.StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_engine is not null)
        {
            await _engine.DisposeAsync();
        }
    }
}
