using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Core;

public class CommodityTests
{
    [Theory]
    [InlineData(Commodity.Scrap)]
    [InlineData(Commodity.Fuel)]
    [InlineData(Commodity.Crew)]
    [InlineData(Commodity.Water)]
    [InlineData(Commodity.Ore)]
    public void Data_HasValidEntries(Commodity commodity)
    {
        var data = CommodityEx.Data[commodity];

        data.InitialValue.Should().BeGreaterThan(0, "all commodities should have positive value");
        data.Volume.Should().BeGreaterThanOrEqualTo(0, "volume should not be negative");
        data.Mass.Should().BeGreaterThanOrEqualTo(0, "mass should not be negative");
    }

    [Fact]
    public void AllCommodities_HaveData()
    {
        foreach (Commodity commodity in Enum.GetValues<Commodity>())
        {
            var data = CommodityEx.Data[commodity];
            data.InitialValue.Should().BeGreaterThan(0, $"{commodity} should have valid data");
        }
    }

    [Fact]
    public void Mass_ReturnsCorrectValue()
    {
        Commodity.Scrap.Mass().Should().Be(CommodityEx.Data[Commodity.Scrap].Mass);
        Commodity.Water.Mass().Should().Be(CommodityEx.Data[Commodity.Water].Mass);
    }

    [Fact]
    public void Volume_ReturnsCorrectValue()
    {
        Commodity.Scrap.Volume().Should().Be(CommodityEx.Data[Commodity.Scrap].Volume);
        Commodity.Water.Volume().Should().Be(CommodityEx.Data[Commodity.Water].Volume);
    }

    [Fact]
    public void Category_ReturnsCorrectCategory()
    {
        Commodity.Scrap.Category().Should().Be(CommodityCategory.Essential);
        Commodity.Fuel.Category().Should().Be(CommodityCategory.Essential);
        Commodity.Ore.Category().Should().Be(CommodityCategory.Raw);
        Commodity.Metal.Category().Should().Be(CommodityCategory.Refined);
        Commodity.Alloys.Category().Should().Be(CommodityCategory.Parts);
        Commodity.Liquor.Category().Should().Be(CommodityCategory.Vice);
    }

    [Fact]
    public void Tier_ReturnsCorrectTier()
    {
        Commodity.Scrap.Tier().Should().Be(GameTier.Early);
        Commodity.Isotopes.Tier().Should().Be(GameTier.Mid);
        Commodity.Nanomaterials.Tier().Should().Be(GameTier.Late);
    }

    [Fact]
    public void Flags_ReturnsCorrectFlags()
    {
        Commodity.Crew.Flags().Should().HaveFlag(CommodityFlag.Integral);
        Commodity.Rations.Flags().Should().HaveFlag(CommodityFlag.Perishable);
        Commodity.Water.Flags().Should().HaveFlag(CommodityFlag.Bulky);
    }

    [Fact]
    public void IsPerishable_ReturnsCorrectly()
    {
        Commodity.Rations.IsPerishable().Should().BeTrue();
        Commodity.Biomass.IsPerishable().Should().BeTrue();
        Commodity.Scrap.IsPerishable().Should().BeFalse();
        Commodity.Metal.IsPerishable().Should().BeFalse();
    }

    [Fact]
    public void IsBulky_ReturnsCorrectly()
    {
        Commodity.Water.IsBulky().Should().BeTrue();
        Commodity.Ore.IsBulky().Should().BeTrue();
        Commodity.Scrap.IsBulky().Should().BeFalse();
        Commodity.Gems.IsBulky().Should().BeFalse();
    }

    [Fact]
    public void IsEssential_ReturnsCorrectly()
    {
        Commodity.Scrap.IsEssential().Should().BeTrue();
        Commodity.Fuel.IsEssential().Should().BeTrue();
        Commodity.Crew.IsEssential().Should().BeTrue();
        Commodity.Water.IsEssential().Should().BeTrue();

        Commodity.Ore.IsEssential().Should().BeFalse();
        Commodity.Metal.IsEssential().Should().BeFalse();
    }

    [Fact]
    public void IsIntegral_ReturnsCorrectly()
    {
        Commodity.Crew.IsIntegral().Should().BeTrue();
        Commodity.Morale.IsIntegral().Should().BeTrue();
        Commodity.Idols.IsIntegral().Should().BeTrue();

        Commodity.Scrap.IsIntegral().Should().BeFalse();
        Commodity.Fuel.IsIntegral().Should().BeFalse();
    }

