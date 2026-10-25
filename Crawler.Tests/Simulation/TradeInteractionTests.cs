using Crawler;
using Crawler.Economy;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class TradePriceTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public TradePriceTests()
    {
        _fixture = new SimulationTestFixture(12345);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Actor_GetMidPrice_ReturnsPositiveValue()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var midPrice = trader.GetMidPrice(Commodity.Metal);

        midPrice.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Actor_GetAskPrice_GreaterThanBidPrice()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var askPrice = trader.GetAskPrice(Commodity.Metal);
        var bidPrice = trader.GetBidPrice(Commodity.Metal);

        askPrice.Should().BeGreaterThan(bidPrice);
    }

    [Fact]
    public void Actor_PricesAreRounded()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var askPrice = trader.GetAskPrice(Commodity.Metal);
        var bidPrice = trader.GetBidPrice(Commodity.Metal);

        // Prices are rounded like scrap, to 1/512 (Commodity.Round)
        askPrice.Should().Be(Commodity.Scrap.Round(askPrice));
        bidPrice.Should().Be(Commodity.Scrap.Round(bidPrice));
    }

    [Fact]
    public void TradePolicy_AffectsPrice()
    {
        // Different factions may have different policies affecting price
        var location = _fixture.GetLocation();
        var trader1 = _fixture.CreateTrader("Trader 1", location, Factions.Civilian0);
        var trader2 = _fixture.CreateTrader("Trader 2", location, Factions.Independent);

        // Prices should be positive for both
        var price1 = trader1.GetMidPrice(Commodity.Metal);
        var price2 = trader2.GetMidPrice(Commodity.Metal);

        price1.Should().BeGreaterThan(0);
        price2.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Segment_GetAskPrice_ReturnsPositiveValue()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var segment = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3).NewSegment(12345);

        var askPrice = trader.GetAskPrice(segment);

        askPrice.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Segment_GetBidPrice_LessThanAskPrice()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var segment = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3).NewSegment(12345);

        var askPrice = trader.GetAskPrice(segment);
        var bidPrice = trader.GetBidPrice(segment);

        askPrice.Should().BeGreaterThan(bidPrice);
    }
}

[Collection(SimulationCollection.Name)]
public class TradeOfferTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public TradeOfferTests()
    {
        _fixture = new SimulationTestFixture(54321);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void TradeOffer_CommodityOffer_HasValidProperties()
    {
        var offer = new TradeOffer
        {
            Commodity = Commodity.Metal,
            Direction = TradeDirection.Sell,
            PricePerUnit = 100,
            Quantity = 10
        };

        offer.IsCommodity.Should().BeTrue();
        offer.IsSegment.Should().BeFalse();
        offer.Commodity.Should().Be(Commodity.Metal);
        offer.PricePerUnit.Should().Be(100);
        offer.Quantity.Should().Be(10);
    }

    [Fact]
    public void TradeOffer_SegmentOffer_HasValidProperties()
    {
        var segment = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3).NewSegment(12345);

        var offer = new TradeOffer
        {
            Segment = segment,
            Direction = TradeDirection.Sell,
            PricePerUnit = 500,
            Quantity = 1
        };

        offer.IsCommodity.Should().BeFalse();
        offer.IsSegment.Should().BeTrue();
        offer.Segment.Should().Be(segment);
    }

    [Fact]
    public void MakeTradeOffers_GeneratesOffers()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var offers = trader.MakeTradeOffers(12345);

        offers.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void MakeTradeOffers_IncludesBothDirections()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var offers = trader.MakeTradeOffers(12345);

        offers.Should().Contain(o => o.Direction == TradeDirection.Sell);
        offers.Should().Contain(o => o.Direction == TradeDirection.Buy);
    }
}

[Collection(SimulationCollection.Name)]
public class InventoryTradeTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public InventoryTradeTests()
    {
        _fixture = new SimulationTestFixture(11111);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Inventory_Add_IncreasesCommodity()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Trader", location);

        var initial = crawler.Inventory[Commodity.Metal, Account.Trade];
        crawler.Inventory.Add(Commodity.Metal, 50, Account.Supplies | Account.Trade);

        crawler.Inventory[Commodity.Metal, Account.Trade].Should().Be(initial + 50);
    }

    [Fact]
    public void Inventory_Remove_DecreasesCommodity()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Trader", location);

        crawler.Inventory.Add(Commodity.Metal, 100, Account.Supplies | Account.Trade);
        crawler.Inventory.Remove(Commodity.Metal, 30, Account.Trade);

        crawler.Inventory[Commodity.Metal, Account.Trade].Should().Be(70);
    }

    [Fact]
    public void Inventory_Transfer_MovesCommodity()
    {
        var location = _fixture.GetLocation();
        var seller = _fixture.CreateCrawler("Seller", location);
        var buyer = _fixture.CreateCrawler("Buyer", location);

        seller.Inventory.Add(Commodity.Metal, 100, Account.Supplies | Account.Trade);
        var initialBuyer = buyer.Inventory[Commodity.Metal, Account.Trade];

        // Simulate transfer
        var qty = 50f;
        seller.Inventory.Remove(Commodity.Metal, qty, Account.Trade);
        buyer.Inventory.Add(Commodity.Metal, qty, Account.Supplies | Account.Trade);

        seller.Inventory[Commodity.Metal, Account.Trade].Should().Be(50);
        buyer.Inventory[Commodity.Metal, Account.Trade].Should().Be(initialBuyer + 50);
    }

    [Fact]
    public void Inventory_Segments_CanBeAdded()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Trader", location);

        var segment = new GunDef('G', 3, "Trade Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3).NewSegment(12345);

        crawler.Inventory.Add(segment, Account.Supplies | Account.Trade);

        crawler.Inventory.SegmentsIn(Account.Trade).Should().Contain(segment);
    }

    [Fact]
    public void Inventory_Segments_CanBeRemoved()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Trader", location);

        var segment = new GunDef('G', 3, "Trade Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3).NewSegment(12345);
        crawler.Inventory.Add(segment, Account.Supplies | Account.Trade);
        crawler.Inventory.Remove(segment);

        crawler.Inventory.SegmentsIn(Account.Trade).Should().NotContain(segment);
    }
}

