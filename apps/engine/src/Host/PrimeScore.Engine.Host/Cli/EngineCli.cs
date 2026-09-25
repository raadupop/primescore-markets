using System.Security.Cryptography;
using System.Text.Json;
using PrimeScore.Engine.Host.Composition;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Engine.Host.Cli;

/// <summary>
/// Operator commands that run without the web surface:
/// <c>verify-ledger</c> recomputes the hash chain (SRS AUD-002, exit 0 intact, 1 broken, 2 error);
/// <c>new-api-token --role READ|ADMIN</c> prints a token once, with the hash to configure.
/// </summary>
internal static class EngineCli
{
    private static readonly JsonSerializerOptions Output = new() { WriteIndented = true };

    public static bool IsCommand(string[] args) => args is ["verify-ledger", ..] or ["new-api-token", ..] or ["evaluate-validation", ..];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "verify-ledger" => await VerifyLedgerAsync(args[1..]).ConfigureAwait(false),
                "evaluate-validation" => await EvaluateValidationAsync(args[1..]).ConfigureAwait(false),
                _ => NewApiToken(args[1..]),
            };
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or System.Data.Common.DbException)
        {
            await Console.Error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 2;
        }
    }

    private static async Task<int> VerifyLedgerAsync(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = EnginePaths.ContentRoot(),
        });
        EnginePaths.ApplyDefaultDatabasePath(builder.Configuration, builder.Environment.ContentRootPath);

        // Standard output carries only the JSON result; logs go to standard error.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddEngineCore(builder.Configuration);
        using var host = builder.Build();

        var database = host.Services.GetRequiredService<EngineDatabase>();
        if (!File.Exists(database.FullPath))
        {
            throw new IOException($"No engine database at '{database.FullPath}'.");
        }

        var verification = await host.Services.GetRequiredService<ILedgerVerifier>()
            .VerifyAsync(CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            database = database.FullPath,
            ok = verification.Ok,
            entries_checked = verification.EntriesChecked,
            head_sequence = verification.HeadSequence,
            first_invalid_sequence = verification.FirstInvalidSequence,
            reason = verification.Reason,
            verified_at = verification.VerifiedAt,
        }, Output));
        return verification.Ok ? 0 : 1;
    }

    private static int NewApiToken(string[] args)
    {
        var role = args is ["--role", var value, ..] ? value.ToUpperInvariant() : ApiRoles.Read;
        if (role is not (ApiRoles.Read or ApiRoles.Admin))
        {
            throw new ArgumentException($"--role must be {ApiRoles.Read} or {ApiRoles.Admin}.");
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Console.WriteLine($"Token (shown once; store it outside the repository): {token}");
        Console.WriteLine($"Role: {role}");
        Console.WriteLine($"Sha256: {ApiTokenAuthenticationHandler.HashToken(token)}");
        Console.WriteLine("Configure Auth:ApiTokens:<n>:Name, :Role and :Sha256 through environment variables (Auth__ApiTokens__<n>__Sha256, ...),");
        Console.WriteLine("or through dotnet user-secrets, which the engine reads only in the Development environment.");
        return 0;
    }

    private static async Task<int> EvaluateValidationAsync(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = EnginePaths.ContentRoot(),
        });
        EnginePaths.ApplyDefaultDatabasePath(builder.Configuration, builder.Environment.ContentRootPath);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddEngineCore(builder.Configuration);
        using var host = builder.Build();
        await EngineDatabaseInitializer.InitializeAsync(host.Services, CancellationToken.None).ConfigureAwait(false);
        using var scope = host.Services.CreateScope();
        var input = await host.Services.GetRequiredService<ILedgerStatusQuery>().GetAsync(CancellationToken.None).ConfigureAwait(false);
        var evaluation = await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateValidationEvents, ValidationEvaluationAck>>()
            .HandleAsync(new EvaluateValidationEvents("operator-cli", input.HeadSequence, input.HeadHash), CancellationToken.None).ConfigureAwait(false);
        var report = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetValidationReport, IReadOnlyList<ValidationEventResult>>>()
            .HandleAsync(new GetValidationReport(), CancellationToken.None).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(host.Services.GetRequiredService<EngineDatabase>().FullPath)!;
        var path = Path.Combine(directory, "validation-report.md");
        var lines = new List<string>
        {
            "# Validation targets", "",
            "Equity/VIX market-data proxy under uncalibrated rules. These are SRS targets, not predictive performance or returns.",
            "Deploy by D checks D and its preceding NYSE trading day; FOMC checks the dislocation threshold. Iran Feb 2026 is a calibration target.", "",
            "| Event | SRS expectation | Result | Evidence | Replay |", "| --- | --- | --- | --- | --- |",
        };
        lines.AddRange(report.Select(row => $"| {row.Event.Label} | {row.Event.Expected} | {row.Verdict} | {row.Detail} | {row.ReplayId} |"));
        await File.WriteAllLinesAsync(path, lines).ConfigureAwait(false);
        Console.WriteLine($"{evaluation.Completed} events completed. Report: {path}");
        foreach (var row in report)
        {
            Console.WriteLine($"{row.Event.Label}: {row.Verdict}. {row.Detail}");
        }

        return 0;
    }
}
