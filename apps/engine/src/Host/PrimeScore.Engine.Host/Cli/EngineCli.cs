using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrimeScore.Engine.Host.Composition;
using PrimeScore.Engine.Host.Security;
using PrimeScore.Ledger;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.Modules.Ingestion.Contracts;
using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Engine.Host.Cli;

/// <summary>
/// Operator commands that run without the web surface:
/// <c>verify-ledger</c> recomputes the hash chain (SRS AUD-002, exit 0 intact, 1 broken, 2 error);
/// <c>new-api-token --role READ|ADMIN</c> prints a token once, with the hash to configure;
/// <c>replay --from yyyy-MM-dd --to yyyy-MM-dd [--label text]</c> records a replay under the current settings;
/// <c>outcomes-report [--context name] [--replay id]</c> prints the outcomes record as JSON (ADR-0008);
/// <c>sources</c> prints each source adapter's stored run record and coverage as JSON;
/// <c>signals [--instrument X] [--source-prefix p] [--provider P] [--take n]</c> prints recorded signals with
/// their provenance as JSON (local operator output: Cboe series are for internal use only);
/// <c>import-catalysts --file csv [--force]</c> records an operator-curated catalyst CSV (ADR-0011) and prints
/// the acknowledgement as JSON (exit 0 recorded, 1 file rejected with line numbers).
/// </summary>
internal static class EngineCli
{
    private static readonly JsonSerializerOptions Output = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions SnakeOutput = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool IsCommand(string[] args) => args is ["verify-ledger", ..] or ["new-api-token", ..] or ["evaluate-validation", ..] or ["set-operator-password", ..]
        or ["replay", ..] or ["outcomes-report", ..] or ["sources", ..] or ["signals", ..] or ["import-catalysts", ..];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "verify-ledger" => await VerifyLedgerAsync(args[1..]).ConfigureAwait(false),
                "evaluate-validation" => await EvaluateValidationAsync(args[1..]).ConfigureAwait(false),
                "set-operator-password" => OperatorSetup.Run(args[1..]),
                "replay" => await ReplayAsync(args[1..]).ConfigureAwait(false),
                "outcomes-report" => await OutcomesReportAsync(args[1..]).ConfigureAwait(false),
                "sources" => await SourcesAsync(args[1..]).ConfigureAwait(false),
                "signals" => await SignalsAsync(args[1..]).ConfigureAwait(false),
                "import-catalysts" => await ImportCatalystsAsync(args[1..]).ConfigureAwait(false),
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

    /// <summary>Flags with a value that the verbs read themselves; everything else goes to the host (e.g. --Engine:DatabasePath).</summary>
    private static readonly string[] VerbFlags =
        ["--from", "--to", "--label", "--context", "--replay", "--database", "--instrument", "--source-prefix", "--provider", "--take", "--file"];

    /// <summary>Flags without a value that the verbs read themselves; kept from the host, which would read them as keys.</summary>
    private static readonly string[] VerbSwitches = ["--force"];

    /// <summary>The value after <paramref name="flag"/>; null when the flag is absent; an error when it has no value.</summary>
    private static string? Option(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        if (index < 0)
        {
            return null;
        }

        return index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[index + 1]
            : throw new ArgumentException($"{flag} needs a value.");
    }

    private static string[] HostArgs(string[] args)
    {
        var rest = new List<string>();
        for (var index = 0; index < args.Length; index++)
        {
            if (VerbFlags.Contains(args[index], StringComparer.Ordinal))
            {
                index++;
                continue;
            }

            if (VerbSwitches.Contains(args[index], StringComparer.Ordinal))
            {
                continue;
            }

            rest.Add(args[index]);
        }

        return [.. rest];
    }

