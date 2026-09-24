using System.Security.Cryptography;
using System.Text.Json;
using PrimeScore.Engine.Host.Composition;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Ledger;

namespace PrimeScore.Engine.Host.Cli;

/// <summary>
/// Operator commands that run without the web surface:
/// <c>verify-ledger</c> recomputes the hash chain (SRS AUD-002, exit 0 intact, 1 broken, 2 error);
/// <c>new-api-token --role READ|ADMIN</c> prints a token once, with the hash to configure.
/// </summary>
internal static class EngineCli
{
    private static readonly JsonSerializerOptions Output = new() { WriteIndented = true };

    public static bool IsCommand(string[] args) => args is ["verify-ledger", ..] or ["new-api-token", ..];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "verify-ledger" => await VerifyLedgerAsync(args[1..]).ConfigureAwait(false),
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
}
