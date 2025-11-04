using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

public enum EncounterType {
    None,
    Crossroads,
    Settlement,
    Resource,
    Hazard,
}
public class EncounterActor {
    public bool Dynamic => ExitTime > 0;
    public long ExitTime { get; set; } = 0; // 0 means never exits (permanent actor)
    public bool ShouldExit(long currentTime) => Dynamic && currentTime >= ExitTime;
    public void ExitAfter(int duration) {
        if (Dynamic) {
            ExitTime = Math.Min(ExitTime, Game.SafeTime + duration);
        } else {
            ExitTime = Game.SafeTime + duration;
        }
    }
}

public class Encounter {
    public Encounter(Location location): this(location,
        location.Type == EncounterType.Settlement ?
            location.Sector.ControllingFaction :
            location.ChooseRandomFaction()) {
    }
    public Encounter(Location location, Faction faction) {
        this.location = location;
        Faction = faction;
        LastEvent = Game.SafeTime;
        Game.Instance!.RegisterEncounter(this);
    }

    public string Name { get; set; } = "Encounter";
    public string Description { get; set; } = "";
    public override string ToString() => $"{Name} at {Location} '{Description}'";
    public string ViewFrom(IActor viewer) {
        string result = Style.Name.Format(Name);
        if (Description != "") {
            result += ": " + Description;
        }
        result += "\n" + viewer.Brief(viewer);
        // result += string.Join("\n", Actors.Select(a => {
        //     return a.Brief(viewer);
        // }));
        return result;
    }
    public IEnumerable<MenuItem> MenuItems(IActor agent) {
        var result = new List<MenuItem>();
        result.Add(new MenuItem("", "<Interactions>"));
        result.Add(MenuItem.Sep);

        // Display mandatory interactions at the top with highlighting
        result.AddRange(menuItems);

        int ap = 0;

        using var activity = LogCat.Interaction.StartActivity($"{nameof(MenuItems)}({agent.Name})")?
            .SetTag("Agent", agent.Name).SetTag("Agent.Faction", agent.Faction);
        if (agent is Crawler ac) {
            activity?.SetTag("About", ac.About);
        }

        foreach (var (index, subject) in ActorsExcept(agent)
                     .OrderBy(a => a.Faction)
                     .Index()) {
            string prefix = "C" + ( char ) ('A' + index);
            using var activityOther = LogCat.Interaction.StartActivity($"Menu {prefix}")?
                .SetTag("Agent", agent.Name).SetTag("Subject", subject.Name).SetTag("Subject.Faction", subject.Faction);
            var interactions = agent.InteractionsWith(subject).ToList();
            ap += interactions.TickInteractions(agent, prefix);
            var agentActorMenus = agent.InteractionMenuItems(interactions, subject.Brief(agent), prefix);
            result.AddRange(agentActorMenus);
        }
        return result;
    }

    // Dynamic crawler management
    float hourlyArrivals => Tuning.Encounter.HourlyArrivals[Location.Type];

    void InitDynamicCrawlers() {
        if (hourlyArrivals <= 0) return;

        float expectedCount = hourlyArrivals * (Tuning.Encounter.DynamicCrawlerLifetimeExpectation / 3600f);
        int initialCount = CrawlerEx.SamplePoisson(expectedCount);
        long time = Game.SafeTime;

        if (initialCount <= 0) {
            return;
        }

        // Calculate arrival times and sort by them
        var arrivals = new List<(long arrivalTime, int lifetime)>();
        for (int i = 0; i < initialCount; i++) {
            int lifetime = ( int ) CrawlerEx.SampleExponential(Tuning.Encounter.DynamicCrawlerLifetimeExpectation);
            // Spread initial arrivals across the lifetime expectation window
            long arrivalTime = time - (int)(Random.Shared.NextSingle() * lifetime);
            arrivals.Add((arrivalTime, lifetime));
        }
        arrivals = arrivals.OrderBy(a => a.arrivalTime).ToList();

        LastEvent = arrivals.First().arrivalTime;
       // Add crawlers in order of arrival time
        foreach (var (arrivalTime, lifetime) in arrivals) {
            var crawler = AddDynamicCrawler(lifetime);
            // Bring all other crawlers up to this time.
            crawler.LastEvent = arrivalTime;
            Tick(arrivalTime);
        }
    }