    private static IHost CoreHost(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = HostArgs(args),
            ContentRootPath = EnginePaths.ContentRoot(),
        });
        EnginePaths.ApplyDefaultDatabasePath(builder.Configuration, builder.Environment.ContentRootPath);
        if (Option(args, "--database") is { } database)
        {
            // dotnet run with the launch profile starts in the project folder, so a relative path can
            // name a file that does not exist there; a verb must never create a new empty database.
            var path = Path.GetFullPath(database);
            if (!File.Exists(path))
            {
                throw new IOException($"No engine database at '{path}'. Pass an absolute path.");
            }

            builder.Configuration["Engine:DatabasePath"] = path;
        }

        EnginePaths.ApplyDefaultOpecFile(builder.Configuration, builder.Environment.ContentRootPath);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddEngineCore(builder.Configuration);
        return builder.Build();
    }

    /// <summary>Records a replay of a date range (New York calendar) under the current settings and prints its id.</summary>
    private static async Task<int> ReplayAsync(string[] args)
    {
        if (!DateOnly.TryParseExact(Option(args, "--from"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var from)
            || !DateOnly.TryParseExact(Option(args, "--to"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var to))
        {
            throw new ArgumentException("replay needs --from yyyy-MM-dd and --to yyyy-MM-dd.");
        }

        var label = Option(args, "--label") ?? $"CLI replay {from:yyyy-MM-dd} to {to:yyyy-MM-dd}";
        _ = Option(args, "--database");
        using var host = CoreHost(args);
        await EngineDatabaseInitializer.InitializeAsync(host.Services, CancellationToken.None).ConfigureAwait(false);
        using var scope = host.Services.CreateScope();
        var input = await host.Services.GetRequiredService<ILedgerStatusQuery>().GetAsync(CancellationToken.None).ConfigureAwait(false);
        var ack = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunReplay, RunReplayAck>>().HandleAsync(new RunReplay(label,
            SharedKernel.MarketTime.AtNewYork(from, TimeOnly.MinValue), SharedKernel.MarketTime.AtNewYork(to, TimeOnly.MaxValue), null, "operator-cli",
            input.HeadSequence, input.HeadHash), CancellationToken.None).ConfigureAwait(false);
        if (ack.ReplayId is not { } id)
        {
            await Console.Error.WriteLineAsync(string.Join("; ", ack.Errors)).ConfigureAwait(false);
            return 1;
        }

        Console.WriteLine(id);
        return 0;
    }

    /// <summary>Prints the outcomes record (live journal, or a stored replay with --replay) as JSON: the same figures as the Outcomes page and API.</summary>
    private static async Task<int> OutcomesReportAsync(string[] args)
    {
        Guid? replay = null;
        if (Option(args, "--replay") is { } text)
        {
            replay = Guid.TryParse(text, out var parsed) ? parsed : throw new ArgumentException("--replay must be a replay id.");
        }

        var context = Option(args, "--context") ?? "equity";
        _ = Option(args, "--database");
        using var host = CoreHost(args);
        var database = host.Services.GetRequiredService<EngineDatabase>();
        if (!File.Exists(database.FullPath))
        {
            throw new IOException($"No engine database at '{database.FullPath}'.");
        }

        using var scope = host.Services.CreateScope();
        var report = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetForwardOutcomes, ForwardOutcomesReport?>>()
            .HandleAsync(new GetForwardOutcomes(context, ReplayId: replay), CancellationToken.None).ConfigureAwait(false);
        if (report is null)
        {
            await Console.Error.WriteLineAsync("Unknown context or replay.").ConfigureAwait(false);
            return 1;
        }

        Console.WriteLine(JsonSerializer.Serialize(report, Output));
        return 0;
    }

    /// <summary>
    /// Prints each adapter's database-derived status: its last stored run (error, partial, counts,
    /// flags, note) and recorded coverage per series. Enabled, running and next-run belong to the
    /// engine process and are left out.
    /// </summary>
    private static async Task<int> SourcesAsync(string[] args)
    {
        _ = Option(args, "--database");
        using var host = ExistingDatabaseHost(args);
        using var scope = host.Services.CreateScope();
        var statuses = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSourceStatus, IReadOnlyList<SourceStatus>>>()
            .HandleAsync(new GetSourceStatus(StoredOnly: true), CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(statuses.Select(status => new
        {
            source = status.Source,
            last_attempt_at = status.LastAttemptAt,
            last_success_at = status.LastSuccessAt,
            last_error = status.LastError,
            partial = status.LastRunPartial,
            counts = status.LastRun,
            flags = status.Flags,
            note = status.Note,
            series = status.Series.Select(series => new
            {
                series_id = series.SeriesId,
                source_identifier = series.SourceIdentifier,
                recorded = series.Recorded,
                latest = series.LatestObservedAt,
            }),
        }), SnakeOutput));
        return 0;
    }

    /// <summary>Prints recorded signals, newest observation first, with their provenance (file hash, reconstructed marker).</summary>
    private static async Task<int> SignalsAsync(string[] args)
    {
        var take = 100;
        if (Option(args, "--take") is { } text
            && !int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out take))
        {
            throw new ArgumentException("--take must be a whole number.");
        }

        var filter = new SignalFilter(
            Instrument: Option(args, "--instrument"), Take: take, SourcePrefix: Option(args, "--source-prefix"), Provider: Option(args, "--provider"));
        _ = Option(args, "--database");
        using var host = ExistingDatabaseHost(args);
        using var scope = host.Services.CreateScope();
        var page = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetSignals, SignalPage>>()
            .HandleAsync(new GetSignals(filter), CancellationToken.None).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            total = page.Total,
            signals = page.Signals.Select(signal => new
            {
                signal_id = signal.SignalId,
                ledger_sequence = signal.LedgerSequence,
                category = signal.Category,
                source_identifier = signal.SourceIdentifier,
                instrument = signal.Instrument,
                variant = signal.Variant,
                observed_at = signal.ObservedAt,
                recorded_at = signal.RecordedAt,
                value = signal.Value,
                payload = JsonDocument.Parse(signal.Payload).RootElement,
                provenance = signal.Provenance,
            }),
        }, SnakeOutput));
        return 0;
    }

    /// <summary>
    /// Records a curated catalyst CSV through the Catalysts module as adapter <c>import</c>, with the
    /// file's SHA-256 as provenance. The whole file is rejected when any row is invalid; the
    /// acknowledgement (snake_case JSON) then lists every error with its line number and exit is 1.
    /// <c>--force</c> lets curated rows replace schedules of a higher source kind.
    /// </summary>
    private static async Task<int> ImportCatalystsAsync(string[] args)
    {
        var path = Option(args, "--file") ?? throw new ArgumentException("import-catalysts needs --file <csv>.");
        var force = args.Contains("--force", StringComparer.Ordinal);
        _ = Option(args, "--database");
        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var name = Path.GetFileName(path);
        using var host = CoreHost(args);
        await EngineDatabaseInitializer.InitializeAsync(host.Services, CancellationToken.None).ConfigureAwait(false);
        using var scope = host.Services.CreateScope();
        var ack = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ImportCatalysts, ImportCatalystsAck>>()
            .HandleAsync(new ImportCatalysts(name, Encoding.UTF8.GetString(bytes), sha256, "operator-cli", force), CancellationToken.None)
            .ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            file = name,
            file_sha256 = sha256,
            forced = force,
            scheduled = ack.Scheduled,
            rescheduled = ack.Rescheduled,
            unchanged = ack.Unchanged,
            flags = ack.Flags,
            errors = ack.Errors,
        }, SnakeOutput));
        return ack.Errors.Count == 0 ? 0 : 1;
    }

    /// <summary>A core host over an existing database; read-only verbs never create or migrate one.</summary>
    private static IHost ExistingDatabaseHost(string[] args)
    {
        var host = CoreHost(args);
        var database = host.Services.GetRequiredService<EngineDatabase>();
        if (!File.Exists(database.FullPath))
        {
            host.Dispose();
            throw new IOException($"No engine database at '{database.FullPath}'.");
        }

        return host;
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
            "Geopolitical classifications and unsourced macro inputs are absent. Each replay records its configuration and source ledger boundary.",
            "Deploy by D checks D and its preceding NYSE trading day; no-deploy targets (FOMC) check that no decision reached DEPLOY. Iran Feb 2026 is a calibration target.", "",
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
