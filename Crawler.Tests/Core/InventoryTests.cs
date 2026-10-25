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

        inventory.Contains(Commodity.Scrap, 50).Should().BeTrue();
    }

    [Fact]
    public void Contains_ReturnsFalse_WhenInsufficient()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 25);

        inventory.Contains(Commodity.Scrap, 50).Should().BeFalse();
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

        inventory.Contains(required).Should().BeTrue();
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

        inventory.Contains(required).Should().BeFalse();
    }

    // Account tests
    [Fact]
    public void Read_SumsEveryStackServingTheMask()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 10, Account.Supplies | Account.Trade);
        inventory.Add(Commodity.Fuel, 20, Account.Trade);
        inventory.Add(Commodity.Fuel, 40, Account.Cargo);

        inventory[Commodity.Fuel, Account.Supplies].Should().Be(10);
        inventory[Commodity.Fuel, Account.Trade].Should().Be(30);
        inventory[Commodity.Fuel, Account.Cargo].Should().Be(40);
        inventory[Commodity.Fuel, Account.All].Should().Be(70);
    }

    [Fact]
    public void Remove_DrawsDedicatedStacksBeforeSharedOnes()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 30, Account.Supplies);
        inventory.Add(Commodity.Fuel, 100, Account.Supplies | Account.Trade);

        inventory.Remove(Commodity.Fuel, 50);

        inventory.Stack(Commodity.Fuel, Account.Supplies).Should().Be(0);
        inventory.Stack(Commodity.Fuel, Account.Supplies | Account.Trade).Should().Be(80);
    }

    [Fact]
    public void Remove_NeverTouchesStacksOutsideTheMask()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 30, Account.Trade);
        inventory.Add(Commodity.Fuel, 30, Account.Cargo);

        inventory.Remove(Commodity.Fuel, 50, Account.Supplies);
        inventory[Commodity.Fuel, Account.All].Should().Be(60);

        inventory.Remove(Commodity.Fuel, 50, Account.Trade);
        inventory.Stack(Commodity.Fuel, Account.Trade).Should().Be(0);
        inventory.Stack(Commodity.Fuel, Account.Cargo).Should().Be(30);
    }

    [Fact]
    public void Indexer_DepositsIntoExactStack_WithdrawsByMask()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 30, Account.Supplies | Account.Trade);

        inventory[Commodity.Scrap] += 10;
        inventory.Stack(Commodity.Scrap, Account.Supplies).Should().Be(10);

        inventory[Commodity.Scrap] -= 25;
        inventory.Stack(Commodity.Scrap, Account.Supplies).Should().Be(0);
        inventory.Stack(Commodity.Scrap, Account.Supplies | Account.Trade).Should().Be(15);
    }

    [Fact]
    public void Move_TransfersBetweenExactStacks_CappedAtWhatIsThere()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 30, Account.Supplies);

        var moved = inventory.Move(Commodity.Fuel, 50, Account.Supplies, Account.Cargo);

        moved.Should().Be(30);
        inventory.Stack(Commodity.Fuel, Account.Supplies).Should().Be(0);
        inventory.Stack(Commodity.Fuel, Account.Cargo).Should().Be(30);
    }

    [Fact]
    public void Contains_Inventory_OnlyCountsStacksServingTheMask()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 30);
        inventory.Add(Commodity.Scrap, 100, Account.Cargo);

        var required = new Inventory();
        required.Add(Commodity.Scrap, 50);

        inventory.Contains(required).Should().BeFalse();
        inventory.Contains(required, Account.All).Should().BeTrue();
    }

    [Fact]
    public void Remove_Inventory_DrawsAcrossStacksServingTheMask()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Scrap, 30);
        inventory.Add(Commodity.Scrap, 100, Account.Supplies | Account.Trade);
        inventory.Add(Commodity.Fuel, 50);

        var toRemove = new Inventory();
        toRemove.Add(Commodity.Scrap, 50); // 30 from Supplies, 20 from Supplies|Trade
        toRemove.Add(Commodity.Fuel, 25);

        inventory.Remove(toRemove);

        inventory.Stack(Commodity.Scrap, Account.Supplies).Should().Be(0);
        inventory.Stack(Commodity.Scrap, Account.Supplies | Account.Trade).Should().Be(80);
        inventory[Commodity.Fuel].Should().Be(25);
    }

    [Fact]
    public void Subset_And_RemoveStacks_RoundTrip()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 10, Account.Supplies);
        inventory.Add(Commodity.Fuel, 20, Account.Trade);

        var trade = inventory.Subset(Account.Trade);
        trade.Stack(Commodity.Fuel, Account.Trade).Should().Be(20);
        trade[Commodity.Fuel, Account.Supplies].Should().Be(0);

        inventory.ContainsStacks(trade).Should().BeTrue();
        inventory.RemoveStacks(trade);
        inventory.Stack(Commodity.Fuel, Account.Trade).Should().Be(0);
        inventory.Stack(Commodity.Fuel, Account.Supplies).Should().Be(10);
    }

    [Fact]
    public void Data_RoundTripsStacks()
    {
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 10, Account.Supplies | Account.Trade);
        inventory.Add(Commodity.Fuel, 5, Account.Cargo);

        var restored = new Inventory();
        restored.FromData(inventory.ToData());

        restored.Stack(Commodity.Fuel, Account.Supplies | Account.Trade).Should().Be(10);
        restored.Stack(Commodity.Fuel, Account.Cargo).Should().Be(5);
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
