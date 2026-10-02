using System.Text;

namespace PrimeScore.Modules.Catalysts.Tests;

/// <summary>The saved page excerpts under <c>Data/</c> (see <c>Data/README.md</c>).</summary>
internal static class CalendarFixtures
{
    public static byte[] Bytes(string relativePath) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Data", relativePath));

    public static string Text(string relativePath) => Encoding.UTF8.GetString(Bytes(relativePath));

    /// <summary><paramref name="text"/> with the first <paramref name="find"/> after the first <paramref name="anchor"/> replaced.</summary>
    public static string ReplaceAfter(string text, string anchor, string find, string replacement)
    {
        var start = text.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{anchor}' not in the fixture");
        var at = text.IndexOf(find, start, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{find}' not after '{anchor}'");
        return string.Concat(text.AsSpan(0, at), replacement, text.AsSpan(at + find.Length));
    }
}