    [Fact]
    public void IsAmmunition_ReturnsCorrectly()
    {
        Commodity.Slugs.IsAmmunition().Should().BeTrue();
        Commodity.Cells.IsAmmunition().Should().BeTrue();
        Commodity.Rockets.IsAmmunition().Should().BeTrue();

        Commodity.Scrap.IsAmmunition().Should().BeFalse();
        Commodity.Metal.IsAmmunition().Should().BeFalse();
    }

    [Fact]
    public void IsIndustrial_ReturnsCorrectly()
    {
        Commodity.Lubricants.IsIndustrial().Should().BeTrue();
        Commodity.Coolant.IsIndustrial().Should().BeTrue();
        Commodity.SpareParts.IsIndustrial().Should().BeTrue();

        Commodity.Scrap.IsIndustrial().Should().BeFalse();
    }

    [Fact]
    public void IsWaste_ReturnsCorrectly()
    {
        Commodity.Slag.IsWaste().Should().BeTrue();

        Commodity.Scrap.IsWaste().Should().BeFalse();
        Commodity.Ore.IsWaste().Should().BeFalse();
    }

    // Rounding tests
    [Theory]
    [InlineData(Commodity.Scrap, 1.5f)]
    [InlineData(Commodity.Fuel, 10.123456f)]
    [InlineData(Commodity.Water, 0.001f)]
    public void Round_NonIntegral_RoundsTo512ths(Commodity commodity, float value)
    {
        var rounded = commodity.Round(value);

        // Check that rounded * 512 is very close to an integer
        var scaled = rounded * 512;
        var remainder = scaled - Math.Round(scaled);

        remainder.Should().BeApproximately(0, 0.0001f);
    }

    [Theory]
    [InlineData(Commodity.Crew, 1.3f, 1f)]
    [InlineData(Commodity.Crew, 1.7f, 2f)]
    [InlineData(Commodity.Crew, 2.5f, 2f)] // Banker's rounding (to nearest even)
    [InlineData(Commodity.Morale, 5.4f, 5f)]
    public void Round_Integral_RoundsToWholeNumber(Commodity commodity, float input, float expected)
    {
        commodity.IsIntegral().Should().BeTrue("test precondition");

        var result = commodity.Round(input);

        result.Should().Be(expected);
    }

    // Category groupings
    [Fact]
    public void AllCategories_HaveAtLeastOneCommodity()
    {
        var categoryCounts = Enum.GetValues<Commodity>()
            .GroupBy(c => c.Category())
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (CommodityCategory category in Enum.GetValues<CommodityCategory>())
        {
            categoryCounts.Should().ContainKey(category, $"category {category} should have commodities");
            categoryCounts[category].Should().BeGreaterThan(0);
        }
    }

    // Value consistency tests
    [Fact]
    public void LateGameCommodities_AreMoreValuable()
    {
        var earlyAvg = Enum.GetValues<Commodity>()
            .Where(c => c.Tier() == GameTier.Early)
            .Average(c => CommodityEx.Data[c].InitialValue);

        var lateAvg = Enum.GetValues<Commodity>()
            .Where(c => c.Tier() == GameTier.Late)
            .Average(c => CommodityEx.Data[c].InitialValue);

        lateAvg.Should().BeGreaterThan(earlyAvg, "late game commodities should be more valuable on average");
    }

    [Fact]
    public void Ammunition_HasAmmunitionCategory()
    {
        Commodity.Slugs.Category().Should().Be(CommodityCategory.Ammunition);
        Commodity.Cells.Category().Should().Be(CommodityCategory.Ammunition);
        Commodity.Rockets.Category().Should().Be(CommodityCategory.Ammunition);
    }

    [Fact]
    public void Industrial_HasIndustrialCategory()
    {
        Commodity.Lubricants.Category().Should().Be(CommodityCategory.Industrial);
        Commodity.Coolant.Category().Should().Be(CommodityCategory.Industrial);
        Commodity.SpareParts.Category().Should().Be(CommodityCategory.Industrial);
    }
}

public class CommodityFlagTests
{
    [Fact]
    public void Flags_CanBeCombined()
    {
        var combined = CommodityFlag.Perishable | CommodityFlag.Bulky;

        combined.HasFlag(CommodityFlag.Perishable).Should().BeTrue();
        combined.HasFlag(CommodityFlag.Bulky).Should().BeTrue();
        combined.HasFlag(CommodityFlag.Integral).Should().BeFalse();
    }

    [Fact]
    public void Biomass_HasMultipleFlags()
    {
        var flags = Commodity.Biomass.Flags();

        flags.HasFlag(CommodityFlag.Perishable).Should().BeTrue();
        flags.HasFlag(CommodityFlag.Bulky).Should().BeTrue();
    }
}
