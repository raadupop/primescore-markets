using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PrimeScore.Modules.Ingestion.Sources;

/// <summary>
/// Reads an adapter's bound options without letting a value the binder cannot convert
/// (<c>Enabled=yes</c>, <c>PollInterval=15m</c>) escape: every read of <see cref="IOptions{T}.Value"/>
/// throws again then, and the status of every adapter, the pull command and the scheduler read these
/// properties. The adapter reports itself disabled with a reason that names the key but never the
/// value, which the binder's own message quotes and which may be a misplaced secret.
/// </summary>
internal static partial class SourceOptions
{
    public static T? TryRead<T>(IOptions<T> options, string section, out string? invalid)
        where T : class
    {
        try
        {
            invalid = null;
            return options.Value;
        }
        catch (InvalidOperationException exception)
        {
            var key = BinderKey().Match(exception.Message) is { Success: true } match ? match.Groups["key"].Value : section;
            invalid = $"invalid configuration: {key} has a value that is not of its type";
            return null;
        }
    }

    [GeneratedRegex(" at '(?<key>[^']+)'", RegexOptions.CultureInvariant)]
    private static partial Regex BinderKey();
}
