namespace PrimeScore.Modules.Risk.Contracts;

/// <summary>
/// Milestone B slot. Drawdown, false-positive budget and cash reserve are measured on position
/// P&amp;L; v1 has no positions, so there is nothing to measure.
/// </summary>
public static class RiskCapability
{
    public const string Requirements = "RSK-001, RSK-002, RSK-003";

    public const string NotImplementedReason =
        "Portfolio risk management is not implemented in v1: drawdown, false-positive budget and cash reserve " +
        "are measured on position P&L, and v1 constructs no positions. Planned for Milestone B (SRS " + Requirements + ").";
}
