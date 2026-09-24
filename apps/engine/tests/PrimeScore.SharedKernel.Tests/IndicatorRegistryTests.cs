using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.SharedKernel.Tests;

public sealed class IndicatorRegistryTests
{
    private static string RepositoryRegistry =>
        IndicatorRegistryLoader.ResolvePath(null, AppContext.BaseDirectory);

    [Fact]
    public void The_repository_registry_loads_with_its_thirteen_symbols_in_four_classes()
    {
        var registry = IndicatorRegistryLoader.Load(RepositoryRegistry);

        Assert.Equal(13, registry.Symbols.Count);
        Assert.Equal(4, registry.Classes.Count);
    }

    [Fact]
    public void VIX_uses_a_1260_close_reference_window_on_business_day_cadence()
    {
        var vix = IndicatorRegistryLoader.Load(RepositoryRegistry).Symbols["VIX"];

        Assert.Equal(SourceCategory.MarketData, vix.IndicatorClass.SourceCategory);
        Assert.Equal(1260, vix.IndicatorClass.ReferenceWindowLength);
        Assert.True(vix.IndicatorClass.IsBusinessDayCadence);
        Assert.Equal(new ProviderMapping("fred", "VIXCLS", "none", Verified: true), vix.Bootstrap);
    }

    [Fact]
    public void CPI_YoY_has_no_long_horizon_so_its_reference_window_is_N_60()
    {
        var cpi = IndicatorRegistryLoader.Load(RepositoryRegistry).Symbols["CPI_YOY"];

        Assert.Null(cpi.IndicatorClass.LongHorizonN);
        Assert.Equal(60, cpi.IndicatorClass.ReferenceWindowLength);
        Assert.Equal("log_gaussian", cpi.IndicatorClass.SeverityFallbackFamily);
    }

    [Fact]
    public void Seven_fred_market_data_symbols_are_registered_and_five_are_unverified()
    {
        var registry = IndicatorRegistryLoader.Load(RepositoryRegistry);
        var market = registry.SymbolsWithProvider("fred")
            .Where(symbol => symbol.IndicatorClass.SourceCategory == SourceCategory.MarketData)
            .ToArray();

        Assert.Equal(["EVZ", "GVZ", "OVX", "RVX", "VIX", "VVIX", "VXN"], market.Select(symbol => symbol.Symbol));
        Assert.Equal(["OVX", "VIX"], market.Where(symbol => symbol.Bootstrap!.Verified).Select(symbol => symbol.Symbol));
    }

    [Theory]
    [InlineData("N_L: 277", "below the SRS floor of 278")]
    [InlineData("N_L: 1260\n    severity_fallback_family: gaussian", "must be 'none' when N_L is set")]
    [InlineData("N_L: null", "must be 'gaussian' or 'log_gaussian'")]
    [InlineData("N_L: 1260\n    unexpected_key: 1", "unexpected_key")]
    public void Invalid_classes_are_rejected(string classFields, string expectedMessage)
    {
        var path = WriteRegistry($"""
            classes:
              test_class:
                source_category: MARKET_DATA
                N: 20
                {classFields}
                deviation_kind: pct_change
                expected_frequency_seconds: 86400
            symbols:
              TEST:
                class: test_class
            """);

        var exception = Assert.ThrowsAny<Exception>(() => IndicatorRegistryLoader.Load(path));
        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_symbol_referencing_an_unknown_class_is_rejected()
    {
        var path = WriteRegistry("""
            classes: {}
            symbols:
              TEST:
                class: missing_class
            """);

        var exception = Assert.Throws<InvalidDataException>(() => IndicatorRegistryLoader.Load(path));
        Assert.Contains("unknown class 'missing_class'", exception.Message, StringComparison.Ordinal);
    }

    private static string WriteRegistry(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"primescore-registry-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, yaml.Replace("\r\n", "\n", StringComparison.Ordinal));
        return path;
    }
}
