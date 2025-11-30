using System.Diagnostics;
using System.Diagnostics.Metrics;
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
    public long ArrivalTime { get; set; } = 0;
    public long ExitTime { get; set; } = 0; // 0 means never exits (permanent actor)
    public bool ShouldExit(long currentTime) => Dynamic && currentTime >= ExitTime;
    public void ExitAfter(int duration) {
        if (Dynamic) {
            ExitTime = Math.Min(ExitTime, Game.SafeTime + duration);
        } else {
            ExitTime = ArrivalTime + duration;
        }
    }
}

public sealed class Encounter {
    public Encounter(ulong seed, Location location): this(seed, location,
        location.Type == EncounterType.Settlement ?
            location.Sector.ControllingFaction :
            location.ChooseRandomFaction()) {
    }
    public Encounter(ulong seed, Location location, Factions faction) {
        this.location = location;
        location.SetEncounter(this);
        var wealth = location.Wealth;
        Rng = new XorShift(seed);
        Gaussian = new GaussianSampler(Rng.Seed());
        Faction = faction;
        lastDynamicEvent = Game.SafeTime - CrawlerEx.PoissonQuantileAt(Tuning.Encounter.DynamicCrawlerLifetimeExpectation, 0.95f);
        EncounterTime = lastDynamicEvent;
        Game.Instance!.RegisterEncounter(this);
    }
    XorShift Rng;
    GaussianSampler Gaussian;

    // C# event delegates for encounter events
    public event ActorArrivedEventHandler? ActorArrived;
    public event ActorLeavingEventHandler? ActorLeaving;
    public event ActorLeftEventHandler? ActorLeft;
    public event EncounterTickEventHandler? EncounterTicked;

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

        using var activity = Scope($"{nameof(MenuItems)}({agent.Name})")?
            .SetTag("Agent", agent.Name).SetTag("Agent.Faction", agent.Faction);
        if (agent is Crawler ac) {
            activity?.SetTag("About", ac.About);
        }

