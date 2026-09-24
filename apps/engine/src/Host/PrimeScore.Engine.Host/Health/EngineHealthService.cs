using System.Reflection;
using PrimeScore.Ledger;
using PrimeScore.Modules.Classification.Contracts;
using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Engine.Host.Health;

public sealed record EngineHealthSnapshot(
    bool Ok,
    string EngineVersion,
    DateTimeOffset CheckedAt,
    LedgerStatus Ledger,
    ClassifierHealth Classifier);

/// <summary>
/// One view of engine health for the API and the UI. Degraded when the classifier cannot be
/// reached or the last ledger verification failed.
/// </summary>
public sealed class EngineHealthService(
    ILedgerStatusQuery ledger,
    IQueryHandler<GetClassifierHealth, ClassifierHealth> classifier,
    IClock clock)
{
    public static string EngineVersion { get; } =
        typeof(EngineHealthService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public async Task<EngineHealthSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var ledgerStatus = await ledger.GetAsync(cancellationToken).ConfigureAwait(false);
        var classifierHealth = await classifier.HandleAsync(new GetClassifierHealth(), cancellationToken).ConfigureAwait(false);
        var ok = classifierHealth.Reachable && (ledgerStatus.LastVerification?.Ok ?? true);
        return new EngineHealthSnapshot(ok, EngineVersion, clock.UtcNow, ledgerStatus, classifierHealth);
    }
}