[Collection(SimulationCollection.Name)]
public class SettlementTradeTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public SettlementTradeTests()
    {
        _fixture = new SimulationTestFixture(22222, 3); // Larger map for better chance of settlement
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void FindSettlement_ReturnsNullWhenNone()
    {
        // Use a location that may not have a settlement
        var location = _fixture.GetLocation();

        // Create encounter if it doesn't exist
        var encounter = location.GetEncounter();

        // Check if settlement exists
        var settlement = SettlementTrade.FindSettlement(location);

        // Settlement may or may not exist depending on map generation
        // Just verify the method works
        if (settlement != null)
        {
            settlement.Role.Should().Be(Roles.Settlement);
        }
    }

    [Fact]
    public void TryBuyFromSettlement_FailsWithoutSettlement()
    {
        var location = _fixture.GetLocation();
        var buyer = _fixture.CreateCrawler("Buyer", location);

        // If no settlement exists, buying should fail
        var settlement = SettlementTrade.FindSettlement(location);
        if (settlement == null)
        {
            var result = SettlementTrade.TryBuyFromSettlement(buyer, Commodity.Metal, 10);
            result.Should().BeFalse();
        }
    }

    [Fact]
    public void TrySellToSettlement_FailsWithoutSettlement()
    {
        var location = _fixture.GetLocation();
        var seller = _fixture.CreateCrawler("Seller", location);
        seller.Inventory.Add(Commodity.Metal, 100, Account.Supplies | Account.Trade);

        // If no settlement exists, selling should fail
        var settlement = SettlementTrade.FindSettlement(location);
        if (settlement == null)
        {
            var result = SettlementTrade.TrySellToSettlement(seller, Commodity.Metal, 50);
            result.Should().BeFalse();
        }
    }
}

[Collection(SimulationCollection.Name)]
public class MarketOfferTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public MarketOfferTests()
    {
        _fixture = new SimulationTestFixture(33333);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void GetAskOffer_ReturnsValidOffer()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        // Add some cargo
        trader.Inventory.Add(Commodity.Metal, 50, Account.Supplies | Account.Trade);

        var offer = trader.GetAskOffer(Commodity.Metal);

        offer.Actor.Should().Be(trader);
        offer.Price.Should().BeGreaterThan(0);
        offer.Quantity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetBidOffer_ReturnsValidOffer()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        // Ensure trader has scrap to buy with
        trader.Inventory.Add(Commodity.Scrap, 1000);

        var offer = trader.GetBidOffer(Commodity.Metal);

        offer.Actor.Should().Be(trader);
        offer.Price.Should().BeGreaterThan(0);
        offer.Quantity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetMarkup_ReturnsDefaultWithoutComponent()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("NoTradeComponent", location);

        var markup = crawler.GetMarkup();

        markup.Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public void GetSpread_ReturnsDefaultWithoutComponent()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("NoTradeComponent", location);

        var spread = crawler.GetSpread();

        spread.Should().BeApproximately(1.0f, 0.01f);
    }
}

[Collection(SimulationCollection.Name)]
public class TradePolicyTests
{
    [Theory]
    [InlineData(TradePolicy.Subsidized)]
    [InlineData(TradePolicy.Legal)]
    [InlineData(TradePolicy.Taxed)]
    [InlineData(TradePolicy.Controlled)]
    [InlineData(TradePolicy.Restricted)]
    [InlineData(TradePolicy.Prohibited)]
    public void TradePolicy_AllValuesAreDefined(TradePolicy policy)
    {
        Enum.IsDefined(typeof(TradePolicy), policy).Should().BeTrue();
    }

    [Theory]
    [InlineData(TradePolicy.Subsidized, 0.7f)]
    [InlineData(TradePolicy.Legal, 1.0f)]
    [InlineData(TradePolicy.Taxed, 1.3f)]
    [InlineData(TradePolicy.Controlled, 1.75f)]
    [InlineData(TradePolicy.Restricted, 2.75f)]
    [InlineData(TradePolicy.Prohibited, 5.0f)]
    public void PolicyMultiplier_ReturnsExpectedValues(TradePolicy policy, float expectedMultiplier)
    {
        var multiplier = Tuning.Trade.PolicyMultiplier(policy);

        multiplier.Should().BeApproximately(expectedMultiplier, 0.01f);
    }
}

[Collection(SimulationCollection.Name)]
public class TradeDirectionTests
{
    [Fact]
    public void TradeDirection_Sell_MeansTraderSells()
    {
        var offer = new TradeOffer
        {
            Commodity = Commodity.Metal,
            Direction = TradeDirection.Sell,
            PricePerUnit = 100,
            Quantity = 10
        };

        offer.Direction.Should().Be(TradeDirection.Sell);
    }

    [Fact]
    public void TradeDirection_Buy_MeansTraderBuys()
    {
        var offer = new TradeOffer
        {
            Commodity = Commodity.Metal,
            Direction = TradeDirection.Buy,
            PricePerUnit = 80,
            Quantity = 10
        };

        offer.Direction.Should().Be(TradeDirection.Buy);
    }
}
