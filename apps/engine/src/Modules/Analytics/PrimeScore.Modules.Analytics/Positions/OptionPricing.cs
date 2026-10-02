namespace PrimeScore.Modules.Analytics.Positions;

/// <summary>
/// Black-Scholes-Merton for European index options with a continuous dividend yield (ADR-0014).
/// Volatility and rates are fractions; time is in years. At or after expiry the value is intrinsic.
/// </summary>
internal static class OptionPricing
{
    private const double InverseSqrtTwoPi = 0.398942280401432677939946059934;

    public static double Price(bool call, double spot, double strike, double years, double volatility, double rate, double dividendYield)
    {
        if (years <= 0 || volatility <= 0)
        {
            return call ? Math.Max(0, spot - strike) : Math.Max(0, strike - spot);
        }

        var (d1, d2) = D(spot, strike, years, volatility, rate, dividendYield);
        var carry = spot * Math.Exp(-dividendYield * years);
        var discounted = strike * Math.Exp(-rate * years);
        return call
            ? (carry * Cdf(d1)) - (discounted * Cdf(d2))
            : (discounted * Cdf(-d2)) - (carry * Cdf(-d1));
    }

    public static double Delta(bool call, double spot, double strike, double years, double volatility, double rate, double dividendYield)
    {
        if (years <= 0 || volatility <= 0)
        {
            return call ? (spot > strike ? 1 : 0) : (spot < strike ? -1 : 0);
        }

        var (d1, _) = D(spot, strike, years, volatility, rate, dividendYield);
        var carry = Math.Exp(-dividendYield * years);
        return call ? carry * Cdf(d1) : carry * (Cdf(d1) - 1);
    }

    /// <summary>Change of delta per 1-unit rise of the spot; the same for calls and puts.</summary>
    public static double Gamma(double spot, double strike, double years, double volatility, double rate, double dividendYield)
    {
        if (years <= 0 || volatility <= 0)
        {
            return 0;
        }

        var (d1, _) = D(spot, strike, years, volatility, rate, dividendYield);
        return Math.Exp(-dividendYield * years) * Pdf(d1) / (spot * volatility * Math.Sqrt(years));
    }

    /// <summary>Value change per 1.00 of volatility (divide by 100 for one volatility point); the same for calls and puts.</summary>
    public static double Vega(double spot, double strike, double years, double volatility, double rate, double dividendYield)
    {
        if (years <= 0 || volatility <= 0)
        {
            return 0;
        }

        var (d1, _) = D(spot, strike, years, volatility, rate, dividendYield);
        return spot * Math.Exp(-dividendYield * years) * Pdf(d1) * Math.Sqrt(years);
    }

    /// <summary>
    /// Standard normal distribution function to double precision (Hart's algorithm 5666 as given by G. West,
    /// "Better approximations to cumulative normal functions", 2005; absolute error about 1e-14).
    /// </summary>
    public static double Cdf(double x)
    {
        var z = Math.Abs(x);
        double tail;
        if (z > 37)
        {
            tail = 0;
        }
        else if (z < 7.07106781186547)
        {
            var numerator = (((((((3.52624965998911E-02 * z) + 0.700383064443688) * z) + 6.37396220353165) * z) + 33.912866078383) * z) + 112.079291497871;
            numerator = (((numerator * z) + 221.213596169931) * z) + 220.206867912376;
            var denominator = (((((((8.83883476483184E-02 * z) + 1.75566716318264) * z) + 16.064177579207) * z) + 86.7807322029461) * z) + 296.564248779674;
            denominator = (((((denominator * z) + 637.333633378831) * z) + 793.826512519948) * z) + 440.413735824752;
            tail = Math.Exp(-z * z / 2) * numerator / denominator;
        }
        else
        {
            var fraction = z + (1 / (z + (2 / (z + (3 / (z + (4 / (z + 0.65))))))));
            tail = Math.Exp(-z * z / 2) / fraction / 2.506628274631;
        }

        return x > 0 ? 1 - tail : tail;
    }

    private static double Pdf(double x) => InverseSqrtTwoPi * Math.Exp(-0.5 * x * x);

    private static (double D1, double D2) D(double spot, double strike, double years, double volatility, double rate, double dividendYield)
    {
        var deviation = volatility * Math.Sqrt(years);
        var d1 = (Math.Log(spot / strike) + ((rate - dividendYield + (0.5 * volatility * volatility)) * years)) / deviation;
        return (d1, d1 - deviation);
    }
}

/// <summary>
/// Implied volatility for a horizon from Cboe's constant-maturity S&amp;P 500 indices (VIX9D 9 days, VIX 30,
/// VIX3M 93, VIX6M 184), linear in total variance σ² · t between neighbours and flat beyond the ends (ADR-0014).
/// </summary>
internal sealed class VolatilityTermStructure
{
    private readonly (int Days, double Volatility)[] _points;

    /// <param name="points">Horizon in calendar days and volatility as a fraction; at least one point.</param>
    public VolatilityTermStructure(IEnumerable<(int Days, double Volatility)> points)
    {
        _points = points.Where(point => point.Days > 0 && point.Volatility > 0).OrderBy(point => point.Days).ToArray();
        if (_points.Length == 0)
        {
            throw new ArgumentException("At least one term-structure point is required.", nameof(points));
        }
    }

    public double At(double days)
    {
        if (days <= _points[0].Days)
        {
            return _points[0].Volatility;
        }

        if (days >= _points[^1].Days)
        {
            return _points[^1].Volatility;
        }

        var upper = Array.FindIndex(_points, point => point.Days >= days);
        var (d0, v0) = _points[upper - 1];
        var (d1, v1) = _points[upper];
        var w0 = v0 * v0 * d0;
        var w1 = v1 * v1 * d1;
        var w = w0 + ((w1 - w0) * (days - d0) / (d1 - d0));
        return Math.Sqrt(w / days);
    }
}
