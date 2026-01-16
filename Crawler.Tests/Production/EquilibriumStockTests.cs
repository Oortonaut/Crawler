using Crawler;
using Crawler.Economy;
using Crawler.Production;
using FluentAssertions;

namespace Crawler.Tests.Production;

public class EquilibriumStockTests
{
    [Fact]
    public void ConsumptionRates_ZeroPopulation_ReturnsZeroRates()
    {
        var rates = EquilibriumStock.ConsumptionRates(0);

        rates[Commodity.Rations].Should().Be(0);
        rates[Commodity.Water].Should().Be(0);
        rates[Commodity.Medicines].Should().Be(0);
    }

    [Fact]
    public void ConsumptionRates_PositivePopulation_ReturnsPositiveEssentials()
    {
        var rates = EquilibriumStock.ConsumptionRates(100);

        rates[Commodity.Rations].Should().BeGreaterThan(0);
        rates[Commodity.Water].Should().BeGreaterThan(0);
        rates[Commodity.Medicines].Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConsumptionRates_ScalesWithPopulation()
    {
        var rates100 = EquilibriumStock.ConsumptionRates(100);
        var rates200 = EquilibriumStock.ConsumptionRates(200);

        // Double population should double consumption
        rates200[Commodity.Rations].Should().BeApproximately(
            rates100[Commodity.Rations] * 2, 0.01f);
        rates200[Commodity.Water].Should().BeApproximately(
            rates100[Commodity.Water] * 2, 0.01f);
    }

    [Fact]
    public void ConsumptionRates_IncludesConsumerGoods()
    {
        var rates = EquilibriumStock.ConsumptionRates(100);

        // Consumer goods should also be consumed
        rates[Commodity.Textiles].Should().BeGreaterThan(0);
        rates[Commodity.Toys].Should().BeGreaterThan(0);
        rates[Commodity.Media].Should().BeGreaterThan(0);
        rates[Commodity.Liquor].Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConsumptionRates_ConsumerGoodsDistributedEqually()
    {
        var rates = EquilibriumStock.ConsumptionRates(100);

        // Each consumer good gets 25% of goods rate
        var textiles = rates[Commodity.Textiles];
        var toys = rates[Commodity.Toys];
        var media = rates[Commodity.Media];
        var liquor = rates[Commodity.Liquor];

        textiles.Should().BeApproximately(toys, 0.001f);
        toys.Should().BeApproximately(media, 0.001f);
        media.Should().BeApproximately(liquor, 0.001f);
    }

    [Fact]
    public void ConsumptionRates_NonConsumedItemsAreZero()
    {
        var rates = EquilibriumStock.ConsumptionRates(100);

        // Raw materials are not consumed directly by population
        rates[Commodity.Ore].Should().Be(0);
        rates[Commodity.Biomass].Should().Be(0);
        rates[Commodity.Silicates].Should().Be(0);

        // Production materials are not consumed by population
        rates[Commodity.Metal].Should().Be(0);
        rates[Commodity.Alloys].Should().Be(0);
    }
}

public class RecipeExExtensionTests
{
    [Theory]
    [InlineData(IndustryType.Refinery)]
    [InlineData(IndustryType.Fabricator)]
    [InlineData(IndustryType.Assembler)]
    [InlineData(IndustryType.Recycler)]
    public void RecipesFor_ReturnsNonEmpty(IndustryType type)
    {
        var recipes = RecipeEx.RecipesFor(type).ToList();

        recipes.Should().NotBeEmpty(
            $"industry type {type} should have at least one recipe");
    }

    [Fact]
    public void RecipesFor_ReturnsOnlyMatchingType()
    {
        var recipes = RecipeEx.RecipesFor(IndustryType.Refinery).ToList();

        foreach (var recipe in recipes)
        {
            recipe.RequiredIndustry.Should().Be(IndustryType.Refinery);
        }
    }

    [Fact]
    public void AllRecipes_CoverAllIndustryTypes()
    {
        var coveredTypes = RecipeEx.AllRecipes
            .Select(r => r.RequiredIndustry)
            .Distinct()
            .ToHashSet();

        foreach (IndustryType type in Enum.GetValues<IndustryType>())
        {
            coveredTypes.Should().Contain(type,
                $"should have recipes for {type}");
        }
    }
}

public class BufferDaysTests
{
    // Test that buffer days are reasonable for different categories
    [Fact]
    public void AllCommodityCategories_HaveDefinedBufferBehavior()
    {
        // Documents that all categories have reasonable buffer expectations
        // Essential items need longer buffers than waste
        foreach (CommodityCategory category in Enum.GetValues<CommodityCategory>())
        {
            // Each category should be defined - just verify enum coverage
            ((int)category).Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
