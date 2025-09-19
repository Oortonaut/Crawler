namespace Crawler;

public enum SimpleEncounterType {
    None,
    Crawler,
    Settlement,
    Resource,
    Hazard,
}

public class Encounter(Location Location) {
    public override string ToString() => $"{Location.Name} {Location.Type} {Location.Terrain}";
    public string Look(Func<IActor, int>? Detailed = null) {
        string result = string.Empty;
        result += string.Join("\n", Actors.Select(a => {
            int detail = Detailed?.Invoke(a) ?? 0;
            if (detail >= 0) {
                return a.Brief(detail);
            } else return string.Empty;
        }));
        return result;
    }
    public IEnumerable<MenuItem> MenuItems(IActor Player) => [
        MenuItem.Sep,
        .. actors.SelectMany(actor => Player.MenuItems(actor)),
        .. GetDefaultMenuItems(),
    ];
    //List<TradeOffer> offers = new();

    List<IActor> actors = new();
    public IReadOnlyCollection<IActor> Actors => actors;

    static List<MenuItem> GetDefaultMenuItems() {
        return [
            MenuItem.Sep,
            new ActionMenuItem("X", "Exit Encounter", null),
        ];
    }
    List<TradeOffer> GenerateGasStationOffers() {
        List<TradeOffer> offers = new();
        // Gas station
        // Sell fuel
        double rate = 1.5;
        double hiked = rate * 1.2;
        double sale = rate * 0.9;
        offers.Add(new TradeOffer(Commodity.Scrap, Commodity.Fuel, rate));
        offers.Add(new TradeOffer(Commodity.Scrap, Commodity.Ration, hiked));
        offers.Add(new TradeOffer(Commodity.Scrap, Commodity.Morale, hiked));
        offers.Add(new TradeOffer(Commodity.Crew, Commodity.Scrap, sale)); // hiring

        var segmentCodes = string.Join("", Segment.AllSegmentCodes().ChooseRandomK(4));
        var segmentsOnOffer = Segment.Create(segmentCodes);
        foreach (var segment in segmentsOnOffer) {
            offers.Add(new TradeOffer(Commodity.Scrap, segment, rate));
        }
        return offers;
    }
    IActor GenerateTradeActor() {
        var Inv = new Inventory();
        var result = new Crawler(Location, Inv);
        Inv.AddRandomInventory(Location, 3, 0.75, true, [1, 0.5, 3, 1]);
        var offers = GenerateGasStationOffers();
        result.Offers.AddRange(offers);
        Inv.Segments.AddRange(result.Offers.SelectMany(o => o.Goods.Segments));
        result.Faction = Faction.Trade;
        result.Name = "Trader";
        result.Recharge(20);
        return result;
    }
    IActor GeneratePlayerActor() {
        var player = Crawler.NewRandom(Location, 1.0, 0.75, [1, 1, 1, 1]);
        player.Faction = Faction.Player;
        player.Recharge(20);
        return player;
    }
    public void GenerateResource() { }
    public IActor GenerateCombat() {
        var enemy = Crawler.NewRandom(Location, 0.8, 0.6, [1, 1, 1, 1]);
        enemy.Faction = Faction.Bandit;
        enemy.Name = "Bandit";
        enemy.Recharge(20);
        return enemy;
    }
    public void GenerateHazard() { }
    public IActor Generate(Faction faction) {

        IActor result = faction switch {
            Faction.Bandit => GenerateCombat(),
            Faction.Player => GeneratePlayerActor(),
            Faction.Trade => GenerateTradeActor(),
            _ => throw new NotImplementedException(),
        };
        actors.Add(result);
        return result;
    }
    public void Tick(IActor player) {
        foreach (var actor in actors) {
            string? fail = actor.FailState;
            if (fail != null) {
                Console.WriteLine($"{actor.Name} {fail}");
            } else {
                var targets = actors.Where(a => !object.ReferenceEquals(this, a));
                actor.Tick(targets);
            }
        }
    }
}