    Crawler AddDynamicCrawler(int lifetime) {
        // Use settlement-specific faction selection for Settlement encounters
        var faction = Location.ChooseRandomFaction();
        var crawler = GenerateFactionActor(faction, lifetime);
        return crawler;
    }
    void UpdateDynamicCrawlers(long currentTime) {
        if (hourlyArrivals <= 0) {
            return;
        }
        int elapsed = (int)(currentTime - LastEvent);
        if (elapsed == 0) {
            return;
        }

        // Remove actors whose exit time has been reached
        var expiredActors = actors
            .Select(a => (a.Key, a.Value))
            .Where(actorRelation => actorRelation.Value.ShouldExit(currentTime))
            .Select(a => a.Key).ToList();
        foreach (var actor in expiredActors) {
            RemoveActor(actor);
        }

        // Sample how many crawlers should arrive
        int arrivalCount = CrawlerEx.SamplePoisson(hourlyArrivals * elapsed / 3600);

        if (arrivalCount > 0) {
            // Calculate arrival times for each new crawler and add in order
            var arrivals = new List<(int arrivalTime, int lifetime)>();
            for (int i = 0; i < arrivalCount; i++) {
                int lifetime = CrawlerEx.SamplePoisson(Tuning.Encounter.DynamicCrawlerLifetimeExpectation);
                // Arrivals spread across the next minute (60 seconds)
                int arrivalTime = Random.Shared.Next(0, 60);
                arrivals.Add((arrivalTime, lifetime));
            }

            // Add crawlers in order of arrival time
            foreach (var (arrivalTime, lifetime) in arrivals.OrderBy(a => a.arrivalTime)) {
                AddDynamicCrawler(lifetime);
            }
        }
    }

    Location location;
    Dictionary<IActor, EncounterActor> actors = new();
    List<MenuItem> menuItems = new();
    public virtual void AddActor(IActor actor, int? lifetime = null) {
        if (actors.ContainsKey(actor)) {
            throw new ArgumentException("Actor is already in encounter");
        }

        long exitTime = lifetime.HasValue ? Game.SafeTime + lifetime.Value : 0;
        var metadata = new EncounterActor {
            ExitTime = exitTime,
        };
        actors[actor] = metadata;
        actor.To(Location).Visited = true;

        var existingActors = ActorsExcept(actor).ToList();

        // Notify new actor about all existing actors
        actor.Meet(this, LastEvent, existingActors);

        // Notify all existing actors about the new actor
        foreach (var other in existingActors) {
            other.Greet(actor);
        }
        if (actor is Crawler crawler) {
            Schedule(crawler);
        }
    }
    // Is this subverting C# idiom to automatically insert using this[]
    public EncounterActor this[IActor actor] => actors.GetOrAddNew(actor);

    public List<IActor> OrderedActors() => actors.Keys.OrderBy(a => a.Faction).ToList();
    public virtual void RemoveActor(IActor actor) {
        var remainingActors = ActorsExcept(actor).ToList();

        // Notify all remaining actors about the leaving actor
        foreach (var other in remainingActors) {
            other.Part(actor);
        }

        actor.Leave(remainingActors);
        actors.Remove(actor);
        if (actor is Crawler crawler) {
            Unschedule(crawler);
        }
    }
    public IReadOnlyCollection<IActor> Actors => OrderedActors();
    public IEnumerable<IActor> Settlements => Actors.Where(a => a.Flags.HasFlag(EActorFlags.Settlement));
    public Location Location => location;
    public Faction Faction { get; }