        foreach (var (index, subject) in ActorsExcept(agent)
                     .OrderBy(a => a.Faction)
                     .Index()) {
            string prefix = "C" + (char)('A' + index);
            using var activityOther = Scope($"Menu {prefix}")?
                .SetTag("Agent", agent.Name).SetTag("Subject", subject.Name).SetTag("Subject.Faction", subject.Faction);
            var interactions = agent.InteractionsWith(subject).Cast<Interaction>().ToList();
            ap += interactions.TickInteractions(agent, prefix);
            var agentActorMenus = agent.InteractionMenuItems(interactions, subject.Brief(agent), prefix);
            result.AddRange(agentActorMenus);
        }
        return result;
    }

    // Dynamic crawler management
    float hourlyArrivals => Tuning.Encounter.HourlyArrivalsPerPop[Location.Type] * Location.Population * Tuning.Encounter.CrawlerDensity;
    long lastDynamicEvent = 0;
    Crawler AddDynamicCrawler(ulong seed, long arrivalTime, int lifetime) {
        Debug.Assert(arrivalTime >= lastDynamicEvent);
        // Use settlement-specific faction selection for Settlement encounters
        var faction = Location.ChooseRandomFaction();
        var crawler = GenerateFactionActor(seed, faction, arrivalTime, lifetime);

        // Add LeaveEncounterComponent to manage automatic departure
        long exitTime = arrivalTime + lifetime;
        crawler.AddComponent(new LeaveEncounterComponent(exitTime));

        return crawler;
    }
    void SpawnDynamicCrawlers(long currentTime) {
        long elapsed = currentTime - lastDynamicEvent;
        if (elapsed < 0) {
            throw new InvalidOperationException($"TODO: Retrocausality");
        }
        if (elapsed == 0) {
            return;
        }

        // Sample how many crawlers should arrive
        float expectation = hourlyArrivals * elapsed / 3600;
        int arrivalCount = CrawlerEx.PoissonQuantile(expectation, ref Rng);

        if (arrivalCount > 0) {
            // Calculate arrival times for each new crawler and add in order
            var arrivals = new List<(long arrivalTime, int lifetime)>();
            for (int i = 0; i < arrivalCount; i++) {
                int lifetime = CrawlerEx.PoissonQuantile(Tuning.Encounter.DynamicCrawlerLifetimeExpectation, ref Rng);
                // Arrivals spread across the next minute (60 seconds)
                long arrivalTime = currentTime - Rng.NextInt64(elapsed);
                if (arrivalTime + lifetime > currentTime) {
                    arrivals.Add((arrivalTime, lifetime));
                }
            }

            // Add crawlers in order of arrival time
            foreach (var (arrivalTime, lifetime) in arrivals.OrderBy(a => a.arrivalTime)) {
                AddDynamicCrawler(Rng.Seed(), arrivalTime, lifetime);
            }
        }
        lastDynamicEvent = currentTime;
    }

    Location location;
    Dictionary<IActor, EncounterActor> actors = new();
    List<MenuItem> menuItems = new();
    public void AddActor(IActor actor, int? lifetime = null) {
        AddActorAt(actor, EncounterTime, lifetime);
    }
    public void AddActorAt(IActor actor, long arrivalTime, int? lifetime = null) {
        if (actors.ContainsKey(actor)) {
            throw new ArgumentException("Actor is already in encounter");
        }

        if (actor.HasFlag(ActorFlags.Loading)) {
            actor.Begin();
        }

        // Update caching when actors join
        if (actor.Flags.HasFlag(ActorFlags.Player)) UpdatePlayerStatus();

        long exitTime = lifetime.HasValue ? arrivalTime + lifetime.Value : 0;
        var metadata = new EncounterActor {
            ArrivalTime = arrivalTime,
            ExitTime = exitTime,
        };
        actors[actor] = metadata;
        actor.To(Location).Visited = true;
        actor.Arrived(this);

        ActorArrived?.Invoke(actor, arrivalTime);

        if (actor is ActorScheduled scheduled) {
            scheduled.PassTime("Arriving", arrivalTime);
            scheduled.TickTo(EncounterTime);
        }

    }
    // Is this subverting C# idiom to automatically insert using this[]
    public EncounterActor this[IActor actor] => actors.GetOrAddNew(actor);

    public List<IActor> OrderedActors() => actors.Keys.OrderBy(a => a.Faction).ToList();
    public void RemoveActor(IActor actor) {
        if (actor is Crawler crawler) {
            Unschedule(crawler);
        }

        // Raise ActorLeaving event
        ActorLeaving?.Invoke(actor, EncounterTime);

        actors.Remove(actor);
        actor.Left(this);

        // Raise ActorLeft event
        ActorLeft?.Invoke(actor, EncounterTime);

        // Update caching when actors leave
        if (actor.Flags.HasFlag(ActorFlags.Player)) UpdatePlayerStatus();
    }
    public IReadOnlyCollection<IActor> Actors => OrderedActors();
    public IEnumerable<IActor> Settlements => Actors.Where(a => a.Flags.HasFlag(ActorFlags.Settlement));
    public Location Location => location;
    public Factions Faction { get; }

    public Crawler GenerateTradeActor(ulong seed) {
        var actorRng = new XorShift(seed);
        float wealth = Location.Wealth * 0.75f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var trader = Crawler.NewRandom(actorRng.Seed(), Factions.Independent, Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.8f, 1, 1]);
        trader.Faction = Factions.Independent;
        trader.Role = Roles.Trader;

        // Initialize role-specific components
        trader.InitializeComponents(actorRng.Seed());
        return trader;
    }
    public Crawler GeneratePlayerActor(ulong seed) {
        using var activity = Scope($"GeneratePlayer {nameof(Encounter)}");
        float wealth = Location.Wealth * 1.0f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.65f;
        float segmentWealth = wealth * 0.5f;
        var player = Crawler.NewRandom(seed, Factions.Player, Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1, 1]);
        player.Flags |= ActorFlags.Player;
        player.Faction = Factions.Player;
        player.Role = Roles.Player;

        // Add player-specific components
        var actorRng = new XorShift(seed);
        player.InitializeComponents(actorRng.Seed());
        return player;
    }
    public Crawler GenerateBanditActor(ulong seed) {
        using var activity = Scope($"GenerateBandit {nameof(Encounter)}");
        var actorRng = new XorShift(seed);
        float wealth = Location.Wealth * 0.8f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.6f;
        float segmentWealth = wealth * 0.5f;
        var enemy = Crawler.NewRandom(actorRng.Seed(), Factions.Bandit, Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1.2f, 0.8f]);
        enemy.Faction = Factions.Bandit;
        enemy.Role = Roles.Bandit;

        // Initialize role-specific components
        enemy.InitializeComponents(actorRng.Seed());
        return enemy;
    }

    public Crawler GenerateCivilianActor(ulong seed, Factions civilianFaction) {
        using var activity = Scope($"GenerateCivilian {nameof(Encounter)}");
        var actorRng = new XorShift(seed);
        // Similar to Trade but with faction-specific policies
        float wealth = Location.Wealth * 0.75f;
        int crew = Math.Max(1, (int)(wealth / 40));
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var civilian = Crawler.NewRandom(actorRng.Seed(), civilianFaction, Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.6f, 0.8f, 1.0f]);
        civilian.Faction = civilianFaction;

        // Randomly assign a civilian role
        var roleRoll = actorRng.NextSingle();
        civilian.Role = roleRoll switch {
            < 0.6f => Roles.Trader,    // 60% traders
            < 0.8f => Roles.Traveler,  // 20% travelers
            _ => Roles.Customs         // 20% customs officers
        };

        // Initialize role-specific components
        civilian.InitializeComponents(actorRng.Seed());
        return civilian;
    }

    public Crawler GenerateCapital(ulong seed) {
        using var activity = Scope($"GenerateCapital {nameof(Encounter)}");
        var settlement = GenerateSettlement(seed);
        settlement.Domes += 2;
        settlement.Flags |= ActorFlags.Capital;
        return settlement;
    }
    public Crawler GenerateSettlement(ulong seed) {
        var settlementRng = new XorShift(seed);
        using var activity = Scope($"GenerateSettlement {nameof(Encounter)}");
        float t = Location.Position.Y / (float)Location.Map.Height;
        int domes = (int)Math.Log2(Location.Population) + 1;
        int crew = Math.Min(domes * 10, Location.Population);
        // wealth is also population scaled,
        float goodsWealth = Location.Wealth * domes * 0.5f;
        float segmentWealth = Location.Wealth * domes * 0.25f;
        var settlement = Crawler.NewRandom(settlementRng.Seed(), Faction, Location, crew, 15, goodsWealth, segmentWealth, [4, 0, 1, 3]);
        settlement.Domes = domes;
        settlement.Flags |= ActorFlags.Settlement;
        settlement.Flags &= ~ActorFlags.Mobile;
        settlement.Role = Roles.Settlement;

        // Add base components
        settlement.AddComponent(new LifeSupportComponent());
        settlement.AddComponent(new AutoRepairComponent(RepairMode.RepairLowest)); // Settlements auto-repair
        settlement.AddComponent(new CustomsComponent());
        settlement.AddComponent(new TradeOfferComponent(settlementRng.Seed(), 0.25f));
        settlement.AddComponent(new RepairComponent());
        settlement.AddComponent(new LicenseComponent());

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
        Name = settlementRng.ChooseRandom(EncounterNames)!;
        AddActor(settlement);
        return settlement;
    }
    public void GenerateResource(ulong seed) {
        var rng = new XorShift(seed);
        var resource = rng.ChooseRandom<Commodity>();
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
            Commodity.Isotopes => ("Isotope Deposit", $"You find a deposit of radioactive isotopes.", $"Extract {amtStr} Isotopes"),
            Commodity.Nanomaterials => ("Nanomaterial Cache", $"You find a cache of advanced nanomaterials.", $"Retrieve {amtStr} Nanomaterials"),
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

        var resourceActor = new ActorBase(rng.Seed(), name, giftDesc, Factions.Independent, Inv, new (), Location);
        resourceActor.AddComponent(new HarvestComponent(Inv, verb, "H"));
        AddActor(resourceActor);
    }
    public void GenerateHazard(ulong seed) {
        var rng = new XorShift(seed);
        float payoff = Location.Wealth * Tuning.Game.hazardWealthFraction;
        float risk = payoff * Tuning.Game.hazardNegativePayoffRatio;
        // Risk/reward mechanic: promised payoff + chance of negative payoff
        var rewardType = rng.ChooseRandom(Enum.GetValues<Commodity>().Where(c => c.CostAt(location) < payoff * 2));
        Name = $"{rewardType} Hazard";

        float rewardAmt = Inventory.QuantityBought(payoff, rewardType, Location);

        // 50% chance of negative payoff (20% loss of a different commodity)
        Commodity penaltyType = rewardType;
        float penaltyAmt = 0;
        while (penaltyType == rewardType) {
            penaltyType = rng.ChooseRandom(Enum.GetValues<Commodity>().Where(c => c.CostAt(location) < risk * 2));
        }
        penaltyAmt = Inventory.QuantityBought(risk, penaltyType, Location);

        // Build description based on reward and penalty types
        string FormatCommodity(Commodity comm, float amt) {
            string amtStr = comm.IsIntegral()
                ? $"{(int)amt}"
                : $"{amt:F1}";

            string prefix = "";

            return comm switch {
                Commodity.Scrap => $"{prefix}{amtStr}¢¢",
                Commodity.Fuel => $"{prefix}{amtStr} fuel",
                Commodity.Rations => $"{prefix}{amtStr} rations",
                Commodity.Crew => $"{prefix}{amtStr} crew",
                Commodity.Morale => $"{prefix}{amtStr} morale",
                Commodity.Isotopes => $"{prefix}{amtStr} isotopes",
                Commodity.Nanomaterials => $"{prefix}{amtStr} nanomaterials",
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
        string penaltyDesc = ", lose up to " + FormatCommodity(penaltyType, penaltyAmt);

        string verb = $"Explore";
        string hazardDesc = $"A dangerous site contains {rewardDesc}{penaltyDesc}";
        string description = $"Gain {rewardDesc}{penaltyDesc}.";

        var Promised = new Inventory();
        Promised.Add(rewardType, rewardAmt);
        var Risked = new Inventory();
        Risked.Add(penaltyType, penaltyAmt);

        var hazardActor = new ActorBase(rng.Seed(), Name, hazardDesc, Factions.Independent, Promised, new(), Location);
        hazardActor.AddComponent(new HazardComponent(
            Rng.Seed(),
            Risked,
            Tuning.Game.hazardNegativePayoffChance,
            description,
            "H"));
        AddActor(hazardActor);
    }
    public Encounter Generate() {
        using var activity = Scope($"Generate {nameof(Encounter)}");
        var seed = Rng.Seed();
        switch (Location.Type) {
        case EncounterType.Crossroads:
        {
            Name = $"{Faction} Crossroads";
            GenerateFactionActor(seed, Faction, EncounterTime, null);
        }
        break;
        case EncounterType.Settlement: GenerateSettlement(seed); break;
        case EncounterType.Resource: GenerateResource(seed); break;
        case EncounterType.Hazard: GenerateHazard(seed); break;
        }
        return this;
    }
    public Crawler GenerateFactionActor(ulong seed, Factions faction, long arrivalTime, int? lifetime) {
        Crawler result;

        result = faction switch {
            Factions.Bandit => GenerateBanditActor(seed),
            Factions.Player => GeneratePlayerActor(seed),
            Factions.Independent => GenerateTradeActor(seed),
            _ => GenerateCivilianActor(seed, faction),
        };
        AddActorAt(result, arrivalTime, lifetime);
        return result;
    }

    public IEnumerable<IActor> ActorsExcept(IActor actor) => Actors.Where(a => a != actor);
    public IEnumerable<Crawler> CrawlersExcept(IActor actor) => ActorsExcept(actor).OfType<Crawler>();

    static Activity? Scope(string name, ActivityKind kind = ActivityKind.Internal) => null; //LogCat.Encounter.StartActivity(name, kind);
    static ILogger Log => LogCat.Log;
    static Meter Metrics => LogCat.EncounterMetrics;

    // Encounter tick metrics
    public static readonly Counter<int> EncounterTickCount = Metrics.CreateCounter<int>(
        "encounter.ticks.total",
        description: "Total number of encounter ticks executed"
    );

    public static readonly Histogram<long> EncounterTickDuration = Metrics.CreateHistogram<long>(
        "encounter.tick.duration.ms",
        description: "Duration of individual encounter tick execution in milliseconds"
    );

    // Crawler tick metrics
    public static readonly Counter<int> CrawlerTickCount = Metrics.CreateCounter<int>(
        "crawler.ticks.total",
        description: "Total number of crawler ticks executed"
    );

    public static readonly Histogram<long> CrawlerTickDuration = Metrics.CreateHistogram<long>(
        "crawler.tick.duration.ms",
        description: "Duration of individual crawler tick execution in milliseconds"
    );

    // Replaced SortedDictionary with PriorityQueue + Lazy Deletion for O(log N) performance
    PriorityQueue<ActorScheduled, long> eventQueue = new();

    public long EncounterTime { get; set; }
    internal long Game_Scheduled = 0;

    // Tracks if the player is present to toggle between High Precision and Batched modes
    bool _hasPlayer = false;
    void UpdatePlayerStatus() {
        _hasPlayer = actors.Keys.Any(a => a.Flags.HasFlag(ActorFlags.Player));
    }

    public long NextEncounterEvent {
        get {
            if (!eventQueue.TryPeek(out _, out long nextCrawlerEvent)) {
                return EncounterTime; //  + Tuning.MaxDelay;
            }

            // High Precision Mode: If player is here, we must run exactly at the next event
            //if (_hasPlayer) {
                return nextCrawlerEvent;
            //}

            /*
            // Batch Mode: For background encounters, only wake up the Game loop hourly.
            // This prevents thousands of micro-updates from clogging the global scheduler.
            long batchInterval = Tuning.MaxDelay;
            long batchTarget = EncounterTime + batchInterval;

            // If the next event is within the current batch window, wait until the window closes.
            if (nextCrawlerEvent < batchTarget) {
                return batchTarget;
            }

            // If the next event is further out than the batch window, sleep until then.
            return nextCrawlerEvent;
            */
        }
    }

    // Re-entrancy guard
    bool isTicking = false;

    public void ActorScheduleChanged(ActorScheduled actor) {
        var nextEvent = actor.NextEvent;
        if (actor.NextEvent == null) {
            Unschedule(actor);
            return;
        }
        long nextTurn = nextEvent!.EndTime;

        Log.LogTrace($"{Name}: {actor.Name} scheduled for {nextEvent}");
        Debug.Assert(nextTurn >= lastDynamicEvent);
        Debug.Assert(nextTurn >= EncounterTime);
        Debug.Assert(EncounterTime >= lastDynamicEvent);
        actor.Encounter_Scheduled = nextEvent;
        eventQueue.Enqueue(actor, nextTurn);

        UpdateGlobalSchedule();
    }

    void Unschedule(ActorScheduled actor) {
        actor.Encounter_Scheduled = null;
        UpdateGlobalSchedule();
    }

    void UpdateGlobalSchedule() {
        // Optimization: Don't poke Global Game Schedule while strictly internal to Tick
    //    if (!isTicking) {
            Game.Instance!.Schedule(this);
      //  }
    }

    public void Tick(long time) {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (time < EncounterTime) {
            Log.LogError($"Encounter {Name} ticked backwards from {EncounterTime} to {time}");
            EncounterTime = time;
            return;
        }
        
        isTicking = true; // Block global updates until we are done processing the batch

        try {
            using var activity = Scope($"Tick {nameof(Encounter)} {this} to {time}");

            Debug.Assert(eventQueue.Count >= actors.Count);
            while (eventQueue.TryPeek(out var actor, out var eventTime)) {
                // exit and keep the event if it should still be pending
                if (eventTime > time) break;

                var actorStopwatch = Stopwatch.StartNew();
                Debug.Assert(EncounterTime <= eventTime);
                (long encounterStartTime, EncounterTime) = (EncounterTime, eventTime);

                if (!eventQueue.TryDequeue(out actor, out eventTime)) {
                    break;
                }
                var nextEvent = actor.NextEvent!;
                Debug.Assert(actor.NextEvent != null);
                if (actor.Encounter_Scheduled != actor.NextEvent || actor.Encounter_Scheduled == null) {
                    continue;
                }
                Log.LogInformation($"{actor.Name} running {nextEvent}");
                actor.Encounter_Scheduled = null;
                SpawnDynamicCrawlers(eventTime);

                EncounterTicked?.Invoke(encounterStartTime, EncounterTime);

                actor.TickTo(EncounterTime);
                actorStopwatch.Stop();

                // TODO: Move this into a component
                if (actor.Flags.HasFlag(ActorFlags.Player)) {
                    Game.Instance!.GameMenu();
                }

                CrawlerTickCount.Add(1, new KeyValuePair<string, object?>("crawler.faction", actor.Faction));
                CrawlerTickDuration.Record(actorStopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("crawler.name", actor.Name),
                    new KeyValuePair<string, object?>("crawler.faction", actor.Faction)
                );
            }
        } finally {
            isTicking = false;
            UpdateGlobalSchedule(); // Single global update at the end
        }

        stopwatch.Stop();
        Log.LogInformation($"Finished ticking Encounter {this} at time {time}, last event {EncounterTime}, next event {NextEncounterEvent}");

        EncounterTickCount.Add(1,
            new KeyValuePair<string, object?>("encounter.name", Name),
            new KeyValuePair<string, object?>("encounter.faction", Faction)
        );
        EncounterTickDuration.Record(stopwatch.ElapsedMilliseconds,
            new KeyValuePair<string, object?>("encounter.name", Name),
            new KeyValuePair<string, object?>("encounter.faction", Faction)
        );
    }
}
