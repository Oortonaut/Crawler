using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

/// <summary>
/// Trades move goods and scrap between inventories without creating or destroying them,
/// and restocking a merchant tops it up instead of piling stock on every price refresh.
/// </summary>
[Collection(SimulationCollection.Name)]
public class TradeConservationTests
{
    readonly Encounter _encounter;
    readonly Crawler _settlement;
    readonly Crawler _player;

    public TradeConservationTests()
    {
        var game = Game.NewSimulation(42, 2);
        var location = game.Map.AllLocations.First(l => l.HasEncounter && l.GetEncounter().Settlements.Any());
        _encounter = location.GetEncounter();
        _settlement = _encounter.Settlements.OfType<Crawler>().First();
        _player = _encounter.GeneratePlayerActor(7);
        _encounter.AddActor(_player);
    }

    static float Total(IActor actor, Commodity commodity) => actor.Inventory[commodity, Account.All];

    [Fact]
    public void SellingGoods_MovesThemAndTheScrap_ExactlyOnce()
    {
        _player.Inventory[Commodity.Relics, Account.Trade] += 5;
        var (relicsBefore, scrapBefore) = (Total(_player, Commodity.Relics) + Total(_settlement, Commodity.Relics),
                                           Total(_player, Commodity.Scrap) + Total(_settlement, Commodity.Scrap));
        var playerRelics = Total(_player, Commodity.Relics);
        var goods = new GoodsOffer(Commodity.Relics, 5);
        var pay = new ScrapOffer(100);
        goods.DisabledFor(_player, _settlement).Should().BeNull();
        pay.DisabledFor(_settlement, _player).Should().BeNull();

        goods.PerformOn(_player, _settlement);
        pay.PerformOn(_settlement, _player);

        Total(_player, Commodity.Relics).Should().Be(playerRelics - 5, "sold goods leave the seller");
        (Total(_player, Commodity.Relics) + Total(_settlement, Commodity.Relics)).Should().Be(relicsBefore);
        (Total(_player, Commodity.Scrap) + Total(_settlement, Commodity.Scrap)).Should().BeApproximately(scrapBefore, 0.01f);
    }

    [Fact]
    public void Exchange_StopsWhenASideRunsOut_AndSaysWhy()
    {
        _player.Inventory[Commodity.Relics, Account.Trade] += 3;
        var relics = Total(_player, Commodity.Relics);

        int done = OfferEx.Exchange(_player, new GoodsOffer(Commodity.Relics, 1), _settlement, new ScrapOffer(1), 5, out var reason);

        done.Should().Be((int)relics);
        reason.Should().StartWith(_player.Name, "the seller ran out of goods");
        Total(_player, Commodity.Relics).Should().Be(0);
    }

    [Fact]
    public void NpcSettlementTrades_ConserveGoodsAndScrap()
    {
        _player.Inventory.Add(Commodity.Ore, 10);
        var stocked = Enum.GetValues<Commodity>()
            .First(c => c.IsTradeGood() && !c.IsIntegral() && _settlement.Inventory[c, Account.Trade] >= 5);
        float Sum(Commodity c) => Total(_player, c) + Total(_settlement, c);
        var (ore, bought, scrap) = (Sum(Commodity.Ore), Sum(stocked), Sum(Commodity.Scrap));

        SettlementTrade.TrySellToSettlement(_player, Commodity.Ore, 10).Should().BeTrue();
        SettlementTrade.TryBuyFromSettlement(_player, stocked, 5).Should().BeTrue();

        Sum(Commodity.Ore).Should().BeApproximately(ore, 0.01f);
        Sum(stocked).Should().BeApproximately(bought, 0.01f);
        Sum(Commodity.Scrap).Should().BeApproximately(scrap, 0.01f);
    }

    [Fact]
    public void Restock_TopsUpInsteadOfPilingUp()
    {
        var trader = _settlement;
        var forSale = new List<Segment>();
        trader.Restock(1, 0.25f, forSale);
        var stockAfterFirst = Enum.GetValues<Commodity>().Sum(c => trader.Inventory[c, Account.Trade]);
        var segmentsBefore = trader.Inventory.Segments.Count - forSale.Count;

        for (ulong seed = 2; seed < 12; seed++) {
            trader.Restock(seed, 0.25f, forSale);
        }

        Enum.GetValues<Commodity>().Sum(c => trader.Inventory[c, Account.Trade])
            .Should().BeLessThan(stockAfterFirst * 2, "ten more restocks top up to the target, they don't add it each time");
        trader.Inventory.Segments.Count.Should().Be(segmentsBefore + forSale.Count,
            "only the latest batch of generated segments stays for sale");
    }

    [Fact]
    public void ListingOffers_ChangesNothing()
    {
        var before = _settlement.Inventory.ToString();
        var segments = _settlement.Inventory.Segments.Count;

        _settlement.MakeTradeOffers(3);
        _settlement.MakeTradeOffers(4);

        _settlement.Inventory.ToString().Should().Be(before);
        _settlement.Inventory.Segments.Count.Should().Be(segments);
    }

    [Fact]
    public void CrewAndMorale_AreNeverOffered()
    {
        foreach (var actor in _encounter.Actors.OfType<ActorBase>()) {
            var trade = actor.Components.OfType<TradeOfferComponent>().FirstOrDefault();
            if (trade == null) continue;
            trade.GetOrCreateOffers().Should().NotContain(o => o.Commodity == Commodity.Crew || o.Commodity == Commodity.Morale,
                $"{actor.Name} must not buy or sell people");
        }
    }
}
