namespace PrimeScore.Modules.Positions.Contracts;

/// <summary>
/// Milestone B slot. Position construction, sizing and execution mode need options prices
/// (strikes, premiums, Greeks); v1 has no options data source and does not simulate them.
/// </summary>
public static class PositionsCapability
{
    public const string Requirements = "POS-001, POS-002, POS-003, POS-004";

    public const string NotImplementedReason =
        "Position construction is not implemented in v1: it needs options prices (strikes, premiums, Greeks) " +
        "and v1 has no options data source. Planned for Milestone B (SRS " + Requirements + "). " +
        "The engine runs in simulation mode only.";
}
