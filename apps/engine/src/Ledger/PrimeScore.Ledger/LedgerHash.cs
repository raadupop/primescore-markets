using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PrimeScore.Ledger;

/// <summary>
/// <c>hash = SHA-256(prev_hash ‖ sequence ‖ recorded_at ‖ kind ‖ entity_id ‖ correlation_id ‖
/// config_version ‖ summary ‖ payload)</c> over UTF-8, fields separated by U+001F, lowercase hex.
/// Every stored column is covered, so editing any of them breaks verification (SRS AUD-002).
/// </summary>
public static class LedgerHash
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";

    private const char Separator = '\u001F';

    public static string Compute(
        string prevHash,
        long sequence,
        string recordedAt,
        string kind,
        string entityId,
        string correlationId,
        int configVersion,
        string summary,
        string payload)
    {
        var material = string.Join(
            Separator,
            prevHash,
            sequence.ToString(CultureInfo.InvariantCulture),
            recordedAt,
            kind,
            entityId,
            correlationId,
            configVersion.ToString(CultureInfo.InvariantCulture),
            summary,
            payload);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>The stored text form of <c>recorded_at</c>; hashing uses exactly this string.</summary>
    public static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    public static string FormatId(Guid id) => id.ToString("D");
}
