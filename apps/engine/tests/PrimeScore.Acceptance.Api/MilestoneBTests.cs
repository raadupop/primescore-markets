using PrimeScore.Acceptance.Api.Harness;
using PrimeScore.Api.Contracts;

namespace PrimeScore.Acceptance.Api;

/// <summary>
/// Brief §3: positions, exits and risk need an options price source v1 does not have. Their
/// endpoints stay in the contract and answer 501 with a body that says so.
/// </summary>
public sealed class MilestoneBTests(EngineFixture fixture)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public Task Listing_positions_is_501_and_names_POS_requirements() =>
        AssertNotImplemented(client => client.ListPositionsAsync(null, Token), "POS-001");

    [Fact]
    public Task Getting_a_position_is_501() =>
        AssertNotImplemented(client => client.GetPositionAsync(Guid.NewGuid(), Token), "POS-001");

    [Fact]
    public Task Listing_exits_is_501_and_names_EXT_requirements() =>
        AssertNotImplemented(client => client.ListExitsAsync(null, Token), "EXT-001");

    [Fact]
    public Task Risk_status_is_501_and_names_RSK_requirements() =>
        AssertNotImplemented(client => client.GetRiskStatusAsync(Token), "RSK-001");

    [Fact]
    public Task Setting_risk_limits_is_501() =>
        AssertNotImplemented(
            client => client.SetRiskLimitsAsync(new RiskLimitsConfig { Max_drawdown = 0.1, Fp_budget = 1000, Fp_warning_level = 0.8, Cash_reserve_minimum = 500, Max_per_trade = 100, Max_portfolio_allocation = 1000 }, Token),
            "RSK-001",
            Role.Admin);

    [Fact]
    public Task Setting_the_holding_period_is_501() =>
        AssertNotImplemented(client => client.SetHoldingPeriodAsync(new Body2 { Max_days = 5 }, Token), "EXT-002", Role.Admin);

    [Fact]
    public Task Switching_execution_mode_is_501_because_v1_is_simulation_only() =>
        AssertNotImplemented(client => client.SetExecutionModeAsync(new Body3 { Mode = ExecutionMode.LIVE }, Token), "simulation mode only", Role.Admin);

    [Fact]
    public Task Setting_the_approval_threshold_is_501() =>
        AssertNotImplemented(client => client.SetApprovalThresholdAsync(new Body4 { Urgency_tier = UrgencyTier.HIGH }, Token), "DEC-004", Role.Admin);

    private async Task AssertNotImplemented(Func<PrimeScoreApiClient, Task> call, string expectedInMessage, Role role = Role.Read)
    {
        var client = fixture.Engine.Client(role);

        var exception = await Assert.ThrowsAsync<PrimeScoreApiException<ErrorResponse>>(() => call(client));

        Assert.Equal(501, exception.StatusCode);
        Assert.Equal("not_implemented", exception.Result.Error);
        Assert.Contains("Milestone B", exception.Result.Message, StringComparison.Ordinal);
        Assert.Contains(expectedInMessage, exception.Result.Message, StringComparison.Ordinal);
    }
}
