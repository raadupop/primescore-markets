using System.Text.Json;
using PrimeScore.SharedKernel.Json;

namespace PrimeScore.SharedKernel.Tests;

public sealed class CanonicalJsonTests
{
    [Fact]
    public void Keys_are_sorted_ordinally_at_every_depth_and_whitespace_is_removed()
    {
        var canonical = CanonicalJson.Canonicalize("""{ "b": 1, "a": { "z": [ 2, 1 ], "Z": true } }""");

        Assert.Equal("""{"a":{"Z":true,"z":[2,1]},"b":1}""", canonical);
    }

    [Fact]
    public void Objects_serialize_with_snake_case_names_and_omit_nulls()
    {
        var canonical = CanonicalJson.Serialize(new { ObservedValue = 17.31, SourceIdentifier = "fred:VIXCLS", Missing = (string?)null });

        Assert.Equal("""{"observed_value":17.31,"source_identifier":"fred:VIXCLS"}""", canonical);
    }

    [Theory]
    [InlineData("""{"a":1,"b":[true,null,"x"]}""")]
    [InlineData("""{"value":0.1}""")]
    [InlineData("""{"value":-1.25E-07}""")]
    public void Canonical_text_is_a_fixed_point(string canonical) =>
        Assert.Equal(canonical, CanonicalJson.Canonicalize(canonical));

    [Theory]
    [InlineData("""{"v":1.0}""", """{"v":1}""")]
    [InlineData("""{"v":1E0}""", """{"v":1}""")]
    [InlineData("""{"v":17.310}""", """{"v":17.31}""")]
    [InlineData("""{"v":-0.50}""", """{"v":-0.5}""")]
    [InlineData("""{"v":9007199254740993}""", """{"v":9007199254740993}""")]
    public void Equal_numbers_written_differently_canonicalize_to_one_spelling(string input, string expected) =>
        Assert.Equal(expected, CanonicalJson.Canonicalize(input));

    [Theory]
    [InlineData("""{"v":1e400}""")]
    [InlineData("""{"v":-1e400}""")]
    public void Numbers_that_overflow_a_double_are_rejected(string input) =>
        Assert.ThrowsAny<JsonException>(() => CanonicalJson.Canonicalize(input));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Non_finite_numbers_are_rejected(double value) =>
        Assert.ThrowsAny<Exception>(() => CanonicalJson.Serialize(new { value }));

    [Fact]
    public void Enums_are_written_by_name()
    {
        var canonical = CanonicalJson.Serialize(new { Category = SourceCategory.CrossAssetFlow });

        Assert.Equal("""{"category":"CrossAssetFlow"}""", canonical);
        Assert.Equal(SourceCategory.CrossAssetFlow, JsonDocument.Parse(canonical).RootElement.GetProperty("category").Deserialize<SourceCategory>(CanonicalJson.SerializerOptions));
    }
}
