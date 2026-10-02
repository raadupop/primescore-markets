namespace PrimeScore.Engine.Host.Composition;

/// <summary>Where the engine looks for its settings and database when none are configured.</summary>
internal static class EnginePaths
{
    private const string SolutionFile = "PrimeScore.Engine.sln";

    /// <summary>The current directory when it holds <c>appsettings.json</c>, otherwise the build output.</summary>
    public static string ContentRoot()
    {
        var current = Directory.GetCurrentDirectory();
        return File.Exists(Path.Combine(current, "appsettings.json")) ? current : AppContext.BaseDirectory;
    }

    /// <summary>
    /// <c>Engine:DatabasePath</c> when configured; otherwise <c>apps/engine/var/engine.db</c> next to
    /// the solution (brief §6), or <c>var/engine.db</c> under the content root outside a checkout.
    /// </summary>
    public static void ApplyDefaultDatabasePath(IConfigurationManager configuration, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Engine:DatabasePath"]))
        {
            return;
        }

        var engineRoot = FindUpwards(contentRoot, SolutionFile) ?? FindUpwards(AppContext.BaseDirectory, SolutionFile) ?? contentRoot;
        configuration["Engine:DatabasePath"] = Path.Combine(engineRoot, "var", "engine.db");
    }

    /// <summary><c>Consensus:Directory</c> when configured; otherwise <c>apps/engine/data/consensus</c> (brief §5).</summary>
    public static void ApplyDefaultConsensusDirectory(IConfigurationManager configuration, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Consensus:Directory"]))
        {
            return;
        }

        var engineRoot = FindUpwards(contentRoot, SolutionFile) ?? FindUpwards(AppContext.BaseDirectory, SolutionFile);
        if (engineRoot is not null)
        {
            configuration["Consensus:Directory"] = Path.Combine(engineRoot, "data", "consensus");
        }
    }

    /// <summary>
    /// <c>Sources:OpecCalendar:File</c> when configured; otherwise <c>apps/engine/data/catalysts/opec.csv</c>,
    /// the operator-curated OPEC and JMMC meeting dates (ADR-0011).
    /// </summary>
    public static void ApplyDefaultOpecFile(IConfigurationManager configuration, string contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(configuration["Sources:OpecCalendar:File"]))
        {
            return;
        }

        var engineRoot = FindUpwards(contentRoot, SolutionFile) ?? FindUpwards(AppContext.BaseDirectory, SolutionFile);
        if (engineRoot is not null)
        {
            configuration["Sources:OpecCalendar:File"] = Path.Combine(engineRoot, "data", "catalysts", "opec.csv");
        }
    }

    private static string? FindUpwards(string start, string fileName)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, fileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}
