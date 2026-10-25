using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class TraderCargoOfferTests
{
    [Fact]
    public void MakeTradeOffers_SellsCarriedGoodsAndBidsOnImports()
    {
        var game = Game.NewSimulation(42, 2);
        var location = game.Map.AllLocations.First(l => l.HasEncounter);
        var trader = location.GetEncounter().GeneratePlayerActor(7);
        trader.Inventory[Commodity.Relics, Account.Trade] += 3;

        var offers = trader.MakeTradeOffers(11, 1.0f);

        offers.Should().Contain(o => o.Commodity == Commodity.Relics && o.Direction == TradeDirection.Sell,
            "goods a trader carries are for sale wherever it is");
        var askedFor = offers.Where(o => o.Direction == TradeDirection.Sell && o.IsCommodity)
            .Select(o => o.Commodity!.Value).ToHashSet();
        offers.Should().Contain(o => o.IsCommodity && o.Direction == TradeDirection.Buy && !askedFor.Contains(o.Commodity!.Value),
            "traders bid on goods they don't stock so hauled goods can be sold");
    }
}
