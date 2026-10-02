using PrimeScore.Modules.Ingestion.Contracts;

namespace PrimeScore.Modules.Ingestion.Tests;

public sealed class SourcePrefixesTests
{
    [Theory]
    [InlineData("cboe:VIX", "Cboe", true)]
    [InlineData("fred:VIXCLS", "FRED", true)]
    [InlineData("bls:CPI_YOY", "api", false)] // an API row recorded before bls: was reserved keeps API treatment
    [InlineData("bls:CPI_YOY", "API", false)]
    [InlineData("econ:CPI_YOY", "Cboe", false)]
    [InlineData("cboe_vix", "Cboe", false)]
    public void Adapter_recorded_means_a_reserved_prefix_and_a_provider_other_than_the_api(string source, string provider, bool expected) =>
        Assert.Equal(expected, SourcePrefixes.IsAdapterRecorded(source, provider));

    [Fact]
    public void The_reserved_prefix_is_matched_in_any_letter_case_and_the_longer_vx_prefix_is_its_own()
    {
        Assert.Equal("cboe:", SourcePrefixes.ReservedPrefixOf("CBOE:VIX"));
        Assert.Equal("cboe-vx:", SourcePrefixes.ReservedPrefixOf("cboe-vx:VX30"));
        Assert.Null(SourcePrefixes.ReservedPrefixOf("cboe_vix"));
    }
}
