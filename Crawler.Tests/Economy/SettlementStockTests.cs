using Crawler;
using Crawler.Economy;
using FluentAssertions;

namespace Crawler.Tests.Economy;

public class SettlementStockTests
{
    [Theory]
    [InlineData(100f, 100f, 1.0f)]   // Baseline equals current = 1.0x
    [InlineData(100f, 50f, 2.0f)]    // Half stock = 2.0x price (scarcity)
    [InlineData(100f, 200f, 0.5f)]   // Double stock = 0.5x price (surplus)
    [InlineData(100f, 25f, 2.0f)]    // Quarter stock = capped at 2.0x
    [InlineData(100f, 400f, 0.5f)]   // Quadruple stock = capped at 0.5x
    public void PriceMultiplier_RespondsToSupplyDemand(float baseline, float current, float expected)
    {
        var stock = new SettlementStock();
        stock.SetBaseline(Commodity.Fuel, baseline);

        var result = stock.PriceMultiplier(Commodity.Fuel, current);

        result.Should().BeApproximately(expected, 0.001f);
    }

    [Fact]
    public void PriceMultiplier_ZeroBaseline_ReturnsOne()
    {
        var stock = new SettlementStock();
        // Baseline defaults to 0

        var result = stock.PriceMultiplier(Commodity.Fuel, 100f);

        result.Should().Be(1.0f, "zero baseline should return neutral multiplier");
    }

    [Fact]
    public void PriceMultiplier_ZeroStock_ReturnsTwoX()
    {
        var stock = new SettlementStock();
        stock.SetBaseline(Commodity.Fuel, 100f);

        var result = stock.PriceMultiplier(Commodity.Fuel, 0f);

        result.Should().Be(2.0f, "zero stock should return max price multiplier");
    }

    [Fact]
    public void SetBaseline_NegativeValue_ClampsToZero()
    {
        var stock = new SettlementStock();
        stock.SetBaseline(Commodity.Fuel, -50f);

        stock.Baseline(Commodity.Fuel).Should().Be(0);
    }

    [Fact]
    public void Baseline_DefaultsToZero()
    {
        var stock = new SettlementStock();

        foreach (Commodity c in Enum.GetValues<Commodity>())
        {
            stock.Baseline(c).Should().Be(0);
        }
    }

    [Fact]
    public void FromTrade_CopiesTradeStockAsBaseline()
    {
        var cargo = new Inventory();
        cargo.Add(Commodity.Fuel, 100, Account.Supplies | Account.Trade);
        cargo.Add(Commodity.Scrap, 500, Account.Supplies | Account.Trade);
        cargo.Add(Commodity.Water, 25, Account.Supplies | Account.Trade);

        var stock = SettlementStock.FromTrade(cargo);

        stock.Baseline(Commodity.Fuel).Should().Be(100);
        stock.Baseline(Commodity.Scrap).Should().Be(500);
        stock.Baseline(Commodity.Water).Should().Be(25);
    }

    [Fact]
    public void ForPopulation_ScalesEssentials()
    {
        var cargo = new Inventory();
        // Empty cargo - baselines will come from population scaling

        var stock = SettlementStock.ForPopulation(1000, cargo);

        // popFactor = 1000/100 = 10
        // Rations: popFactor * 50 = 500
        // Water: popFactor * 25 = 250
        // Medicines: popFactor * 5 = 50
        // Fuel: 100 + popFactor * 10 = 200
        // SpareParts: 50 + popFactor * 5 = 100

        stock.Baseline(Commodity.Rations).Should().Be(500);
        stock.Baseline(Commodity.Water).Should().Be(250);
        stock.Baseline(Commodity.Medicines).Should().Be(50);
        stock.Baseline(Commodity.Fuel).Should().Be(200);
        stock.Baseline(Commodity.SpareParts).Should().Be(100);
    }

    [Fact]
    public void ForPopulation_UsesTradeStockIfHigher()
    {
        var cargo = new Inventory();
        cargo.Add(Commodity.Rations, 1000, Account.Supplies | Account.Trade); // Higher than scaled value

        var stock = SettlementStock.ForPopulation(100, cargo);

        // popFactor = 1 -> Rations would be 50, but cargo is 1000
        stock.Baseline(Commodity.Rations).Should().Be(1000);
    }

    [Fact]
    public void InitializePrices_SetsInitialValues()
    {
        var stock = new SettlementStock();
        stock.InitializePrices();

        foreach (Commodity c in Enum.GetValues<Commodity>())
        {
            stock.Price(c).Should().Be(CommodityEx.Data[c].InitialValue);
        }
    }

    [Fact]
    public void UpdatePrices_AdjustsBasedOnStock()
    {
        var stock = new SettlementStock();
        stock.SetBaseline(Commodity.Fuel, 100);

        var cargo = new Inventory();
        cargo.Add(Commodity.Fuel, 50, Account.Supplies | Account.Trade); // Half stock

        stock.UpdatePrices(cargo);

        // Price should be InitialValue * 2.0 (due to half stock)
        var expectedPrice = CommodityEx.Data[Commodity.Fuel].InitialValue * 2.0f;
        stock.Price(Commodity.Fuel).Should().BeApproximately(expectedPrice, 0.01f);
    }

    [Fact]
    public void UpdatePrices_SurplusLowersPrices()
    {
        var stock = new SettlementStock();
        stock.SetBaseline(Commodity.Fuel, 100);

        var cargo = new Inventory();
        cargo.Add(Commodity.Fuel, 200, Account.Supplies | Account.Trade); // Double stock

        stock.UpdatePrices(cargo);

        // Price should be InitialValue * 0.5 (due to surplus)
        var expectedPrice = CommodityEx.Data[Commodity.Fuel].InitialValue * 0.5f;
        stock.Price(Commodity.Fuel).Should().BeApproximately(expectedPrice, 0.01f);
    }

    [Fact]
    public void MarkPriceUpdate_TracksTime()
    {
        var stock = new SettlementStock();
        var time = new TimePoint(1000);

        stock.MarkPriceUpdate(time);

        stock.LastPriceUpdate.Should().Be(time);
    }
}
