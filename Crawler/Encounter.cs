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
    public long ArrivalTime { get; set; } = Game.SafeTime;
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

public class Encounter {
    public Encounter(ulong seed, Location location): this(seed, location,
        location.Type == EncounterType.Settlement ?
            location.Sector.ControllingFaction :
            location.ChooseRandomFaction()) {
    }
    public Encounter(ulong seed, Location location, Faction faction) {
        this.location = location;
        var wealth = location.Wealth;
        Rng = new XorShift(seed);
        Gaussian = new GaussianSampler(Rng.Seed());
        Faction = faction;
        LastEncounterEvent = Game.SafeTime;
        lastDynamicEvent = Game.SafeTime - CrawlerEx.PoissonQuantileAt(Tuning.Encounter.DynamicCrawlerLifetimeExpectation, 0.95f);
        Game.Instance!.RegisterEncounter(this);
    }
    XorShift Rng;
    GaussianSampler Gaussian;

    // Event subscription system
    EArraySparse<EncounterEventType, List<IEncounterEventHandler>> _eventHandlers = new();

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
            var interactions = agent.InteractionsWith(subject).ToList();
            ap += interactions.TickInteractions(agent, prefix);
            var agentActorMenus = agent.InteractionMenuItems(interactions, subject.Brief(agent), prefix);
            result.AddRange(agentActorMenus);
        }
        return result;
    }

    // Event subscription system
    public void Subscribe(EncounterEventType eventType, IEncounterEventHandler handler) {
        if (!_eventHandlers.ContainsKey(eventType)) {
            _eventHandlers[eventType] = new List<IEncounterEventHandler>();
        }
        if (!_eventHandlers[eventType].Contains(handler)) {
            _eventHandlers[eventType].Add(handler);
        }
    }

    public void Unsubscribe(EncounterEventType eventType, IEncounterEventHandler handler) {
        if (_eventHandlers.TryGetValue(eventType, out var handlers)) {
            handlers.Remove(handler);
        }
    }

    void PublishEvent(EncounterEvent evt) {
        if (_eventHandlers.TryGetValue(evt.Type, out var handlers)) {
            // Create a copy to avoid modification during iteration
            foreach (var handler in handlers.ToList()) {
                try {
                    handler.HandleEvent(evt);
                } catch (Exception ex) {
                    // Don't let a component error break the encounter
                    Log.LogError(ex, $"Error in event handler for {evt.Type}: {ex.Message}");
                }
            }
        }
    }

    // Dynamic crawler management
    float hourlyArrivals => Tuning.Encounter.HourlyArrivalsPerPop[Location.Type] * Location.Population * Tuning.Encounter.CrawlerDensity;
    long lastDynamicEvent = 0;
    Crawler AddDynamicCrawler(ulong seed, long arrivalTime, int lifetime) {
        // Use settlement-specific faction selection for Settlement encounters
        var faction = Location.ChooseRandomFaction();
        var crawler = GenerateFactionActor(seed, faction, arrivalTime, lifetime);
        crawler.LastEvent = arrivalTime;
        return crawler;
    }
    void UpdateDynamicCrawlers(long currentTime) {
        int elapsed = (int)(currentTime - lastDynamicEvent);
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
        // Remove actors whose exit time has been reached
        var expiredActors = actors
            .Select(a => (a.Key, a.Value))
            .Where(actorRelation => actorRelation.Value.ShouldExit(currentTime))
            .Select(a => a.Key).ToList();
        foreach (var actor in expiredActors) {
            RemoveActor(actor);
        }
    }

    Location location;
    Dictionary<IActor, EncounterActor> actors = new();
    List<MenuItem> menuItems = new();
    public void AddActor(IActor actor, int? lifetime = null) {
        AddActorAt(actor, Game.SafeTime, lifetime);
    }
    public virtual void AddActorAt(IActor actor, long arrivalTime, int? lifetime = null) {
        if (actors.ContainsKey(actor)) {
            throw new ArgumentException("Actor is already in encounter");
        }

        // Update caching when actors join
        if (actor.Flags.HasFlag(EActorFlags.Player)) UpdatePlayerStatus();

        long exitTime = lifetime.HasValue ? arrivalTime + lifetime.Value : 0;
        var metadata = new EncounterActor {
            ArrivalTime = arrivalTime,
            ExitTime = exitTime,
        };
        actors[actor] = metadata;
        actor.To(Location).Visited = true;

        // Subscribe all actor components to encounter events
        foreach (var component in actor.Components) {
            foreach (var eventType in component.SubscribedEvents) {
                Subscribe(eventType, component);
            }
        }

        var existingActors = ActorsExcept(actor).ToList();

        // Notify new actor about all existing actors (old method - will be deprecated)
        actor.Meet(this, LastEncounterEvent, existingActors);

        // Notify all existing actors about the new actor (old method - will be deprecated)
        foreach (var other in existingActors) {
            other.Greet(actor);
        }

        // Publish ActorArrived event (new component-based system)
        PublishEvent(new EncounterEvent(EncounterEventType.ActorArrived, actor, arrivalTime, this));

        if (actor is Crawler crawler) {
            crawler.LastEvent = arrivalTime;
            Schedule(crawler);
        }
    }
    // Is this subverting C# idiom to automatically insert using this[]
    public EncounterActor this[IActor actor] => actors.GetOrAddNew(actor);

    public List<IActor> OrderedActors() => actors.Keys.OrderBy(a => a.Faction).ToList();
    public virtual void RemoveActor(IActor actor) {
        var remainingActors = ActorsExcept(actor).ToList();

        // Publish ActorLeaving event (new component-based system)
        PublishEvent(new EncounterEvent(EncounterEventType.ActorLeaving, actor, LastEncounterEvent, this));

        // Notify all remaining actors about the leaving actor (old method - will be deprecated)
        foreach (var other in remainingActors) {
            other.Part(actor);
        }

        actor.Leave(remainingActors);

        // Publish ActorLeft event (new component-based system)
        PublishEvent(new EncounterEvent(EncounterEventType.ActorLeft, actor, LastEncounterEvent, this));

        // Unsubscribe all actor components from encounter events
        foreach (var component in actor.Components) {
            foreach (var eventType in component.SubscribedEvents) {
                Unsubscribe(eventType, component);
            }
        }

        actors.Remove(actor);

        // Update caching when actors leave
        if (actor.Flags.HasFlag(EActorFlags.Player)) UpdatePlayerStatus();

        if (actor is Crawler crawler) {
            Unschedule(crawler);
        }
    }
    public IReadOnlyCollection<IActor> Actors => OrderedActors();
    public IEnumerable<IActor> Settlements => Actors.Where(a => a.Flags.HasFlag(EActorFlags.Settlement));
    public Location Location => location;
    public Faction Faction { get; }

    public Crawler GenerateTradeActor(ulong seed) {
        var actorRng = new XorShift(seed);
        float wealth = Location.Wealth * 0.75f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var trader = Crawler.NewRandom(actorRng.Seed(), Faction.Independent, Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.8f, 1, 1]);
        trader.Faction = Faction.Independent;
        // Use component instead of StoredProposals
        trader.AddComponent(new TradeOfferComponent(actorRng.Seed(), 0.25f));
        trader.UpdateSegmentCache();
        return trader;
    }
    public Crawler GeneratePlayerActor(ulong seed) {
        using var activity = Scope($"GeneratePlayer {nameof(Encounter)}");
        float wealth = Location.Wealth * 1.0f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.65f;
        float segmentWealth = wealth * 0.5f;
        var player = Crawler.NewRandom(seed, Faction.Player, Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1, 1]);
        player.Flags |= EActorFlags.Player;
        player.Faction = Faction.Player;
        return player;
    }
    public Crawler GenerateBanditActor(ulong seed) {
        using var activity = Scope($"GenerateBandit {nameof(Encounter)}");
        var actorRng = new XorShift(seed);
        float wealth = Location.Wealth * 0.8f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.6f;
        float segmentWealth = wealth * 0.5f;
        var enemy = Crawler.NewRandom(actorRng.Seed(), Faction.Bandit, Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1.2f, 0.8f]);
        enemy.Faction = Faction.Bandit;
        // Add bandit-specific components
        enemy.AddComponent(new BanditExtortionComponent(actorRng.Seed()));
        enemy.AddComponent(new EncounterMessengerComponent());
        enemy.AddComponent(new RelationPrunerComponent());
        return enemy;
    }

    public Crawler GenerateCivilianActor(ulong seed, Faction civilianFaction) {
        using var activity = Scope($"GenerateCivilian {nameof(Encounter)}");
        var actorRng = new XorShift(seed);
        // Similar to Trade but with faction-specific policies
        float wealth = Location.Wealth * 0.75f;
        int crew = Math.Max(1, (int)(wealth / 40));
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var civilian = Crawler.NewRandom(actorRng.Seed(), civilianFaction, Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.6f, 0.8f, 1.0f]);
        civilian.Faction = civilianFaction;
        // Use component instead of StoredProposals
        civilian.AddComponent(new TradeOfferComponent(actorRng.Seed(), 0.25f));
        civilian.AddComponent(new EncounterMessengerComponent());
        civilian.AddComponent(new RelationPrunerComponent());
        civilian.UpdateSegmentCache();
        return civilian;
    }

    public Crawler GenerateCapital(ulong seed) {
        using var activity = Scope($"GenerateCapital {nameof(Encounter)}");
        var settlement = GenerateSettlement(seed);
        settlement.Domes += 2;
        settlement.Flags |= EActorFlags.Capital;
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
        settlement.Flags |= EActorFlags.Settlement;
        settlement.Flags &= ~EActorFlags.Mobile;
        // Use components instead of StoredProposals
        settlement.AddComponent(new TradeOfferComponent(settlementRng.Seed(), 1f));
        settlement.AddComponent(new SettlementContrabandComponent());
        settlement.AddComponent(new EncounterMessengerComponent());
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

        var resourceActor = new StaticActor(name, giftDesc, Faction.Independent, Inv, Location);
        resourceActor.StoredProposals.Add(new ProposeLootTake(Rng / 'H', verb, "H"));
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

        var hazardActor = new StaticActor(Name, hazardDesc, Faction.Independent, Promised, Location);
        hazardActor.StoredProposals.Add(new ProposeLootRisk(Rng / 1, hazardActor,
            Risked,
            Tuning.Game.hazardNegativePayoffChance,
            description));
        AddActor(hazardActor);
    }
    public Encounter Generate() {
        using var activity = Scope($"Generate {nameof(Encounter)}");
        var seed = Rng.Seed();
        switch (Location.Type) {
        case EncounterType.Crossroads:
        {
            Name = $"{Faction} Crossroads";
            GenerateFactionActor(seed, Faction, Game.SafeTime, null);
        }
            break;
        case EncounterType.Settlement: GenerateSettlement(seed); break;
        case EncounterType.Resource: GenerateResource(seed); break;
        case EncounterType.Hazard: GenerateHazard(seed); break;
        }
        return this;
    }
    public Crawler GenerateFactionActor(ulong seed, Faction faction, long arrivalTime, int? lifetime) {
        Crawler result;

        result = faction switch {
            Faction.Bandit => GenerateBanditActor(seed),
            Faction.Player => GeneratePlayerActor(seed),
            Faction.Independent => GenerateTradeActor(seed),
            _ => GenerateCivilianActor(seed, faction),
        };
        AddActorAt(result, arrivalTime, lifetime);
        return result;
    }

    public IEnumerable<IActor> ActorsExcept(IActor actor) => Actors.Where(a => a != actor);
    public IEnumerable<IActor> CrawlersExcept(IActor actor) => ActorsExcept(actor).OfType<Crawler>();

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
    PriorityQueue<Crawler, long> eventQueue = new();
    Dictionary<Crawler, long> scheduledTimes = new();

    public long LastEncounterEvent { get; protected set; }

    // Tracks if the player is present to toggle between High Precision and Batched modes
    bool _hasPlayer = false;
    void UpdatePlayerStatus() {
        _hasPlayer = actors.Keys.Any(a => a.Flags.HasFlag(EActorFlags.Player));
    }

    public long NextEncounterEvent {
        get {
            if (eventQueue.Count == 0) return LastEncounterEvent + Tuning.MaxDelay;
            
            eventQueue.TryPeek(out _, out long nextCrawlerEvent);

            // High Precision Mode: If player is here, we must run exactly at the next event
            if (_hasPlayer) return nextCrawlerEvent;

            // Batch Mode: For background encounters, only wake up the Game loop hourly.
            // This prevents thousands of micro-updates from clogging the global scheduler.
            long batchInterval = Tuning.MaxDelay;
            long batchTarget = LastEncounterEvent + batchInterval;

            // If the next event is within the current batch window, wait until the window closes.
            if (nextCrawlerEvent < batchTarget) {
                return batchTarget;
            }

            // If the next event is further out than the batch window, sleep until then.
            return nextCrawlerEvent;
        }
    }

    // Re-entrancy guard
    bool isTicking = false;

    public void Schedule(Crawler crawler) {
        long eventTime = crawler.NextEvent;
        if (eventTime == 0) {
            int delay = crawler.WeaponDelay() ?? Tuning.MaxDelay;
            eventTime = LastEncounterEvent + delay;
        } else {
            if (eventTime < LastEncounterEvent)
                throw new InvalidOperationException($"Encounter {Name} has a crawler with a negative NextEvent: {crawler.Name} {crawler.NextEvent}");
        }
        
        Schedule(crawler, eventTime);
    }

    void Schedule(Crawler crawler, long nextTurn) {
        // Lazy Deletion: If already scheduled later/same, ignore. If earlier, mark old time stale.
        if (scheduledTimes.TryGetValue(crawler, out var scheduledTurn)) {
            if (nextTurn >= scheduledTurn) return;
            // Implicitly unschedule by overwriting the authoritative time in scheduledTimes
        }
        
        scheduledTimes[crawler] = nextTurn;
        eventQueue.Enqueue(crawler, nextTurn);

        UpdateGlobalSchedule();
    }

    void Unschedule(Crawler crawler) {
        if (scheduledTimes.Remove(crawler)) {
            // Removing from scheduledTimes makes the entry in PriorityQueue "stale" (ignored on pop)
        }
        UpdateGlobalSchedule();
    }

    void UpdateGlobalSchedule() {
        // Optimization: Don't poke Global Game Schedule while strictly internal to Tick
        if (!isTicking) {
            Game.Instance!.Schedule(Location.GetEncounter());
        }
    }

    public void Tick(long time) {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        if (time < LastEncounterEvent) {
            Log.LogError($"Encounter {Name} ticked backwards from {LastEncounterEvent} to {time}");
            LastEncounterEvent = time;
            return;
        }
        
        isTicking = true; // Block global updates until we are done processing the batch

        try {
            using var activity = Scope($"Tick {nameof(Encounter)} {this} to {time}");

            UpdateDynamicCrawlers(time);

            while (eventQueue.Count > 0) {
                if (!eventQueue.TryPeek(out var crawler, out var eventTime)) break;
                
                // Stop if we've reached the target time
                if (eventTime > time) break;

                eventQueue.Dequeue();

                // Lazy Deletion: Check if this event is stale
                if (!scheduledTimes.TryGetValue(crawler, out var validTime) || validTime != eventTime) {
                    continue;
                }
                scheduledTimes.Remove(crawler);

                // Process Crawler
                var crawlerStopwatch = System.Diagnostics.Stopwatch.StartNew();
                crawler.TickThink(eventTime);
                
                if (crawler.Flags.HasFlag(EActorFlags.Player)) {
                    Game.Instance!.GameMenu();
                }

                crawlerStopwatch.Stop();
                CrawlerTickCount.Add(1, new KeyValuePair<string, object?>("crawler.faction", crawler.Faction));
                CrawlerTickDuration.Record(crawlerStopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("crawler.name", crawler.Name),
                    new KeyValuePair<string, object?>("crawler.faction", crawler.Faction)
                );

                LastEncounterEvent = eventTime;
                Schedule(crawler);
            }
            
            // Ensure we update our LastEvent to the tick time, even if no events processed
            // (This prevents falling behind if we just waited for a batch window)
            if (LastEncounterEvent < time) LastEncounterEvent = time;

        } finally {
            isTicking = false;
            UpdateGlobalSchedule(); // Single global update at the end
        }

        stopwatch.Stop();
        Log.LogInformation($"Finished ticking Encounter {this} at time {time}, last event {LastEncounterEvent}, next event {NextEncounterEvent}");

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
