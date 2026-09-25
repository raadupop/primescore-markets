using PrimeScore.Ledger;

namespace PrimeScore.Engine.Host.Health;

/// <summary>
/// Re-verifies the hash chain every <c>Ledger:VerifyEveryHours</c> (default 6, at least 0.25,
/// at most 168; 0 disables). Each verification records the head, which bounds how many
/// unverified entries could be truncated or rewritten undetected (ADR-0003). A verification
/// that cannot run is logged and retried on the next tick; it never stops the engine.
/// </summary>
internal sealed partial class LedgerVerificationScheduler(
    ILedgerVerifier verifier,
    IConfiguration configuration,
    ILogger<LedgerVerificationScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hours = configuration.GetValue("Ledger:VerifyEveryHours", 6.0);
        if (hours <= 0 || !double.IsFinite(hours))
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Clamp(hours, 0.25, 168)));
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ConfigureAwait(false);
            do
            {
                try
                {
                    var result = await verifier.VerifyAsync(stoppingToken).ConfigureAwait(false);
                    if (result.Ok)
                    {
                        LogVerified(result.EntriesChecked);
                    }
                    else
                    {
                        LogBroken(result.EntriesChecked, result.Reason);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogFailed(exception.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scheduled ledger verification: intact, {Entries} entries")]
    private partial void LogVerified(long entries);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Scheduled ledger verification: chain broken after {Entries} entries: {Reason}")]
    private partial void LogBroken(long entries, string? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scheduled ledger verification could not run: {Error}")]
    private partial void LogFailed(string error);
}
