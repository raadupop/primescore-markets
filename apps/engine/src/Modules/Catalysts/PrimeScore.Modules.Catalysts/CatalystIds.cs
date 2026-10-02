using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Catalysts;

/// <summary>
/// Canonical catalyst ids, <c>FAMILY-YYYY-MM-DD</c> from the New York date of the first recorded
/// schedule (ADR-0011). The id never changes when the catalyst is rescheduled, so decisions,
/// outcomes and brief sentences join on a stable key while the vintages record the move.
/// </summary>
internal static class CatalystIds
{
    public static string For(CatalystFamily family, DateOnly newYorkDate) =>
        string.Create(CultureInfo.InvariantCulture, $"{family.ToWireName()}-{newYorkDate:yyyy-MM-dd}");

    /// <summary>
    /// The id for a new catalyst: <see cref="For"/>, or with <c>-2</c>, <c>-3</c>, ... appended when a
    /// catalyst that has since moved away from that date already owns it.
    /// </summary>
    public static string Allocate(CatalystFamily family, DateOnly newYorkDate, Func<string, bool> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var id = For(family, newYorkDate);
        if (!taken(id))
        {
            return id;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{id}-{suffix}");
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// The ledger entity id shared by every vintage of one catalyst: a name-based GUID (first 16
    /// bytes of SHA-256 of <c>catalyst:&lt;id&gt;</c>, version 5 and RFC 4122 variant bits).
    /// </summary>
    public static Guid EntityId(string catalystId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("catalyst:" + catalystId));
        var bytes = hash.AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
