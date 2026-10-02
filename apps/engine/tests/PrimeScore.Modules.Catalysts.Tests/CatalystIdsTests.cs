using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Catalysts.Tests;

public sealed class CatalystIdsTests
{
    [Fact]
    public void The_id_is_the_family_wire_name_and_the_New_York_date()
    {
        Assert.Equal("FOMC-2026-10-28", CatalystIds.For(CatalystFamily.Fomc, new DateOnly(2026, 10, 28)));
        Assert.Equal("CLAIMS-2026-01-01", CatalystIds.For(CatalystFamily.Claims, new DateOnly(2026, 1, 1)));
        Assert.Equal("WPSR-2026-09-30", CatalystIds.For(CatalystFamily.Wpsr, new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void An_id_already_owned_by_a_moved_catalyst_gets_the_next_free_suffix()
    {
        var date = new DateOnly(2026, 10, 14);

        Assert.Equal("CPI-2026-10-14", CatalystIds.Allocate(CatalystFamily.Cpi, date, _ => false));
        Assert.Equal("CPI-2026-10-14-2", CatalystIds.Allocate(CatalystFamily.Cpi, date, id => id == "CPI-2026-10-14"));
        Assert.Equal("CPI-2026-10-14-3", CatalystIds.Allocate(CatalystFamily.Cpi, date, id => id is "CPI-2026-10-14" or "CPI-2026-10-14-2"));
    }

    [Fact]
    public void Every_vintage_of_a_catalyst_shares_one_name_based_entity_id()
    {
        var id = CatalystIds.EntityId("FOMC-2026-10-28");

        Assert.Equal(id, CatalystIds.EntityId("FOMC-2026-10-28"));
        Assert.NotEqual(id, CatalystIds.EntityId("FOMC-2026-12-09"));
        Assert.Equal(5, id.Version);
    }

    [Fact]
    public void Family_wire_names_are_exact_upper_case_names()
    {
        Assert.Equal(
            ["FOMC", "CPI", "NFP", "CLAIMS", "GDP", "PCE", "WPSR", "OPEC"],
            Enum.GetValues<CatalystFamily>().Select(family => family.ToWireName()));
        Assert.True(CatalystFamilyNames.TryParse("CLAIMS", out var claims));
        Assert.Equal(CatalystFamily.Claims, claims);
        Assert.False(CatalystFamilyNames.TryParse("fomc", out _));
        Assert.False(CatalystFamilyNames.TryParse("Fomc", out _));
    }
}
