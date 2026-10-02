using PrimeScore.Modules.Analytics.Positions;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>Black-Scholes-Merton and the term-structure interpolation against published and hand-derived values (ADR-0014).</summary>
public sealed class OptionPricingTests
{
    // Hull, Options, Futures and Other Derivatives: S = 42, K = 40, r = 10%, σ = 20%, T = 0.5 → call 4.76, put 0.81.
    [Fact]
    public void Hull_example_prices_match()
    {
        Assert.Equal(4.7594, OptionPricing.Price(true, 42, 40, 0.5, 0.2, 0.10, 0), 4);
        Assert.Equal(0.8086, OptionPricing.Price(false, 42, 40, 0.5, 0.2, 0.10, 0), 4);
    }

    // S = K = 100, T = 1, r = 5%, σ = 20%, q = 0: d1 = 0.35, d2 = 0.15. Call 10.4506, put 5.5735;
    // delta N(0.35) = 0.636831; gamma n(0.35) / (100 × 0.2) = 0.375240 / 20 = 0.018762; vega 100 × 0.375240 = 37.5240.
    [Fact]
    public void At_the_money_values_and_greeks_match_the_hand_calculation()
    {
        Assert.Equal(10.4506, OptionPricing.Price(true, 100, 100, 1, 0.2, 0.05, 0), 4);
        Assert.Equal(5.5735, OptionPricing.Price(false, 100, 100, 1, 0.2, 0.05, 0), 4);
        Assert.Equal(0.636831, OptionPricing.Delta(true, 100, 100, 1, 0.2, 0.05, 0), 6);
        Assert.Equal(0.636831 - 1, OptionPricing.Delta(false, 100, 100, 1, 0.2, 0.05, 0), 6);
        Assert.Equal(0.018762, OptionPricing.Gamma(100, 100, 1, 0.2, 0.05, 0), 6);
        Assert.Equal(37.5240, OptionPricing.Vega(100, 100, 1, 0.2, 0.05, 0), 4);
    }

    // Put-call parity with a dividend yield: C − P = S e^(−qT) − K e^(−rT).
    [Theory]
    [InlineData(4900, 5000, 36 / 365.0, 0.205, 0.04, 0.013)]
    [InlineData(7600, 7000, 0.5, 0.18, 0.045, 0.012)]
    public void Put_call_parity_holds_with_dividends(double spot, double strike, double years, double sigma, double rate, double dividend)
    {
        var parity = (spot * Math.Exp(-dividend * years)) - (strike * Math.Exp(-rate * years));

        Assert.Equal(parity, OptionPricing.Price(true, spot, strike, years, sigma, rate, dividend) - OptionPricing.Price(false, spot, strike, years, sigma, rate, dividend), 6);
    }

    [Fact]
    public void At_expiry_the_value_is_intrinsic()
    {
        Assert.Equal(100, OptionPricing.Price(true, 5100, 5000, 0, 0.2, 0.04, 0.013));
        Assert.Equal(0, OptionPricing.Price(false, 5100, 5000, 0, 0.2, 0.04, 0.013));
        Assert.Equal(0.5, OptionPricing.Cdf(0), 7);
        Assert.Equal(0.9750021048517795, OptionPricing.Cdf(1.96), 13);
        Assert.Equal(0.0013498980316301, OptionPricing.Cdf(-3), 13);
        Assert.Equal(1 - 2.866515718791939E-07, OptionPricing.Cdf(5), 13);
    }

    // Points 9 d 18.9%, 30 d 20%, 93 d 22%, 184 d 23%. At 36 days: w30 = 0.04 × 30 = 1.2, w93 = 0.0484 × 93 = 4.5012,
    // w36 = 1.2 + 3.3012 × 6 / 63 = 1.51440; σ = √(1.51440 / 36) = 0.205102. Flat outside 9 to 184 days.
    [Fact]
    public void The_term_structure_interpolates_total_variance_and_is_flat_beyond_its_ends()
    {
        var term = new VolatilityTermStructure([(30, 0.20), (9, 0.189), (184, 0.23), (93, 0.22)]);

        Assert.Equal(0.205102, term.At(36), 6);
        Assert.Equal(0.20, term.At(30), 12);
        Assert.Equal(0.189, term.At(5), 12);
        Assert.Equal(0.23, term.At(400), 12);
    }
}
