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
    public TimePoint ArrivalTime { get; set; } = TimePoint.Zero;
}

public sealed class Encounter : IComparable<Encounter> {
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
        long offset = (Rng / "StartTime").NextInt64(2500);
        // Initialize encounter time to a time in the past before any actors exist
        // Will be updated when first actor arrives
        EncounterTime = Tuning.StartGameTime - new TimeDuration(CrawlerEx.PoissonQuantileAt(Tuning.Encounter.DynamicCrawlerLifetimeExpectation, 0.95f));

        Game.Instance!.RegisterEncounter(this);
    }
    XorShift Rng;
    GaussianSampler Gaussian;

    // C# event delegates for encounter events
    public event ActorArrivedEventHandler? ActorArrived;
    public event ActorLeftEventHandler? ActorLeft;
    public event EncounterTickEventHandler? EncounterTicked;

    public string Name { get; set; } = "<unnamed>";
    public string Description { get; set; } = "";
    public override string ToString() => $"Encounter {Name} at {Location} '{Description}' {Game.TimeString(EncounterTime)}";
    public string ViewFrom(IActor viewer) {
        string result = "Encounter " + Style.Name.Format(Name) + $" at {Location.PosString}";
        if (Description != "") {
            result += ": " + Description;
        }
        result += "\n" + viewer.Brief(viewer);
        // result += string.Join("\n", Actors.Select(a => {
        //     return a.Brief(viewer);
        // }));
        return result;
    }
    /// <summary>
    /// Build interaction context for the agent (typically player).
    /// Groups interactions by subject actor and includes trade offers for market display.
    /// </summary>
    public InteractionContext BuildInteractionContext(IActor agent) {
        using var activity = Scope($"{nameof(BuildInteractionContext)}({agent.Name})")?
            .SetTag("Agent", agent.Name).SetTag("Agent.Faction", agent.Faction);
        if (agent is Crawler ac) {
            activity?.SetTag("About", ac.About);
        }

        var context = new InteractionContext { Agent = agent };

        foreach (var (index, subject) in ActorsExcept(agent)
                     .OrderBy(a => a.Faction)
                     .Index()) {
            string prefix = "C" + (char)('A' + index);
            using var activityOther = Scope($"Menu {prefix}")?
                .SetTag("Agent", agent.Name).SetTag("Subject", subject.Name).SetTag("Subject.Faction", subject.Faction);

            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: Processing {prefix} - agent={agent.Name}, subject={subject.Name}");

            // Collect interactions from both directions: agent->subject and subject->agent
            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: Calling agent.InteractionsWith(subject)");
            var agentToSubject = agent.InteractionsWith(subject).ToList();
            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: agent->subject yielded {agentToSubject.Count} interactions");

            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: Calling subject.InteractionsWith(agent)");
            var subjectToAgent = subject.InteractionsWith(agent).ToList();
            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: subject->agent yielded {subjectToAgent.Count} interactions");

            var interactions = agentToSubject.Concat(subjectToAgent).ToList();
            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: Total interactions for {prefix}: {interactions.Count}");

            // Get trade offers if subject has TradeOfferComponent
            List<TradeOffer>? tradeOffers = null;
            if (subject is ActorBase subjectBase) {
                var tradeComponent = subjectBase.Components.OfType<TradeOfferComponent>().FirstOrDefault();
                if (tradeComponent != null) {
                    tradeOffers = tradeComponent.GetOrCreateOffers();
                    LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: Subject {subject.Name} has {tradeOffers.Count} trade offers");
                }
            }

            var group = new InteractionGroup {
                Subject = subject,
                Label = subject.Brief(agent),
                Prefix = prefix,
                Interactions = interactions,
                TradeOffers = tradeOffers
            };

            context.Groups.Add(group);
            LogCat.Log.LogInformation($"Encounter.BuildInteractionContext: Added group {prefix} with {interactions.Count} interactions");
        }

        return context;
    }

    /// <summary>
    /// Legacy method for backward compatibility.
    /// Builds InteractionContext and converts to MenuItem list using ConsoleMenuRenderer logic.
    /// </summary>
    public IEnumerable<MenuItem> MenuItems(IActor agent) {
        var result = new List<MenuItem>();
        result.Add(new MenuItem("", "<Interactions>"));
        result.Add(MenuItem.Sep);

        // Display mandatory interactions at the top with highlighting
        result.AddRange(menuItems);

        var context = BuildInteractionContext(agent);

        // Process immediate interactions and build menu items
        foreach (var group in context.Groups) {
            // Process immediate interactions
            foreach (var interaction in group.Interactions) {
                var msg = interaction.MessageFor(agent);
                if (!string.IsNullOrEmpty(msg)) {
                    agent.Message(Style.Em.Format(msg));
                }
                // Note: Immediate interactions are now handled by renderer
            }

            // Generate menu items for this group (using old helper method)
            var agentActorMenus = agent.InteractionMenuItems(group.Interactions, group.Label, group.Prefix);
            result.AddRange(agentActorMenus);
        }

        return result;
    }

    // Dynamic crawler management
    float hourlyArrivals => Tuning.Encounter.HourlyArrivalsPerPop[Location.Type] * Location.Population * Tuning.Encounter.CrawlerDensity;
    Crawler AddDynamicCrawler(ulong seed, TimePoint arrivalTime, int lifetime) {
        // Use settlement-specific faction selection for Settlement encounters
        var faction = Location.ChooseRandomFaction();
        var crawler = GenerateFactionActor(seed, faction);

        // Dynamic crawlers spawn directly at this encounter's location
        crawler.Location = Location;

        // Note: GenerateFactionActor creates the crawler with the Loading flag set
        // and calls InitializeComponents. TravelEvent.OnEnd will call AddActorAt when
        // the crawler arrives, which will call Begin() to properly enter all components.

        // Attach LeaveEncounterComponent to handle exit
        crawler.AddComponent(new LeaveEncounterComponent(arrivalTime + TimeDuration.FromSeconds(lifetime)));

        // Schedule travel to this encounter instead of adding directly
        Game.Instance!.ScheduleTravel(crawler, arrivalTime, Location);

        return crawler;
    }
    public void SpawnDynamicCrawlers(TimePoint previousTime, TimePoint currentTime) {
        var elapsed = currentTime - previousTime;
        if (elapsed.IsNegative) {
            throw new InvalidOperationException($"TODO: Retrocausality");
        }
        if (elapsed == TimeDuration.Zero) {
            return;
        }

        // Sample how many crawlers should arrive
        float expectation = hourlyArrivals * (float)elapsed.TotalHours;
        int arrivalCount = CrawlerEx.PoissonQuantile(expectation, ref Rng);

        Log.LogInformation($"SpawnDynamicCrawlers: {Name} type={Location.Type} pop={Location.Population} hourly={hourlyArrivals:F4} elapsed={elapsed} expect={expectation:F2} count={arrivalCount}");

        if (arrivalCount > 0) {
            // Calculate arrival times for each new crawler and add in order
            var arrivals = new List<(TimePoint arrivalTime, int lifetime)>();
            for (int i = 0; i < arrivalCount; i++) {
                int lifetime = CrawlerEx.PoissonQuantile(Tuning.Encounter.DynamicCrawlerLifetimeExpectation, ref Rng);
                // Arrivals spread across the elapsed time period
                var arrivalTime = previousTime + TimeDuration.FromSeconds(Rng.NextInt64(elapsed.TotalSeconds));
                // Only add crawlers that:
                // 1. Are still present at currentTime (arrivalTime + lifetime > currentTime)
                // 2. Arrive at or after current encounter time (arrivalTime >= EncounterTime)
                //    This prevents adding crawlers scheduled in the past during tick processing
                if (arrivalTime + TimeDuration.FromSeconds(lifetime) > currentTime && arrivalTime >= EncounterTime) {
                    arrivals.Add((arrivalTime, lifetime));
                }
            }
            arrivals.Sort((a, b) => a.arrivalTime.CompareTo(b.arrivalTime));

            // Add crawlers in order of arrival time
            foreach (var (arrivalTime, lifetime) in arrivals) {
                AddDynamicCrawler(Rng.Seed(), arrivalTime, lifetime);
            }
        }
    }

    Location location;
    Dictionary<IActor, EncounterActor> actors = new();
    List<MenuItem> menuItems = new();
    public void AddActor(IActor actor, int? lifetime = null) {
        AddActorAt(actor, EncounterTime, lifetime);
    }
    public void AddActorAt(IActor actor, TimePoint arrivalTime, int? lifetime = null) {
        if (actors.ContainsKey(actor)) {
            throw new ArgumentException("Actor is already in encounter");
        }

        if (actor.HasFlag(ActorFlags.Loading)) {
            actor.Begin();
        }

        // Update caching when actors join
        if (actor.Flags.HasFlag(ActorFlags.Player)) UpdatePlayerStatus();

        if (lifetime.HasValue && actor is Crawler crawler) {
            crawler.AddComponent(new LeaveEncounterComponent(arrivalTime + TimeDuration.FromSeconds(lifetime.Value)));
        }

        TimePoint exitTime = lifetime.HasValue ? arrivalTime + TimeDuration.FromSeconds(lifetime.Value) : TimePoint.Zero;
        var metadata = new EncounterActor {
            ArrivalTime = arrivalTime,
        };
        actors[actor] = metadata;
        actor.To(Location).Visited = true;
        actor.Arrived(this);

        // Initialize actor time to arrival time before firing events
        // This ensures message timestamps display correctly
        const bool simulateAll = true;
        if (simulateAll) {
            foreach (var other in actors.Keys) {
                other.SimulateTo(arrivalTime);
                Game.Instance!.Preempt(other, 0); // tick anything with 0 priority
            }
        } else {
            actor.SimulateTo(arrivalTime);
        }

        // CRITICAL: Event handlers receive historical arrival time
        // Actors have already been simulated to arrivalTime via SimulateTo above
        LogCat.Log.LogInformation($"Encounter.AddActorAt: Firing ActorArrived event for {actor.Name} at time {Game.TimeString(arrivalTime)}, subscribers={ActorArrived?.GetInvocationList().Length ?? 0}");
        ActorArrived?.Invoke(actor, arrivalTime);

    }
    // Is this subverting C# idiom to automatically insert using this[]
    public EncounterActor this[IActor actor] => actors.GetOrAddNew(actor);

    public List<IActor> OrderedActors() => actors.Keys.OrderBy(a => a.Faction).ToList();
    public bool TryRemoveActor(IActor actor) {
        if (actors.ContainsKey(actor)) {
            RemoveActor(actor);
            return true;
        }
        return false;
    }
    public void RemoveActor(IActor actor) {
        actors.Remove(actor);
        actor.Left(this);

        // Raise ActorLeft event
        ActorLeft?.Invoke(actor, EncounterTime);

        // Update caching when actors leave
        if (actor.Flags.HasFlag(ActorFlags.Player))
            UpdatePlayerStatus();
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
        trader.Name = Names.HumanName(actorRng.Seed());
        trader.Faction = Factions.Independent;
        trader.Role = Roles.Trader;

        // Traders should have cargo segments to sell
        var cargoSegments = Inventory.GenerateCargoSegments(actorRng.Seed(), Location, goodsWealth, null);
        trader.Supplies.AddSegments(cargoSegments);

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
        player.Name = Names.HumanName(seed);
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
        enemy.Name = Names.HumanName(actorRng.Seed());
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

        // Randomly assign a civilian role
        var roleRoll = actorRng.NextSingle();
        var role = roleRoll switch {
            < 0.6f => Roles.Trader,    // 60% traders
            < 0.8f => Roles.Traveler,  // 20% travelers
            _ => Roles.Customs         // 20% customs officers
        };

        // Generate working segments
        var workingSegments = Inventory.GenerateCoreSegments(actorRng.Seed(), Location, segmentWealth, [1.2f, 0.6f, 0.8f, 1.0f]);

        // Generate inventory with essentials and cargo
        var newInv = new Inventory();
        newInv.AddEssentials(actorRng.Seed(), Location, crew, 10);
        newInv.AddCargo(actorRng.Seed(), Location, goodsWealth, civilianFaction);
        var cargoSegments = Inventory.GenerateCargoSegments(actorRng.Seed(), Location, goodsWealth, null);
        newInv.AddSegments(cargoSegments);

        // Build crawler with role and component initialization set BEFORE Begin() is called
        var civilian = new Crawler.Builder()
            .WithSeed(actorRng.Seed())
            .WithName(Names.HumanName(actorRng.Seed()))
            .WithFaction(civilianFaction)
            .WithLocation(Location)
            .WithSupplies(newInv)
            .WithSegments(workingSegments)
            .WithRole(role)
            .WithComponentInitialization(true)  // Initialize components before Begin()
            .Build();

        return civilian;
    }

    public Crawler CreateCapital(ulong seed) {
        using var activity = Scope($"GenerateCapital {nameof(Encounter)}");
        var settlement = CreateSettlement(seed);
        settlement.Domes += 2;
        settlement.Flags |= ActorFlags.Capital;
        return settlement;
    }
    public Crawler CreateSettlement(ulong seed) {
        var settlementRng = new XorShift(seed);
        using var activity = Scope($"CreateSettlement {seed} at {Location.Description}");
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
        var settlementName = settlementRng.ChooseRandom(EncounterNames)!;
        Name = settlementName;
        settlement.Name = settlementName;
        return settlement;
    }
    public IActor CreateResource(ulong seed) {
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
        return resourceActor;
    }
    public IActor CreateHazard(ulong seed) {
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
        return hazardActor;
    }
    public Encounter Create(TimePoint currentTime) {
        using var activity = Scope($"Create {nameof(Encounter)}");
        var seed = Rng.Seed();
        IActor? actor = null;
        switch (Location.Type) {
        case EncounterType.Crossroads:
        {
            Name = $"{Faction} Crossroads";
            actor = GenerateFactionActor(seed, Faction);
        }
        break;
        case EncounterType.Settlement: actor = CreateSettlement(seed); break;
        case EncounterType.Resource: actor = CreateResource(seed); break;
        case EncounterType.Hazard: actor = CreateHazard(seed); break;
        }
        if (actor != null) {
            // Add the main encounter actor at the encounter's start time
            AddActorAt(actor, EncounterTime);
        }

        // Spawn dynamic crawlers from encounter start to current time
        SpawnDynamicCrawlers(EncounterTime, currentTime);

        return this;
    }
    public Crawler GenerateFactionActor(ulong seed, Factions faction) {
        Crawler result;

        result = faction switch {
            Factions.Bandit => GenerateBanditActor(seed),
            Factions.Player => GeneratePlayerActor(seed),
            Factions.Independent => GenerateTradeActor(seed),
            var civilian => GenerateCivilianActor(seed, faction),
        };
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

    public TimePoint EncounterTime { get; set; }

    // Tracks if the player is present to toggle between High Precision and Batched modes
    bool _hasPlayer = false;
    void UpdatePlayerStatus() {
        _hasPlayer = actors.Keys.Any(a => a.Flags.HasFlag(ActorFlags.Player));
    }

    /// <summary>
    /// Updates the encounter to the specified time, spawning dynamic crawlers
    /// and firing the EncounterTicked event.
    /// </summary>
    public void UpdateTo(TimePoint currentTime) {
        if (currentTime < EncounterTime) {
            throw new InvalidOperationException($"Cannot move encounter time backwards from {Game.TimeString(EncounterTime)} to {Game.TimeString(currentTime)}");
        }

        if (currentTime == EncounterTime) {
            return; // Already at this time
        }

        TimePoint previousTime = EncounterTime;

        // Spawn dynamic crawlers for the elapsed time period
        SpawnDynamicCrawlers(previousTime, currentTime);

        // Fire the EncounterTicked event for components listening to time passage
        EncounterTicked?.Invoke(previousTime, currentTime);

        // Advance encounter time
        EncounterTime = currentTime;
    }

    // IComparable implementation for use with Scheduler
    public int CompareTo(Encounter? other) {
        if (other == null) return 1;
        if (ReferenceEquals(this, other)) return 0;

        // Compare by identity using GetHashCode as a stable unique identifier
        // The Scheduler uses the Tag only for identity, not ordering
        return GetHashCode().CompareTo(other.GetHashCode());
    }
}
