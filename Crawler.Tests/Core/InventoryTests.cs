using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Core;

public class InventoryTests
{
    [Fact]
    public void NewInventory_IsEmpty()
    {
        var inventory = new Inventory();

        inventory.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Add_Commodity_IncreasesCount()
    {
        var inventory = new Inventory();

        inventory.Add(Commodity.Scrap, 100);

        inventory[Commodity.Scrap].Should().Be(100);
    }

    [Fact]
    public void Add_MultipleTimes_Accumulates()
    {
        var inventory = new Inventory();

        inventory.Add(Commodity.Scrap, 50);
        inventory.Add(Commodity.Scrap, 30);

        inventory[Commodity.Scrap].Should().Be(80);
    }

    [Fact]
    public void Remove_Commodity_DecreasesCount()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);

        inventory.Remove(Commodity.Scrap, 40);

        inventory[Commodity.Scrap].Should().Be(60);
    }

    [Fact]
    public void Remove_MoreThanAvailable_GoesToZero()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 50);

        inventory.Remove(Commodity.Scrap, 100);

        inventory[Commodity.Scrap].Should().Be(0);
    }

    [Fact]
    public void Indexer_Set_UpdatesValue()
    {
        var inventory = new Inventory();

        inventory[Commodity.Fuel] = 75;

        inventory[Commodity.Fuel].Should().Be(75);
    }

    [Fact]
    public void Indexer_Set_NegativeBecomesZero()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);

        inventory[Commodity.Scrap] = -50;

        inventory[Commodity.Scrap].Should().Be(0);
    }

    [Fact]
    public void IsEmpty_FalseWhenHasCommodities()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 1);

        inventory.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesAllCommodities()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);
        inventory.Add(Commodity.Fuel, 50);

        inventory.Clear();

        inventory.IsEmpty.Should().BeTrue();
        inventory[Commodity.Scrap].Should().Be(0);
        inventory[Commodity.Fuel].Should().Be(0);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new Inventory();
        original.Add(Commodity.Scrap, 100);

        var clone = original.Clone();
        clone.Add(Commodity.Scrap, 50);

        original[Commodity.Scrap].Should().Be(100, "original should be unchanged");
        clone[Commodity.Scrap].Should().Be(150);
    }

    [Fact]
    public void Add_Inventory_CombinesCommodities()
    {
        var inventory1 = new Inventory();
        inventory1.Add(Commodity.Scrap, 100);
        inventory1.Add(Commodity.Fuel, 50);

        var inventory2 = new Inventory();
        inventory2.Add(Commodity.Scrap, 25);
        inventory2.Add(Commodity.Water, 10);

        inventory1.Add(inventory2);

        inventory1[Commodity.Scrap].Should().Be(125);
        inventory1[Commodity.Fuel].Should().Be(50);
        inventory1[Commodity.Water].Should().Be(10);
    }

    [Fact]
    public void Mass_CalculatesFromCommodities()
    {
        var inventory = new Inventory();

        // Add commodities with known masses
        inventory.Add(Commodity.Scrap, 100); // 0.005 mass each = 0.5
        inventory.Add(Commodity.Crew, 2);    // 0.08 mass each = 0.16

        var expectedMass = 100 * CommodityEx.Data[Commodity.Scrap].Mass +
                           2 * CommodityEx.Data[Commodity.Crew].Mass;

        inventory.Mass.Should().BeApproximately(expectedMass, 0.001f);
    }

    [Fact]
    public void Volume_CalculatesFromCommodities()
    {
        var inventory = new Inventory();

        inventory.Add(Commodity.Scrap, 100);
        inventory.Add(Commodity.Crew, 2);

        var expectedVolume = 100 * CommodityEx.Data[Commodity.Scrap].Volume +
                             2 * CommodityEx.Data[Commodity.Crew].Volume;

        inventory.Volume.Should().BeApproximately(expectedVolume, 0.001f);
    }

    [Fact]
    public void Contains_ReturnsTrue_WhenSufficient()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);

        var result = inventory.Contains(Commodity.Scrap, 50);

        result.Should().Be(FromInventory.Primary);
    }

    [Fact]
    public void Contains_ReturnsFalse_WhenInsufficient()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 25);

        var result = inventory.Contains(Commodity.Scrap, 50);

        result.Should().Be(FromInventory.None);
    }

    [Fact]
    public void Contains_Inventory_ChecksAllCommodities()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);
        inventory.Add(Commodity.Fuel, 50);

        var required = new Inventory();
        required.Add(Commodity.Scrap, 50);
        required.Add(Commodity.Fuel, 25);

        inventory.Contains(required).Should().Be(FromInventory.Primary);
    }

    [Fact]
    public void Contains_Inventory_FailsIfAnyCommodityInsufficient()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);
        inventory.Add(Commodity.Fuel, 10);

        var required = new Inventory();
        required.Add(Commodity.Scrap, 50);
        required.Add(Commodity.Fuel, 25); // More than available

        inventory.Contains(required).Should().Be(FromInventory.None);
    }

    // Overdraft tests
    [Fact]
    public void Overdraft_FulfillsDeficit()
    {
        var primary = new Inventory();
        primary.Add(Commodity.Scrap, 30);

        var overdraft = new Inventory();
        overdraft.Add(Commodity.Scrap, 100);

        primary.Overdraft = overdraft;

        // Remove more than primary has
        primary.Remove(Commodity.Scrap, 50);

        primary[Commodity.Scrap].Should().Be(0);
        overdraft[Commodity.Scrap].Should().Be(80); // 100 - 20 (deficit)
    }

    [Fact]
    public void Overdraft_Contains_ReturnsOverdraft()
    {
        var primary = new Inventory();
        primary.Add(Commodity.Scrap, 30);

        var overdraft = new Inventory();
        overdraft.Add(Commodity.Scrap, 100);

        primary.Overdraft = overdraft;

        var result = primary.Contains(Commodity.Scrap, 50);

        result.Should().Be(FromInventory.Overdraft);
    }

    [Fact]
    public void Overdraft_Chain_WorksRecursively()
    {
        var primary = new Inventory();
        primary.Add(Commodity.Scrap, 10);

        var secondary = new Inventory();
        secondary.Add(Commodity.Scrap, 20);

        var tertiary = new Inventory();
        tertiary.Add(Commodity.Scrap, 100);

        primary.Overdraft = secondary;
        secondary.Overdraft = tertiary;

        // Need 50, have 10+20+100 available
        var result = primary.Contains(Commodity.Scrap, 50);

        result.Should().Be(FromInventory.Overdraft);
    }

    [Fact]
    public void Remove_Inventory_UsesPrimaryFirst_ThenOverdraft()
    {
        var primary = new Inventory();
        primary.Add(Commodity.Scrap, 30);
        primary.Add(Commodity.Fuel, 50);

        var overdraft = new Inventory();
        overdraft.Add(Commodity.Scrap, 100);

        primary.Overdraft = overdraft;

        var toRemove = new Inventory();
        toRemove.Add(Commodity.Scrap, 50); // 30 from primary, 20 from overdraft
        toRemove.Add(Commodity.Fuel, 25);  // all from primary

        primary.Remove(toRemove);

        primary[Commodity.Scrap].Should().Be(0);
        primary[Commodity.Fuel].Should().Be(25);
        overdraft[Commodity.Scrap].Should().Be(80);
    }

    [Fact]
    public void SetOverdraft_ReturnsSelf_ForChaining()
    {
        var primary = new Inventory();
        var overdraft = new Inventory();

        var result = primary.SetOverdraft(overdraft);

        result.Should().BeSameAs(primary);
        primary.Overdraft.Should().BeSameAs(overdraft);
    }

    // Capacity tests
    [Fact]
    public void MaxVolume_DefaultIsUnlimited()
    {
        var inventory = new Inventory();

        inventory.MaxVolume.Should().Be(float.MaxValue);
    }

    [Fact]
    public void AvailableVolume_CalculatesRemaining()
    {
        var inventory = new Inventory();
        inventory.MaxVolume = 100;
        inventory.Add(Commodity.Scrap, 1000); // Some volume used

        inventory.AvailableVolume.Should().BeLessThan(100);
    }

    [Fact]
    public void CanFit_Commodity_ReturnsTrue_WhenEnoughSpace()
    {
        var inventory = new Inventory();
        inventory.MaxVolume = 100;

        // Scrap has 0.001 volume per unit
        inventory.CanFit(Commodity.Scrap, 1000).Should().BeTrue();
    }

    [Fact]
    public void CanFit_Commodity_ReturnsFalse_WhenInsufficientSpace()
    {
        var inventory = new Inventory();
        inventory.MaxVolume = 1;

        // Water has 1.0 volume per unit
        inventory.CanFit(Commodity.Water, 10).Should().BeFalse();
    }

    [Fact]
    public void VolumeUtilization_CalculatesCorrectly()
    {
        var inventory = new Inventory();
        inventory.MaxVolume = 100;

        // Add items until roughly half full
        // Water has 1.0 volume per unit
        inventory.Add(Commodity.Water, 50);

        inventory.VolumeUtilization.Should().BeApproximately(0.5f, 0.01f);
    }

    // Rounding tests
    [Fact]
    public void Commodity_RoundingApplied()
    {
        var inventory = new Inventory();

        // Scrap is not integral, should round to 512ths
        inventory.Add(Commodity.Scrap, 1.001f);

        // Value should be rounded
        var value = inventory[Commodity.Scrap];
        (value * 512 % 1).Should().BeApproximately(0, 0.0001f);
    }

    [Fact]
    public void IntegralCommodity_RoundsToInteger()
    {
        var inventory = new Inventory();

        // Crew is integral
        inventory.Add(Commodity.Crew, 2.7f);

        inventory[Commodity.Crew].Should().Be(3);
    }

    // Brief/ToString tests
    [Fact]
    public void Brief_ReturnsNothing_WhenEmpty()
    {
        var inventory = new Inventory();

        inventory.Brief().Should().Be("nothing");
    }

    [Fact]
    public void Brief_ListsCommodities()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 100);

        var brief = inventory.Brief();

        brief.Should().NotBe("nothing");
    }

    // Serialization tests
    [Fact]
    public void ToData_FromData_RoundTrip()
    {
        var original = new Inventory();
        original.Add(Commodity.Scrap, 100);
        original.Add(Commodity.Fuel, 50);

        var data = original.ToData();
        var restored = new Inventory();
        restored.FromData(data);

        restored[Commodity.Scrap].Should().Be(100);
        restored[Commodity.Fuel].Should().Be(50);
    }
}
