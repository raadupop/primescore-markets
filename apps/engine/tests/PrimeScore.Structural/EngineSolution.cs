using System.Reflection;
using System.Xml.Linq;

namespace PrimeScore.Structural;

/// <summary>The engine's projects as declared on disk and loaded as assemblies.</summary>
internal static class EngineSolution
{
    public const string ModulesPrefix = "PrimeScore.Modules.";

    public static readonly string[] ImplementedModules = ["Ingestion", "Classification", "Decision", "Analytics", "Configuration"];

    public static readonly string[] MilestoneBModules = ["Positions", "Exits", "Risk"];

    public static IEnumerable<string> AllModules => ImplementedModules.Concat(MilestoneBModules);

    public static string Root { get; } = FindRoot();

    /// <summary>Project name → names of projects it references, from every csproj under <c>src/</c> and <c>tests/</c>.</summary>
    public static IReadOnlyDictionary<string, ProjectFile> Projects { get; } = LoadProjects();

    public static string Implementation(string module) => ModulesPrefix + module;

    public static string Contracts(string module) => ModulesPrefix + module + ".Contracts";

    public static Assembly Load(string assemblyName) => Assembly.Load(new AssemblyName(assemblyName));

    public static bool IsModuleImplementation(string projectName) =>
        projectName.StartsWith(ModulesPrefix, StringComparison.Ordinal)
        && !projectName.EndsWith(".Contracts", StringComparison.Ordinal)
        && !projectName.EndsWith(".Tests", StringComparison.Ordinal);

    public static bool IsModuleContracts(string projectName) =>
        projectName.StartsWith(ModulesPrefix, StringComparison.Ordinal) && projectName.EndsWith(".Contracts", StringComparison.Ordinal);

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PrimeScore.Engine.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("PrimeScore.Engine.sln not found above the test output directory.");
    }

    private static Dictionary<string, ProjectFile> LoadProjects()
    {
        var projects = new Dictionary<string, ProjectFile>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var references = XDocument.Load(path).Descendants("ProjectReference")
                .Select(reference => new ProjectReference(
                    Path.GetFileNameWithoutExtension(((string?)reference.Attribute("Include") ?? "").Replace('\\', Path.DirectorySeparatorChar)),
                    !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var name = Path.GetFileNameWithoutExtension(path);
            projects[name] = new ProjectFile(name, path, references);
        }

        return projects;
    }
}

internal sealed record ProjectReference(string Name, bool ReferencesAssembly);

internal sealed record ProjectFile(string Name, string Path, IReadOnlyList<ProjectReference> References)
{
    public IEnumerable<string> ReferencedProjects => References.Select(reference => reference.Name);
}
