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
