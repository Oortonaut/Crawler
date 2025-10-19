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
        Generate();
        Tick();
        Game.Instance.RegisterEncounter(this);
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
        result.AddRange(menuItems);

        foreach (var (index, actor) in ActorsExcept(agent)
                     .OrderBy(a => a.Faction)
                     .Index()) {
            string shortcut = "C" + ( char ) ('A' + index);
            var interactions = agent.InteractionsWith(actor).ToList();

            bool enabled = interactions.Any();
            if (interactions.Count == 0) {
                result.Add(new MenuItem(shortcut, actor.Brief(agent) + "\n"));
            } else {
                var show = interactions.Count > 3 ? ShowArg.Hide : ShowArg.Show;
                result.AddRange(InteractionMenuItems(interactions, "", shortcut, show));
                result.Add(MenuItem.Sep);
                result.Add(new ActionMenuItem(shortcut,
                    actor.Brief(agent),
                    args => InteractionMenu(interactions, args, shortcut),
                    enabled ? EnableArg.Enabled : EnableArg.Disabled));
                result.Add(MenuItem.Sep);
            }
            // if (actor is Crawler actorCrawler) {
            //     result.AddRange(actorCrawler.ActionsFor(agent, index));
            // }
        }
        if (result.Count() > 1) {
            return result;
        } else {
            return [];
        }
    }
    IEnumerable<MenuItem> InteractionMenuItems(List<IInteraction> interactions, string args, string prefix, ShowArg show) {
        var counters = new Dictionary<string, int>();
        foreach (var interaction in interactions) {
            int counter;
            var shortcut = $"{prefix}{interaction.OptionCode}";
            if (counters.ContainsKey(shortcut)) {
                counter = ++counters[shortcut];
            } else {
                counter = counters[shortcut] = 1;
            }
            yield return new ActionMenuItem($"{shortcut}{counter}",
                $"{interaction.Description}",
                args => interaction.Perform(args),
                interaction.Enabled().Enable(),
                show);
        }
    }
    int InteractionMenu(List<IInteraction> interactions, string args, string prefix) {
        List<MenuItem> interactionsMenu = [
            MenuItem.Cancel,
            .. InteractionMenuItems(interactions, args, prefix, ShowArg.Show),
        ];

        var (tradeResult, turns) = CrawlerEx.MenuRun($"{Name}", interactionsMenu.ToArray());
        return turns;
    }

    //List<Trade> offers = new();

    // Dynamic crawler management
    float hourlyArrivals => Tuning.Encounter.HourlyArrivals[Location.Type];

    void InitDynamicCrawlers() {
        if (hourlyArrivals <= 0) return;
        
        float expectedCount = hourlyArrivals * (Tuning.Encounter.DynamicCrawlerLifetimeExpectation / 3600f);
        int initialCount = CrawlerEx.SamplePoisson(expectedCount);
        
        for (int i = 0; i < initialCount; i++) {
            int lifetime = ( int ) CrawlerEx.SampleExponential(Tuning.Encounter.DynamicCrawlerLifetimeExpectation);
            AddDynamicCrawler(lifetime);
        }
    }

    // Modified to accept optional lifetime parameter
    void AddDynamicCrawler(int lifetime) {
        var faction = Location.ChooseRandomFaction();
        var crawler = GenerateFactionActor(faction, lifetime);
    }
    void AddDynamicCrawler() {
        int lifetime = CrawlerEx.SamplePoisson(Tuning.Encounter.DynamicCrawlerLifetimeExpectation);
        AddDynamicCrawler(lifetime);
    }
    void UpdateDynamicCrawlers() {
        if (hourlyArrivals <= 0) return;

        long currentTime = Game.Instance.TimeSeconds;
        if (currentTime % 60 != 0) {
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

        // Sample how many crawlers should be present
        int arrivals = CrawlerEx.SamplePoisson(hourlyArrivals / 60);

        if (arrivals > 0) {
            // Add crawlers
            for (int i = 0; i < arrivals; i++) {
                AddDynamicCrawler();
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

        long exitTime = lifetime.HasValue ? Game.Instance.TimeSeconds + lifetime.Value : 0;
        var metadata = new EncounterActor {
            ExitTime = exitTime,
        };
        actors[actor] = metadata;
        actor.To(Location).Visited = true;
        foreach (var other in ActorsExcept(actor)) {
            other.Message($"{actor.Name} enters");
        }
        actor.Message($"You enter {Name}");

        // Check for forced interactions triggered by entry
        CheckForcedInteractions(actor);
    }

    void CheckForcedInteractions(IActor actor) {
        foreach (var other in ActorsExcept(actor)) {
            // Check if other has forced interactions for the new actor
            var otherForced = other.ForcedInteractions(actor).ToList();
            if (otherForced.Any()) {
                PresentForcedInteractions(other, actor, otherForced);
            }

            // Check if new actor has forced interactions for existing actors
            var actorForced = actor.ForcedInteractions(other).ToList();
            if (actorForced.Any()) {
                PresentForcedInteractions(actor, other, actorForced);
            }
        }
    }

    void PresentForcedInteractions(IActor agent, IActor subject, List<IInteraction> interactions) {
        if (subject.Faction == Faction.Player) {
            // Present to player immediately
            var menuItems = new List<MenuItem> { MenuItem.Cancel };
            foreach (var (index, interaction) in interactions.Index()) {
                string shortcut = $"F{index + 1}";
                menuItems.Add(new ActionMenuItem(shortcut,
                    interaction.Description,
                    args => interaction.Perform(args),
                    interaction.Enabled().Enable(),
                    ShowArg.Show));
            }
            var (result, turns) = CrawlerEx.MenuRun($"{agent.Name} Demands", menuItems.ToArray());
        } else {
            // NPC - auto-execute first available interaction
            var first = interactions.FirstOrDefault(i => i.Enabled());
            first?.Perform();
        }
    }
    public List<IActor> OrderedActors() => actors.Keys.OrderBy(a => a.Faction).ToList();
    public virtual void RemoveActor(IActor actor) {
        actor.Message($"You leave {Name}");
        foreach (var other in ActorsExcept(actor)) {
            other.Message($"{actor.Name} leaves");
        }
        actors.Remove(actor);
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
        var trader = Crawler.NewRandom(Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.8f, 1, 1], Faction.Independent);
        trader.Faction = Faction.Independent;
        trader.StoredProposals.AddRange(trader.MakeTradeProposals( 0.25f, trader.Faction));
        trader.UpdateSegments();
        trader.Recharge(20);
        return trader;
    }
    public Crawler GeneratePlayerActor() {
        float wealth = Location.Wealth * 1.0f;
        int crew = (int)Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.65f;
        float segmentWealth = wealth * 0.5f;
        var player = Crawler.NewRandom(Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1, 1], Faction.Player);
        player.Flags |= EActorFlags.Player;
        player.Faction = Faction.Player;
        player.Recharge(20);
        return player;
    }
    public Crawler GenerateBanditActor() {
        float wealth = Location.Wealth * 0.8f;
        int crew = ( int ) Math.Sqrt(wealth) / 3;
        float goodsWealth = wealth * 0.6f;
        float segmentWealth = wealth * 0.5f;
        var enemy = Crawler.NewRandom(Location, crew, 10, goodsWealth, segmentWealth, [1, 1, 1.2f, 0.8f], Faction.Bandit);
        enemy.Faction = Faction.Bandit;
        enemy.Recharge(20);

        return enemy;
    }

    public Crawler GenerateCivilianActor(Faction civilianFaction) {
        // Similar to Trade but with faction-specific policies
        float wealth = Location.Wealth * 0.75f;
        int crew = Math.Max(1, (int)(wealth / 40));
        float goodsWealth = wealth * 0.375f * 0.5f;
        float segmentWealth = wealth * (1.0f - 0.75f);
        var civilian = Crawler.NewRandom(Location, crew, 10, goodsWealth, segmentWealth, [1.2f, 0.6f, 0.8f, 1.0f], civilianFaction);
        civilian.Faction = civilianFaction;
        civilian.StoredProposals.AddRange(civilian.MakeTradeProposals(0.25f, civilian.Faction));
        civilian.UpdateSegments();
        civilian.Recharge(20);
        return civilian;
    }

    public void GenerateSettlement() {
        float t = Location.Position.Y / ( float ) Location.Map.Height;
        int domes = (int)(1 + Location.Population / 50);
        int crew = domes * 10;
        // wealth is also population scaled,
        float goodsWealth = Location.Wealth * domes * 0.5f;
        float segmentWealth = Location.Wealth * domes * 0.25f;
        var settlement = Crawler.NewRandom(Location, crew, 15, goodsWealth, segmentWealth, [4, 0, 1, 3]);
        settlement.Flags |= EActorFlags.Settlement;
        settlement.Flags &= ~EActorFlags.Mobile;
        settlement.Faction = Faction;
        settlement.Recharge(20);
        var proposals = settlement.MakeTradeProposals( 1, settlement.Faction);
        settlement.StoredProposals.AddRange(proposals);
        settlement.UpdateSegments();

        IEnumerable<string> EncounterNames = ["Settlement"];
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
        Name = EncounterNames.ChooseRandom() ?? "Settlement";
        settlement.Name = $"{Name} Settlement ({domes:F0} Domes)";
        AddActor(settlement);
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
        resourceActor.StoredProposals.Add(new ProposeHarvestFree(resourceActor, Inv, "H", verb));
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
        hazardActor.StoredProposals.Add(new ProposeLootRisk(
            hazardActor,
            Risked,
            Tuning.Game.hazardNegativePayoffChance));
        AddActor(hazardActor);
    }
    public void Generate() {
        switch (Location.Type) {
        case EncounterType.Crossroads: GenerateFactionActor(Faction, null); break;
        case EncounterType.Settlement: GenerateSettlement(); break;
        case EncounterType.Resource: GenerateResource(); break;
        case EncounterType.Hazard: GenerateHazard(); break;
        }
        InitDynamicCrawlers();
    }
    public IActor GenerateFactionActor(Faction faction, int? lifetime) {
        Name = $"{faction} Crossroads";
        Crawler result;

        if (faction.IsCivilian()) {
            result = GenerateCivilianActor(faction);
        } else {
            result = faction switch {
                Faction.Bandit => GenerateBanditActor(),
                Faction.Player => GeneratePlayerActor(),
                Faction.Independent => GenerateTradeActor(),
                _ => throw new ArgumentException($"Unexpected faction: {faction}")
            };
        }
        result.Recharge(20);
        AddActor(result, lifetime);
        return result;
    }

    public IEnumerable<IActor> ActorsExcept(IActor actor) => Game.Instance.Moving ? [] : Actors.Where(a => a != actor);
    public IEnumerable<IActor> CrawlersExcept(IActor actor) => ActorsExcept(actor).OfType<Crawler>();
    public void Tick() {
        UpdateDynamicCrawlers();

        foreach (var actor in actors.Keys) {
            if (actor.EndState is { } state) {
            } else {
                actor.Tick();
            }
        }
        foreach (var actor in actors.Keys) {
            actor.Tick(ActorsExcept(actor));
        }

        // Periodically check for forced interactions (e.g., demands during standoffs)
        if (Game.Instance.TimeSeconds % 300 == 0) { // Every 5 minutes
            CheckPeriodicForcedInteractions();
        }
    }

    void CheckPeriodicForcedInteractions() {
        var actorList = actors.Keys.ToList();
        foreach (var actor in actorList) {
            foreach (var other in ActorsExcept(actor)) {
                var forced = actor.ForcedInteractions(other).ToList();
                if (forced.Any()) {
                    PresentForcedInteractions(actor, other, forced);
                }
            }
        }
    }
}
