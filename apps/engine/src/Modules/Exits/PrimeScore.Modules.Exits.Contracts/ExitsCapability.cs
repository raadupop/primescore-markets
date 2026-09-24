namespace PrimeScore.Modules.Exits.Contracts;

/// <summary>Milestone B slot. Exits act on positions, which v1 does not construct.</summary>
public static class ExitsCapability
{
    public const string Requirements = "EXT-001, EXT-002, EXT-003, EXT-004";

    public const string NotImplementedReason =
        "Exit management is not implemented in v1: exits act on options positions, which need an options " +
        "price source that v1 does not have. Planned for Milestone B (SRS " + Requirements + ").";
}
