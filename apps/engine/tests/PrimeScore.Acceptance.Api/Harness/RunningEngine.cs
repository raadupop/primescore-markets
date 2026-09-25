using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api.Harness;

public enum Role
{
    Read,
    Admin,
}

/// <summary>
/// The engine and the Python classifier as separate processes on loopback ports, with a fresh
/// temporary database. Tests reach them over HTTP only (SRS EVO-001a). The classifier runs
/// with provider bootstrap disabled and the repository registry pinned, as the gate requires.
/// </summary>
public sealed class RunningEngine : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

    private readonly string _workDirectory;
    private readonly List<Process> _processes = [];
    private Process? _classifier;
    private Process? _engine;
    private readonly Dictionary<Role, string> _tokens = new()
    {
        [Role.Read] = RandomToken(),
        [Role.Admin] = RandomToken(),
    };

    private RunningEngine(string workDirectory)
    {
        _workDirectory = workDirectory;
    }

    public Uri ApiBase { get; private set; } = null!;

    public Uri ClassifierBase { get; private set; } = null!;

    public string EngineLogPath => Path.Combine(_workDirectory, "engine.log");

    /// <summary>The engine's SQLite file, for storage-level tests (SRS AUD-002).</summary>
    public string DatabasePath => Path.Combine(_workDirectory, "engine.db");

    public static async Task<RunningEngine> StartAsync(IReadOnlyDictionary<string, string>? engineSettings = null)
    {
        var workDirectory = Path.Combine(Path.GetTempPath(), "primescore-acceptance", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);
        var engine = new RunningEngine(workDirectory);
        try
        {
            await engine.StartClassifierAsync().ConfigureAwait(false);
            await engine.StartEngineAsync(engineSettings ?? new Dictionary<string, string>()).ConfigureAwait(false);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>The generated typed client, authenticated as <paramref name="role"/>.</summary>
    public PrimeScoreApiClient Client(Role role) => new(Http(role));

    /// <summary>A raw HTTP client for requests the typed client cannot express (malformed or unauthenticated).</summary>
    public HttpClient Http(Role? role)
    {
        var http = new HttpClient { BaseAddress = ApiBase, Timeout = TimeSpan.FromMinutes(5) };
        if (role is { } value)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _tokens[value]);
        }

        return http;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var process in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
            {
                // Already gone or not killable; the temp directory is disposable either way.
            }

            process.Dispose();
        }

        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Log files can stay locked briefly on Windows.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task StartClassifierAsync()
    {
        var port = FreePort();
        ClassifierBase = new Uri($"http://127.0.0.1:{port}/");
        var start = new ProcessStartInfo(RepositoryPaths.Python)
        {
            WorkingDirectory = RepositoryPaths.ClassifierDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-m", "uvicorn", "main:app", "--host", "127.0.0.1", "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--log-level", "warning" })
        {
            start.ArgumentList.Add(argument);
        }

        // Same isolation as the repository gate: no provider credentials, no live bootstrap.
        start.Environment["BOOTSTRAP_MODE"] = "disabled";
        start.Environment["RUN_LIVE_BOOTSTRAP"] = "";
        start.Environment["PRIMESCORE_REGISTRY_PATH"] = RepositoryPaths.Registry;
        start.Environment["REGISTRY_PATH"] = RepositoryPaths.Registry;
        foreach (var key in new[] { "FRED_API_KEY", "TWELVE_DATA_API_KEY", "FINNHUB_API_KEY", "ANTHROPIC_API_KEY" })
        {
            start.Environment[key] = "";
        }

        start.Environment["GOODNESS_OF_FIT_ALPHA"] = "0.05";
        start.Environment["DEGRADED_CERTAINTY_FACTOR"] = "0.5";
        start.Environment["LOG_LEVEL"] = "WARNING";
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHONHASHSEED"] = "0";
        _classifier = Launch(start, Path.Combine(_workDirectory, "classifier.log"));

        // /health answers 503 until windows are bootstrapped; any HTTP answer means it is up.
        await WaitAsync(ClassifierBase, "health", null, accept: _ => true, Path.Combine(_workDirectory, "classifier.log")).ConfigureAwait(false);
    }

    private async Task StartEngineAsync(IReadOnlyDictionary<string, string> engineSettings)
    {
        var port = FreePort();
        ApiBase = new Uri($"http://127.0.0.1:{port}/api/");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _workDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(RepositoryPaths.EngineHost);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");

        foreach (var key in start.Environment.Keys.Where(IsEngineSetting).ToArray())
        {
            start.Environment.Remove(key);
        }

        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        start.Environment["Engine__DatabasePath"] = Path.Combine(_workDirectory, "engine.db");
        start.Environment["Registry__Path"] = RepositoryPaths.Registry;
        start.Environment["Classifier__BaseUrl"] = ClassifierBase.ToString();

        // An empty consensus directory unless a test supplies one: the operator's curated files never leak in.
        var consensus = Path.Combine(_workDirectory, "consensus");
        Directory.CreateDirectory(consensus);
        start.Environment["Consensus__Directory"] = consensus;
        start.Environment["Logging__LogLevel__Default"] = "Warning";
        start.Environment["Auth__ApiTokens__0__Name"] = "acceptance-read";
        start.Environment["Auth__ApiTokens__0__Role"] = "READ";
        start.Environment["Auth__ApiTokens__0__Sha256"] = Sha256(_tokens[Role.Read]);
        start.Environment["Auth__ApiTokens__1__Name"] = "acceptance-admin";
        start.Environment["Auth__ApiTokens__1__Role"] = "ADMIN";
        start.Environment["Auth__ApiTokens__1__Sha256"] = Sha256(_tokens[Role.Admin]);
        foreach (var (key, value) in engineSettings)
        {
            start.Environment[key] = value;
        }

        _engine = Launch(start, EngineLogPath);
        await WaitAsync(ApiBase, "health", _tokens[Role.Read], accept: status => status == HttpStatusCode.OK, EngineLogPath).ConfigureAwait(false);
    }

    /// <summary>Stops the engine process and leaves its database in place.</summary>
    public async Task StopEngineAsync()
    {
        if (_engine is { HasExited: false } engine)
        {
            engine.Kill(entireProcessTree: true);
            await engine.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Runs an engine CLI command (e.g. <c>verify-ledger</c>) against this engine's database.</summary>
    public async Task<(int ExitCode, string Output)> RunEngineCommandAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _workDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(RepositoryPaths.EngineHost);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var key in start.Environment.Keys.Where(IsEngineSetting).ToArray())
        {
            start.Environment.Remove(key);
        }

        start.Environment["DOTNET_ENVIRONMENT"] = "Testing";
        start.Environment["Engine__DatabasePath"] = DatabasePath;
        start.Environment["Registry__Path"] = RepositoryPaths.Registry;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the engine command.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token).ConfigureAwait(false);
        return (process.ExitCode, await output.ConfigureAwait(false) + await errors.ConfigureAwait(false));
    }

    /// <summary>Runs a short Python script with the classifier's interpreter (used to reach the database at storage level).</summary>
    public static async Task<string> RunPythonAsync(string script, params string[] arguments)
    {
        var start = new ProcessStartInfo(RepositoryPaths.Python)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Python.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Python exited {process.ExitCode}: {await errors.ConfigureAwait(false)}");
        }

        return await output.ConfigureAwait(false);
    }

    private static bool IsEngineSetting(string key) =>
        key.StartsWith("Engine__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Auth__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Fred__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Consensus__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Classification__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Classifier__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Registry__", StringComparison.OrdinalIgnoreCase);

    /// <summary>Stops the classifier process, as an outage would (SRS CLS-004 tests).</summary>
    public async Task StopClassifierAsync()
    {
        if (_classifier is { HasExited: false } classifier)
        {
            classifier.Kill(entireProcessTree: true);
            await classifier.WaitForExitAsync().ConfigureAwait(false);
        }
    }

    private Process Launch(ProcessStartInfo start, string logPath)
    {
        var log = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var gate = new object();
        process.OutputDataReceived += (_, line) => { if (line.Data is not null) { lock (gate) { log.WriteLine(line.Data); } } };
        process.ErrorDataReceived += (_, line) => { if (line.Data is not null) { lock (gate) { log.WriteLine(line.Data); } } };
        process.Exited += (_, _) => { lock (gate) { log.Dispose(); } };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {start.FileName}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _processes.Add(process);
        return process;
    }

    private async Task WaitAsync(Uri baseAddress, string path, string? token, Func<HttpStatusCode, bool> accept, string logPath)
    {
        using var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(5) };
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_processes[^1].HasExited)
            {
                throw new InvalidOperationException($"Process exited during startup (code {_processes[^1].ExitCode}):\n{Tail(logPath)}");
            }

            try
            {
                using var response = await http.GetAsync(new Uri(path, UriKind.Relative)).ConfigureAwait(false);
                if (accept(response.StatusCode))
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(200).ConfigureAwait(false);
        }

        throw new TimeoutException($"{baseAddress} did not become ready within {StartupTimeout.TotalSeconds} s:\n{Tail(logPath)}");
    }

    private static string Tail(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n');
            return string.Join('\n', lines.TakeLast(40));
        }
        catch (IOException exception)
        {
            return $"(log unavailable: {exception.Message})";
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string RandomToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static string Sha256(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
