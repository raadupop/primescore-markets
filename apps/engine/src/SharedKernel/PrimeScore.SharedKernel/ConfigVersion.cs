using System.Globalization;

namespace PrimeScore.SharedKernel;

/// <summary>
/// Monotonic version of the engine configuration. Every computed record carries the version
/// it used (SRS NFR-003, ANA-001); version 0 means no configuration has been recorded.
/// </summary>
public readonly record struct ConfigVersion(int Value) : IComparable<ConfigVersion>
{
    public static readonly ConfigVersion None = new(0);

    public ConfigVersion Next() => new(Value + 1);

    public int CompareTo(ConfigVersion other) => Value.CompareTo(other.Value);

    public static bool operator <(ConfigVersion left, ConfigVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ConfigVersion left, ConfigVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ConfigVersion left, ConfigVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ConfigVersion left, ConfigVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
