namespace PrimeScore.Acceptance.Api.Harness;

/// <summary>Locations of the engine build output, the classifier and the shared registry.</summary>
internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string Registry => Path.Combine(Root, "infra", "registry.yaml");

    public static string ClassifierDirectory => Path.Combine(Root, "apps", "classification");

    /// <summary>Built by the solution before the tests run; same configuration as this assembly.</summary>
    public static string EngineHost
    {
        get
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var path = Path.Combine(Root, "apps", "engine", "src", "Host", "PrimeScore.Engine.Host", "bin", configuration, "net10.0", "PrimeScore.Engine.Host.dll");
            return File.Exists(path) ? path : throw new FileNotFoundException("Build the engine before running the acceptance suite.", path);
        }
    }

    /// <summary>
    /// <c>PRIMESCORE_PYTHON</c> (set by the repository gate), then an active or repository
    /// virtual environment, then <c>python</c> on PATH.
    /// </summary>
    public static string Python
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("PRIMESCORE_PYTHON") ?? Environment.GetEnvironmentVariable("HARNESS_PYTHON");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var environments = new[] { Environment.GetEnvironmentVariable("VIRTUAL_ENV"), Path.Combine(Root, ".venv"), Path.Combine(ClassifierDirectory, ".venv") };
            foreach (var environment in environments.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                foreach (var candidate in new[] { Path.Combine(environment!, "Scripts", "python.exe"), Path.Combine(environment!, "bin", "python") })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return OperatingSystem.IsWindows() ? "python" : "python3";
        }
    }

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "infra", "registry.yaml"))
                && Directory.Exists(Path.Combine(directory.FullName, "apps", "engine")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found above the test output directory.");
    }
}