    public Crawler GenerateTradeActor() {
        float wealth = Location.Wealth * 0.75f;
        int crew = ( int ) Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var trader = Crawler.NewRandom(Faction.Independent, Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.8f, 1, 1]);
        trader.Faction = Faction.Independent;
        trader.StoredProposals.AddRange(trader.MakeTradeProposals( 0.25f));
        trader.UpdateSegmentCache();
        return trader;
    }
    public Crawler GeneratePlayerActor() {
        using var activity = LogCat.Encounter.StartActivity($"GeneratePlayer {nameof(Encounter)}");
        float wealth = Location.Wealth * 1.0f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.65f;
        float segmentWealth = wealth * 0.5f;
        var player = Crawler.NewRandom(Faction.Player, Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1, 1]);
        player.Flags |= EActorFlags.Player;
        player.Faction = Faction.Player;
        return player;
    }
    public Crawler GenerateBanditActor() {
        using var activity = LogCat.Encounter.StartActivity($"GenerateBandit {nameof(Encounter)}");
        float wealth = Location.Wealth * 0.8f;
        int crew = ( int ) Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.6f;
        float segmentWealth = wealth * 0.5f;
        var enemy = Crawler.NewRandom(Faction.Bandit, Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1.2f, 0.8f]);
        enemy.Faction = Faction.Bandit;

        return enemy;
    }

    public Crawler GenerateCivilianActor(Faction civilianFaction) {
        using var activity = LogCat.Encounter.StartActivity($"GenerateCivilian {nameof(Encounter)}");
        // Similar to Trade but with faction-specific policies
        float wealth = Location.Wealth * 0.75f;
        int crew = Math.Max(1, (int)(wealth / 40));
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var civilian = Crawler.NewRandom(civilianFaction, Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.6f, 0.8f, 1.0f]);
        civilian.Faction = civilianFaction;
        civilian.StoredProposals.AddRange(civilian.MakeTradeProposals(0.25f));
        civilian.UpdateSegmentCache();
        return civilian;
    }

    public Crawler GenerateCapital() {
        using var activity = LogCat.Encounter.StartActivity($"GenerateCapital {nameof(Encounter)}");
        var settlement = GenerateSettlement();
        settlement.Domes += 2;
        settlement.Flags |= EActorFlags.Capital;
        return settlement;
    }
    public Crawler GenerateSettlement() {
        using var activity = LogCat.Encounter.StartActivity($"GenerateSettlement {nameof(Encounter)}");
        float t = Location.Position.Y / ( float ) Location.Map.Height;
        int domes = ( int ) Math.Log2(Location.Population) + 1;
        int crew = Math.Min(domes * 10, Location.Population);
        // wealth is also population scaled,
        float goodsWealth = Location.Wealth * domes * 0.5f;
        float segmentWealth = Location.Wealth * domes * 0.25f;
        var settlement = Crawler.NewRandom(Faction, Location, crew, 15, goodsWealth, segmentWealth, [4, 0, 1, 3]);
        settlement.PassengersInv = Location.Population - crew;
        settlement.Domes = domes;
        settlement.Flags |= EActorFlags.Settlement;
        settlement.Flags &= ~EActorFlags.Mobile;
        var proposals = settlement.MakeTradeProposals( 1);
        settlement.StoredProposals.AddRange(proposals);
        settlement.UpdateSegmentCache();

        IEnumerable<string> EncounterNames = [];
        if (t < 0.15f) {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.StormSettlementNames];
        } else if (t < 0.3f) {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.StormSettlementNames, .. Names.DaySettlementNames];
        } else if (t < 0.45f) {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.DaySettlementNames];
        } else if (t < 0.6f) {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.DaySettlementNames, .. Names.DuskSettlementNames];
        } else if (t < 0.75f) {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.DuskSettlementNames];
        } else if (t < 0.9f) {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.DuskSettlementNames, .. Names.NightSettlementNames];
        } else {
            EncounterNames = [.. Names.ClassicSettlementNames, .. Names.NightSettlementNames];
        }
        Name = EncounterNames.ChooseRandom()!;
        AddActor(settlement);
        return settlement;
    }
    public void GenerateResource() {
        var resource = CrawlerEx.ChooseRandom<Commodity>();
        Name = $"{resource} Resource";
        var amt = Inventory.QuantityBought(Location.Wealth * Tuning.Game.resourcePayoffFraction, resource, location);

        string giftDesc;
        string verb;
        string name;

        // Format amount appropriately (integer for integral commodities, 1 decimal otherwise)
        string amtStr = resource.IsIntegral()
            ? $"{(int)amt}"
            : $"{amt:F1}";

        (name, giftDesc, verb) = resource switch {
            Commodity.Fuel => ("Fuel Cache", "You find an abandoned fuel cache.", $"Take {amtStr} Fuel"),
            Commodity.Rations => ("Ration Stash", "You find an abandoned stash of rations.", $"Take {amtStr} Rations"),
            Commodity.Crew => ("Disabled Crawler", $"You find a disabled crawler with {amtStr} crew running low on oxygen.", $"Rescue {amtStr} Crew"),
            Commodity.Morale => ("Hidden Entertainment", "You find cached entertainment media. It will invigorate the crew.", $"Take Media ({amtStr} Morale)"),
            Commodity.Scrap => ("Abandoned Crawler", "This abandoned crawler can be scrapped.", $"Scrap for {amtStr}¢¢"),
            Commodity.Passengers => ("Stranded Passengers", $"You find {amtStr} stranded passengers in need of transport.", $"Rescue {amtStr} Passengers"),
            Commodity.Soldiers => ("Stranded Soldiers", $"You find {amtStr} armed soldiers whose vehicle broke down.", $"Pick Up {amtStr} Soldiers"),
            Commodity.Air => ("Air Cache", "You find pressurized air canisters.", $"Take {amtStr} Air"),
            Commodity.Water => ("Water Cache", "You find sealed water containers.", $"Take {amtStr} Water"),
            Commodity.Biomass => ("Biomass Deposit", "You find a deposit of organic material.", $"Harvest {amtStr} Biomass"),
            Commodity.Ore => ("Ore Deposit", "You find exposed metallic ore.", $"Mine {amtStr} Ore"),
            Commodity.Silicates => ("Silicate Deposit", "You find crystalline mineral formations.", $"Harvest {amtStr} Silicates"),
            Commodity.Metal => ("Metal Cache", "You find processed metal sheets and bars.", $"Take {amtStr} Metal"),
            Commodity.Chemicals => ("Chemical Cache", "You find sealed containers of industrial chemicals.", $"Take {amtStr} Chemicals"),
            Commodity.Glass => ("Glass Cache", "You find crates of glass panels and containers.", $"Take {amtStr} Glass"),
            Commodity.Ceramics => ("Ceramic Cache", "You find heat-resistant ceramic components.", $"Take {amtStr} Ceramics"),
            Commodity.Polymers => ("Polymer Cache", "You find synthetic polymer materials.", $"Take {amtStr} Polymers"),
            Commodity.Alloys => ("Alloy Cache", "You find advanced metal alloy components.", $"Take {amtStr} Alloys"),
            Commodity.Electronics => ("Electronics Cache", "You find salvaged circuit boards and components.", $"Take {amtStr} Electronics"),
            Commodity.Explosives => ("Explosives Cache", "You find sealed military explosives.", $"Take {amtStr} Explosives"),
            Commodity.Medicines => ("Medical Supplies", "You find a medical supply cache.", $"Take {amtStr} Medicines"),
            Commodity.Textiles => ("Textile Cache", "You find bolts of fabric and cloth.", $"Take {amtStr} Textiles"),
            Commodity.Gems => ("Gem Cache", "You find a hidden stash of precious gems.", $"Take {amtStr} Gems"),
            Commodity.Toys => ("Toy Cache", "You find a cache of entertainment items and games.", $"Take {amtStr} Toys"),
            Commodity.Machines => ("Machinery Cache", "You find industrial machinery and tools.", $"Take {amtStr} Machines"),
            Commodity.AiCores => ("Computer Cache", "You find intact AI cores.", $"Take {amtStr} AI cores"),
            Commodity.Media => ("Media Cache", "You find data storage devices full of entertainment.", $"Take {amtStr} Media"),
            Commodity.Liquor => ("Liquor Cache", "You find sealed bottles of alcohol.", $"Take {amtStr} Liquor"),
            Commodity.Stims => ("Stim Cache", "You find a hidden stash of stimulants.", $"Take {amtStr} Stims"),
            Commodity.Downers => ("Downer Cache", "You find a hidden stash of sedatives.", $"Take {amtStr} Downers"),
            Commodity.Trips => ("Psychedelics Cache", "You find a hidden stash of psychoactive substances.", $"Take {amtStr} Trips"),
            Commodity.SmallArms => ("Weapons Cache", "You find a cache of small arms.", $"Take {amtStr} SmallArms"),
            Commodity.Idols => ("Religious Idols", "You find sacred religious artifacts.", $"Take {amtStr} Idols"),
            Commodity.Texts => ("Sacred Texts", "You find ancient religious manuscripts.", $"Take {amtStr} Texts"),
            Commodity.Relics => ("Holy Relics", "You find precious religious relics.", $"Take {amtStr} Relics"),
            _ => ($"{resource} Cache", $"You find a cache of {resource}.", $"Take {amtStr} {resource}"),
        };

        var Inv = new Inventory();
        Inv.Add(resource, amt);

        var resourceActor = new StaticActor(name, giftDesc, Faction.Independent, Inv, Location);
        resourceActor.StoredProposals.Add(new ProposeLootTake("H", verb));
        AddActor(resourceActor);
    }
    public void GenerateHazard() {
        // Risk/reward mechanic: promised payoff + chance of negative payoff
        var rewardType = CrawlerEx.ChooseRandom<Commodity>();
        Name = $"{rewardType} Hazard";

        float payoff = Location.Wealth * Tuning.Game.hazardPayoffFraction;
        float rewardAmt = Inventory.QuantityBought(payoff, rewardType, Location);

        // 50% chance of negative payoff (20% loss of a different commodity)
        Commodity penaltyType = rewardType;
        float penaltyAmt = 0;
        while (penaltyType == rewardType) {
            penaltyType = CrawlerEx.ChooseRandom<Commodity>();
        }
        penaltyAmt = Inventory.QuantityBought(payoff * Tuning.Game.hazardNegativePayoffRatio, penaltyType, Location);

        // Build description based on reward and penalty types
        string FormatCommodity(Commodity comm, float amt, bool isLoss = false) {
            string amtStr = comm.IsIntegral()
                ? $"{(int)amt}"
                : $"{amt:F1}";

            string prefix = isLoss ? "lose up to " : "";

            return comm switch {
                Commodity.Scrap => isLoss ? $"{prefix}{amtStr}¢¢" : $"{amtStr}¢¢",
                Commodity.Fuel => $"{prefix}{amtStr} fuel",
                Commodity.Rations => $"{prefix}{amtStr} rations",
                Commodity.Crew => $"{prefix}{amtStr} crew",
                Commodity.Morale => $"{prefix}{amtStr} morale",
                Commodity.Passengers => $"{prefix}{amtStr} passengers",
                Commodity.Soldiers => $"{prefix}{amtStr} soldiers",
                Commodity.Air => $"{prefix}{amtStr} air",
                Commodity.Water => $"{prefix}{amtStr} water",
                Commodity.Biomass => $"{prefix}{amtStr} biomass",
                Commodity.Ore => $"{prefix}{amtStr} ore",
                Commodity.Silicates => $"{prefix}{amtStr} silicates",
                Commodity.Metal => $"{prefix}{amtStr} metal",
                Commodity.Chemicals => $"{prefix}{amtStr} chemicals",
                Commodity.Glass => $"{prefix}{amtStr} glass",
                Commodity.Ceramics => $"{prefix}{amtStr} ceramics",
                Commodity.Polymers => $"{prefix}{amtStr} polymers",
                Commodity.Alloys => $"{prefix}{amtStr} alloys",
                Commodity.Electronics => $"{prefix}{amtStr} electronics",
                Commodity.Explosives => $"{prefix}{amtStr} explosives",
                Commodity.Medicines => $"{prefix}{amtStr} medicines",
                Commodity.Textiles => $"{prefix}{amtStr} textiles",
                Commodity.Gems => $"{prefix}{amtStr} gems",
                Commodity.Toys => $"{prefix}{amtStr} toys",
                Commodity.Machines => $"{prefix}{amtStr} machines",
                Commodity.AiCores => $"{prefix}{amtStr} computers",
                Commodity.Media => $"{prefix}{amtStr} media",
                Commodity.Liquor => $"{prefix}{amtStr} liquor",
                Commodity.Stims => $"{prefix}{amtStr} stims",
                Commodity.Downers => $"{prefix}{amtStr} downers",
                Commodity.Trips => $"{prefix}{amtStr} trips",
                Commodity.SmallArms => $"{prefix}{amtStr} small arms",
                Commodity.Idols => $"{prefix}{amtStr} idols",
                Commodity.Texts => $"{prefix}{amtStr} texts",
                Commodity.Relics => $"{prefix}{amtStr} relics",
                _ => $"{prefix}{amtStr} {comm.ToString().ToLower()}",
            };
        }

        string rewardDesc = FormatCommodity(rewardType, rewardAmt);
        string penaltyDesc = ", " + FormatCommodity(penaltyType, penaltyAmt, isLoss: true);

        string verb = $"Explore";
        string hazardDesc = $"A dangerous site contains {rewardDesc}{penaltyDesc}";

        var Promised = new Inventory();
        Promised.Add(rewardType, rewardAmt);
        var Risked = new Inventory();
        Risked.Add(penaltyType, penaltyAmt);

        var hazardActor = new StaticActor(Name, hazardDesc, Faction.Independent,  Promised, Location);
        hazardActor.StoredProposals.Add(new ProposeLootPay(
            hazardActor,
            Risked,
            Tuning.Game.hazardNegativePayoffChance));
        AddActor(hazardActor);
    }
    public Encounter Generate() {
        using var activity = LogCat.Encounter.StartActivity($"Generate {nameof(Encounter)}");
        switch (Location.Type) {
        case EncounterType.Crossroads:
        {
            Name = $"{Faction} Crossroads";
            GenerateFactionActor(Faction, null);
        } break;
        case EncounterType.Settlement: GenerateSettlement(); break;
        case EncounterType.Resource: GenerateResource(); break;
        case EncounterType.Hazard: GenerateHazard(); break;
        }
        InitDynamicCrawlers();
        return this;
    }
    public Crawler GenerateFactionActor(Faction faction, int? lifetime) {
        Crawler result;

        result = faction switch {
            Faction.Bandit => GenerateBanditActor(),
            Faction.Player => GeneratePlayerActor(),
            Faction.Independent => GenerateTradeActor(),
            _ => GenerateCivilianActor(faction),
        };
        AddActor(result, lifetime);
        return result;
    }

    public IEnumerable<IActor> ActorsExcept(IActor actor) => Actors.Where(a => a != actor);
    public IEnumerable<IActor> CrawlersExcept(IActor actor) => ActorsExcept(actor).OfType<Crawler>();

    SortedDictionary<long, List<Crawler>> crawlersByTurn = new();
    Dictionary<Crawler, long> turnForCrawler = new();
    public long LastEvent { get; protected set; }
    public long NextEvent => crawlersByTurn.Any() ? crawlersByTurn.Keys.Min() : Game.SafeTime + Tuning.MaxDelay;
    public void Schedule(Crawler crawler) {
        int delay = crawler.GetDelay();
        Schedule(crawler, Game.SafeTime + delay);
    }
    void Schedule(Crawler crawler, long nextTurn) {
        if (turnForCrawler.TryGetValue(crawler, out var scheduledTurn)) {
            if (nextTurn < scheduledTurn) {
                Unschedule(crawler);
            } else {
                return;
            }
        }
        var turnList = crawlersByTurn.GetOrAddNew(nextTurn, () => new List<Crawler>());
        turnList.Add(crawler);
        turnForCrawler[crawler] = nextTurn;
        // No encounter while we are creating it, but it will schedule itself
        if (Location.HasEncounter) {
            Game.Instance!.Schedule(Location.GetEncounter());
        }
    }
    void Unschedule(Crawler crawler) {
        if (turnForCrawler.TryGetValue(crawler, out var turn)) {
            var turnList = crawlersByTurn[turn];
            turnList.Remove(crawler);
            if (turnList.Count == 0) {
                crawlersByTurn.Remove(turn);
            }
            turnForCrawler.Remove(crawler);
        }
        if (Location.HasEncounter) {
            Game.Instance!.Schedule(Location.GetEncounter());
        }
    }
    List<Crawler> TurnCrawlers() {
        var first = crawlersByTurn.First();
        long turn = first.Key;
        crawlersByTurn.Remove(turn);
        var crawlers = first.Value;
        foreach (var crawler in crawlers) {
            // TODO: Use a 0 sentinel instead of removing
            turnForCrawler.Remove(crawler);
        }
        return crawlers;
    }
    public void Tick(long time) {
        if (time < LastEvent) {
            LogCat.Log.LogInformation($"Encounter {Name} ticked backwards from {LastEvent} to {time}");
            LastEvent = time;
            return;
        }
        int elapsed = (int) (time - LastEvent);
        using var activity = LogCat.Encounter.StartActivity($"Tick {nameof(Encounter)} {Name} to {time} elapsed {elapsed}");
        if (elapsed == 0) {
            return;
        }

        UpdateDynamicCrawlers(time);

        while (crawlersByTurn.Count > 0 && NextEvent <= time) {
            foreach (var crawler in TurnCrawlers()) {
                crawler.Tick(time);
                if (crawler.Flags.HasFlag(EActorFlags.Player)) {
                    Game.Instance!.GameMenu();
                } else {
                    crawler.Think(elapsed, ActorsExcept(crawler));
                }
                Schedule(crawler);
            }
        }

        LastEvent = time;
    }
}
