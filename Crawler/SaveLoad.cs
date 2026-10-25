using YamlDotNet.Serialization;
using System.Numerics;
using System.Drawing;

namespace Crawler;

[YamlSerializable]
public class SaveGameData {
    public string Version { get; set; } = "1.0";
    public ulong Seed { get; set; }
    public long Hour { get; set; }
    public int AP { get; set; }
    public int TurnAP { get; set; }
    public Vector2 CurrentLocationPos { get; set; }
    public SavedCrawler Player { get; set; } = new();
    public SavedMap Map { get; set; } = new();
    public bool Quit { get; set; }
    // RNG state
    public ulong RngState { get; set; }
    public ulong GaussianRngState { get; set; }
    public bool GaussianPrimed { get; set; }
    public double GaussianZSin { get; set; }
}

[YamlSerializable]
public class SavedCrawler {
    public string Name { get; set; } = "";
    public Factions Faction { get; set; }
    public Vector2 LocationPos { get; set; }
    /// <summary>One saved inventory per non-empty account stack.</summary>
    public Dictionary<Account, SavedInventory> Inventory { get; set; } = new();
    // Saves from before accounts: Supplies loads as the Supplies stack, Cargo as Supplies|Trade
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public SavedInventory? Supplies { get; set; }
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public SavedInventory? Cargo { get; set; }
    public float Markup { get; set; } = 1.0f;
    public float Spread { get; set; } = 1.0f;
    public Dictionary<string, SavedActorRelation> Relations { get; set; } = new();
    public Dictionary<string, SavedActorLocation> VisitedLocations { get; set; } = new();
    public int EvilPoints { get; set; } = 0;
    public EEndState? EndState { get; set; }
    public string EndMessage { get; set; } = "";
    // RNG state
    public ulong RngState { get; set; }
    public ulong GaussianRngState { get; set; }
    public bool GaussianPrimed { get; set; }
    public double GaussianZSin { get; set; }
}

[YamlSerializable]
public class SavedInventory {
    // Essential/Currency
    public float Scrap { get; set; }
    public float Fuel { get; set; }
    public float Crew { get; set; }
    public float Morale { get; set; }
    public float Isotopes { get; set; }
    public float Nanomaterials { get; set; }

    // Life support
    public float Air { get; set; }
    public float Water { get; set; }
    public float Rations { get; set; }

    // Raw materials
    public float Biomass { get; set; }
    public float Ore { get; set; }
    public float Silicates { get; set; }

    // Refined materials
    public float Metal { get; set; }
    public float Chemicals { get; set; }
    public float Glass { get; set; }

    // Parts
    public float Ceramics { get; set; }
    public float Polymers { get; set; }
    public float Alloys { get; set; }
    public float Electronics { get; set; }
    public float Explosives { get; set; }

    // Consumer Goods
    public float Medicines { get; set; }
    public float Textiles { get; set; }
    public float Gems { get; set; }
    public float Toys { get; set; }
    public float Machines { get; set; }
    public float AiCores { get; set; }
    public float Media { get; set; }

    // Vice & Contraband
    public float Liquor { get; set; }
    public float Stims { get; set; }
    public float Downers { get; set; }
    public float Trips { get; set; }
    public float SmallArms { get; set; }

    // Religious items
    public float Idols { get; set; }
    public float Texts { get; set; }
    public float Relics { get; set; }

    // Segments
    public List<SavedSegment> Segments { get; set; } = new();
}

[YamlSerializable]
public class SavedSegment {
    public ulong Seed { get; set; }
    public string DefName { get; set; } = "";
    public int Hits { get; set; }
    public float Charge { get; set; }
    public float ShieldLeft { get; set; }
    public bool Packaged { get; set; } = true;
    public bool Activated { get; set; } = true;
}

[YamlSerializable]
public class SavedActorRelation {
    public bool Hostile { get; set; }
    public bool Surrendered { get; set; }
    public int DamageCreated { get; set; }
    public int DamageInflicted { get; set; }
    public int DamageTaken { get; set; }
}

[YamlSerializable]
public class SavedActorLocation {
    public bool Visited { get; set; } = false;
}


[YamlSerializable]
public class SavedMap {
    public int Width { get; set; }
    public int Height { get; set; }
    public ulong TerrainSeed { get; set; }
    public List<SavedLocation> Locations { get; set; } = new();
    public List<SavedFactionData> FactionData { get; set; } = new();
}

[YamlSerializable]
public class SavedFactionData {
    public Factions Faction { get; set; }
    public required string Name { get; set; }
    public required Color Color { get; set; }
    public SavedCapital? Capital { get; set; }
}

[YamlSerializable]
public class SavedCapital {
    public required SavedCrawler Settlement { get; set; }
    public required string Name { get; set; }
    public float Influence { get; set; }
}

[YamlSerializable]
public class SavedLocation {
    public Vector2 Position { get; set; }
    public EncounterType Type { get; set; }
    public float Wealth { get; set; }
    public ulong Seed { get; set; }
    public int Population { get; set; }
    public Factions ControllingFaction { get; set; } = Factions.Independent;
    public Dictionary<Commodity, float> LocalMarkup { get; set; } = new();
    public Dictionary<SegmentKind, float> LocalSegmentRates { get; set; } = new();
    public SavedEncounter? Encounter { get; set; }
}

[YamlSerializable]
public class SavedEncounter {
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Factions Faction { get; set; }
    public List<SavedCrawler> Actors { get; set; } = new();
}

public static class SaveLoadExtensions {
    public static SaveGameData ToSaveData(this Game game) {
        return new SaveGameData {
            Version = "1.0",
            Seed = game.GetSeed(),
            Hour = game.GetPlayer().Time.Elapsed,
            CurrentLocationPos = game.PlayerLocation.Position,
            Player = game.GetPlayer().ToSaveData(),
            Map = game.GetMap().ToSaveData(),
            Quit = game.GetQuit(),
            RngState = game.GetRngState(),
            GaussianRngState = game.GetGaussianRngState(),
            GaussianPrimed = game.GetGaussianPrimed(),
            GaussianZSin = game.GetGaussianZSin()
        };
    }

    public static SavedCrawler ToSaveData(this Crawler crawler) {
        // Use the new Data structure
        var data = (Crawler.Data)crawler.ToData();

        var tradeComponent = crawler.Components.OfType<TradeOfferComponent>().FirstOrDefault();
        return new SavedCrawler {
            Name = data.Init.Name,
            Faction = data.Init.Faction,
            LocationPos = data.Init.Location.Position,
            Inventory = data.Init.Inventory.ToSaveData(),
            Markup = tradeComponent?.Markup ?? 1.0f,
            Spread = tradeComponent?.Spread ?? 1.0f,
            Relations = data.ActorRelations.ToDictionary(
                kvp => kvp.Key,
                kvp => new SavedActorRelation {
                    Hostile = kvp.Value.Flags.HasFlag(ActorToActor.EFlags.Hostile),
                    Surrendered = kvp.Value.Flags.HasFlag(ActorToActor.EFlags.Surrendered),
                    DamageCreated = kvp.Value.DamageCreated,
                    DamageInflicted = kvp.Value.DamageInflicted,
                    DamageTaken = kvp.Value.DamageTaken
                }
            ),
            VisitedLocations = data.LocationRelations.ToDictionary(
                kvp => kvp.Key,
                kvp => new SavedActorLocation { Visited = kvp.Value.Visited }
            ),
            EvilPoints = data.EvilPoints,
            EndState = data.EndState,
            EndMessage = data.EndMessage,
            RngState = data.Rng.State,
            GaussianRngState = data.Gaussian.Rng.State,
            GaussianPrimed = data.Gaussian.Primed,
            GaussianZSin = data.Gaussian.ZSin
        };
    }

    public static Dictionary<Account, SavedInventory> ToSaveData(this Inventory inventory) =>
        AccountEx.Stacks
            .Where(stack => inventory.Stacks.Any(entry => entry.Stack == stack) || inventory.Segments.Any(s => s.Account == stack))
            .ToDictionary(stack => stack, stack => inventory.ToSaveData(stack));

    public static SavedInventory ToSaveData(this Inventory inventory, Account stack) {
        return new SavedInventory {
            // Essential/Currency
            Scrap = inventory.Stack(Commodity.Scrap, stack),
            Fuel = inventory.Stack(Commodity.Fuel, stack),
            Crew = inventory.Stack(Commodity.Crew, stack),
            Morale = inventory.Stack(Commodity.Morale, stack),
            Isotopes = inventory.Stack(Commodity.Isotopes, stack),
            Nanomaterials = inventory.Stack(Commodity.Nanomaterials, stack),

            // Life support
            Air = inventory.Stack(Commodity.Air, stack),
            Water = inventory.Stack(Commodity.Water, stack),
            Rations = inventory.Stack(Commodity.Rations, stack),

            // Raw materials
            Biomass = inventory.Stack(Commodity.Biomass, stack),
            Ore = inventory.Stack(Commodity.Ore, stack),
            Silicates = inventory.Stack(Commodity.Silicates, stack),

            // Refined materials
            Metal = inventory.Stack(Commodity.Metal, stack),
            Chemicals = inventory.Stack(Commodity.Chemicals, stack),
            Glass = inventory.Stack(Commodity.Glass, stack),

            // Parts
            Ceramics = inventory.Stack(Commodity.Ceramics, stack),
            Polymers = inventory.Stack(Commodity.Polymers, stack),
            Alloys = inventory.Stack(Commodity.Alloys, stack),
            Electronics = inventory.Stack(Commodity.Electronics, stack),
            Explosives = inventory.Stack(Commodity.Explosives, stack),

            // Consumer Goods
            Medicines = inventory.Stack(Commodity.Medicines, stack),
            Textiles = inventory.Stack(Commodity.Textiles, stack),
            Gems = inventory.Stack(Commodity.Gems, stack),
            Toys = inventory.Stack(Commodity.Toys, stack),
            Machines = inventory.Stack(Commodity.Machines, stack),
            AiCores = inventory.Stack(Commodity.AiCores, stack),
            Media = inventory.Stack(Commodity.Media, stack),

            // Vice & Contraband
            Liquor = inventory.Stack(Commodity.Liquor, stack),
            Stims = inventory.Stack(Commodity.Stims, stack),
            Downers = inventory.Stack(Commodity.Downers, stack),
            Trips = inventory.Stack(Commodity.Trips, stack),
            SmallArms = inventory.Stack(Commodity.SmallArms, stack),

            // Religious items
            Idols = inventory.Stack(Commodity.Idols, stack),
            Texts = inventory.Stack(Commodity.Texts, stack),
            Relics = inventory.Stack(Commodity.Relics, stack),

            // Segments
            Segments = inventory.Segments.Where(s => s.Account == stack).Select(s => s.ToSaveData()).ToList()
        };
    }

    public static SavedSegment ToSaveData(this Segment segment) {
        var saved = new SavedSegment {
            Seed = segment.Seed,
            DefName = segment.SegmentDef.Name,
            Hits = segment.Hits,
            Packaged = segment.Packaged,
            Activated = segment.Activated
        };

        if (segment is ReactorSegment reactor) {
            saved.Charge = reactor.Charge;
        }
        if (segment is ShieldSegment shield) {
            saved.ShieldLeft = shield.ShieldLeft;
        }

        return saved;
    }

    public static SavedActorRelation ToSaveData(this ActorToActor toActor) {
        return new SavedActorRelation {
            Hostile = toActor.Hostile,
            Surrendered = toActor.Surrendered,
            DamageCreated = toActor.DamageCreated,
            DamageInflicted = toActor.DamageInflicted,
            DamageTaken = toActor.DamageTaken
        };
    }

    public static SavedActorLocation ToSaveData(this LocationActor locationActorLoc) {
        return new SavedActorLocation {
            Visited = locationActorLoc.Visited,
        };
    }

    public static SavedMap ToSaveData(this Map map) {
        return new SavedMap {
            Width = map.Width,
            Height = map.Height,
            TerrainSeed = map.Seed,
            Locations = map.AllLocations.Select(l => l.ToSaveData()).ToList(),
            FactionData = map.FactionData.Pairs()
                .TakeWhile(kvp => kvp.Value != null)
                .Select(kvp => kvp.Value!.ToSaveData(kvp.Key))
                .ToList()
        };
    }

    public static SavedFactionData ToSaveData(this FactionData data, Factions faction) {
        return new SavedFactionData {
            Faction = faction,
            Name = data.Name,
            Color = data.Color,
            Capital = data.Capital?.ToSaveData()
        };
    }

    public static SavedCapital ToSaveData(this Capital capital) {
        return new SavedCapital {
            Settlement = capital.Settlement.ToSaveData(),
            Name = capital.Name,
            Influence = capital.Influence,
        };
    }

    public static SavedLocation ToSaveData(this Location location) {
        return new SavedLocation {
            Position = location.Position,
            Type = location.Type,
            Wealth = location.Wealth,
            Seed = location.Seed,
            Population = location.Population,
            ControllingFaction = location.ControllingFaction,
            LocalMarkup = location.LocalMarkup.Pairs().ToDictionary(kv => kv.Key, kv => kv.Value),
            LocalSegmentRates = location.LocalSegmentRates.Pairs().ToDictionary(kv => kv.Key, kv => kv.Value),
            Encounter = location.HasEncounter ? location.GetEncounter().ToSaveData() : null
        };
    }

    public static SavedEncounter ToSaveData(this Encounter encounter) {
        return new SavedEncounter {
            Name = encounter.Name,
            Description = encounter.Description,
            Faction = encounter.Faction,
            Actors = encounter.Actors.OfType<Crawler>().Select(c => c.ToSaveData()).ToList()
        };
    }

    public static Map ToGameMap(this SavedMap savedMap) {
        // Use the saved terrain seed for reconstruction
        var map = new Map(savedMap.TerrainSeed, savedMap.Height, savedMap.Width);

        // Clear generated locations and restore saved ones
        // Note: Map generates locations in constructor, we need to add saved locations
        // For now, we'll match by position and restore state
        foreach (var savedLocation in savedMap.Locations) {
            var location = savedLocation.ToGameLocation(map);
            // The map already has locations from generation; we need to restore their state
            var existingLocation = map.AllLocations
                .FirstOrDefault(l => Vector2.Distance(l.Position, savedLocation.Position) < 0.001f);

            if (existingLocation != null) {
                // Restore mutable state
                existingLocation.ControllingFaction = savedLocation.ControllingFaction;
                existingLocation.Population = savedLocation.Population;

                // Restore encounter if present
                if (savedLocation.Encounter != null) {
                    var encounter = savedLocation.Encounter.ToGameEncounter(existingLocation);
                    existingLocation.SetEncounter(encounter);
                }
            }
        }

        // Restore faction data
        foreach (var savedFactionData in savedMap.FactionData) {
            var capital = savedFactionData.Capital?.ToGameCapital(map);
            map.FactionData[savedFactionData.Faction] = new FactionData(
                savedFactionData.Name,
                savedFactionData.Color,
                capital
            );
        }

        // Build actor lookup table from all encounters for Relations restoration
        var actorLookup = new Dictionary<string, IActor>();
        foreach (var location in map.AllLocations.Where(l => l.HasEncounter)) {
            foreach (var actor in location.GetEncounter().Actors.OfType<Crawler>()) {
                actorLookup[actor.Name] = actor;
            }
        }

        // Second pass: Restore Relations for all crawlers
        foreach (var location in map.AllLocations.Where(l => l.HasEncounter)) {
            var encounter = location.GetEncounter();
            foreach (var crawler in encounter.Actors.OfType<Crawler>()) {
                var savedCrawler = savedMap.Locations
                    .SelectMany(l => l.Encounter?.Actors ?? [])
                    .FirstOrDefault(sc => sc.Name == crawler.Name);

                if (savedCrawler != null) {
                    savedCrawler.RestoreRelationsTo(crawler, actorLookup);
                }
            }
        }

        return map;
    }

    public static Capital ToGameCapital(this SavedCapital savedCapital, Map map) {
        var settlement = savedCapital.Settlement.ToGameCrawler(map);
        return new Capital(savedCapital.Name, settlement, savedCapital.Influence);
    }

    public static Location ToGameLocation(this SavedLocation savedLocation, Map map) {
        var encounterTime = Tuning.StartGameTime; // TODO: Save this time
        var location = new Location(savedLocation.Seed,
            map, savedLocation.Position, savedLocation.Type, savedLocation.Wealth,
            loc => savedLocation.Encounter?.ToGameEncounter(loc) ??
                   new Encounter(savedLocation.Seed, loc).Create(encounterTime));
        location.ControllingFaction = savedLocation.ControllingFaction;
        location.Population = savedLocation.Population;
        return location;
    }

    public static Encounter ToGameEncounter(this SavedEncounter savedEncounter, Location location) {
        var encounter = new Encounter(location.Seed, location, savedEncounter.Faction) {
            Name = savedEncounter.Name,
            Description = savedEncounter.Description
        };

        // Restore actors
        foreach (var savedActor in savedEncounter.Actors) {
            var actor = savedActor.ToGameCrawler(location.Map);
            encounter.AddActor(actor);
        }

        return encounter;
    }

    public static Location FindLocationByPosition(this Map map, Vector2 position) {
        foreach (var location in map.AllLocations) {
            if (Vector2.Distance(location.Position, position) < 0.001f) {
                return location;
            }
        }
        throw new InvalidOperationException($"Location not found at position {position}");
    }

    public static Crawler ToGameCrawler(this SavedCrawler savedCrawler, Map map) {
        var location = map.FindLocationByPosition(savedCrawler.LocationPos);
        var inventory = new Inventory();
        foreach (var (stack, savedInv) in savedCrawler.Inventory) {
            savedInv.AddTo(inventory, stack);
        }
        savedCrawler.Supplies?.AddTo(inventory, Account.Supplies);
        savedCrawler.Cargo?.AddTo(inventory, Account.Supplies | Account.Trade);

        // Create Init structure
        var init = new Crawler.Init {
            Seed = savedCrawler.RngState, // Use saved RNG state as seed
            Name = savedCrawler.Name,
            Brief = "",
            Faction = savedCrawler.Faction,
            Location = location,
            Inventory = inventory,
            Role = Roles.None, // TODO: Save/restore role
            InitializeComponents = false,
            WorkingSegments = new List<Segment>() // Will be restored from segments in supplies
        };

        // Create Data structure
        var actorRelations = savedCrawler.Relations.ToDictionary(
            kvp => kvp.Key,
            kvp => new ActorToActor.Data {
                Flags = (kvp.Value.Hostile ? ActorToActor.EFlags.Hostile : 0) |
                        (kvp.Value.Surrendered ? ActorToActor.EFlags.Surrendered : 0),
                DamageCreated = kvp.Value.DamageCreated,
                DamageInflicted = kvp.Value.DamageInflicted,
                DamageTaken = kvp.Value.DamageTaken,
                Ultimatum = null
            }
        );

        var locationRelations = savedCrawler.VisitedLocations.ToDictionary(
            kvp => kvp.Key,
            kvp => new LocationActor.Data {
                Visited = kvp.Value.Visited
            }
        );

        var data = new Crawler.Data {
            Init = init,
            Rng = new XorShift.Data { State = savedCrawler.RngState },
            Gaussian = new GaussianSampler.Data {
                Rng = new XorShift.Data { State = savedCrawler.GaussianRngState },
                Primed = savedCrawler.GaussianPrimed,
                ZSin = savedCrawler.GaussianZSin
            },
            Time = 0, // TODO: Save/restore time
            LastTime = 0,
            EndState = savedCrawler.EndState,
            EndMessage = savedCrawler.EndMessage,
            ActorRelations = actorRelations,
            LocationRelations = locationRelations,
            WorkingSegments = new List<Segment.Data>(), // TODO: Extract from segments
            EvilPoints = savedCrawler.EvilPoints
        };

        // Create crawler using Init + Data constructor
        var crawler = new Crawler(init, data);

        // Restore markup/spread to TradeOfferComponent if it exists
        var tradeComponent = crawler.Components.OfType<TradeOfferComponent>().FirstOrDefault();
        if (tradeComponent != null) {
            tradeComponent.Markup = savedCrawler.Markup;
            tradeComponent.Spread = savedCrawler.Spread;
        }

        // Call Begin to initialize
        crawler.Begin();

        return crawler;
    }

    public static void AddTo(this SavedInventory savedInv, Inventory inventory, Account stack) {
        // Essential/Currency
        inventory.Add(Commodity.Scrap, savedInv.Scrap, stack);
        inventory.Add(Commodity.Fuel, savedInv.Fuel, stack);
        inventory.Add(Commodity.Crew, savedInv.Crew, stack);
        inventory.Add(Commodity.Morale, savedInv.Morale, stack);
        inventory.Add(Commodity.Isotopes, savedInv.Isotopes, stack);
        inventory.Add(Commodity.Nanomaterials, savedInv.Nanomaterials, stack);

        // Life support
        inventory.Add(Commodity.Air, savedInv.Air, stack);
        inventory.Add(Commodity.Water, savedInv.Water, stack);
        inventory.Add(Commodity.Rations, savedInv.Rations, stack);

        // Raw materials
        inventory.Add(Commodity.Biomass, savedInv.Biomass, stack);
        inventory.Add(Commodity.Ore, savedInv.Ore, stack);
        inventory.Add(Commodity.Silicates, savedInv.Silicates, stack);

        // Refined materials
        inventory.Add(Commodity.Metal, savedInv.Metal, stack);
        inventory.Add(Commodity.Chemicals, savedInv.Chemicals, stack);
        inventory.Add(Commodity.Glass, savedInv.Glass, stack);

        // Parts
        inventory.Add(Commodity.Ceramics, savedInv.Ceramics, stack);
        inventory.Add(Commodity.Polymers, savedInv.Polymers, stack);
        inventory.Add(Commodity.Alloys, savedInv.Alloys, stack);
        inventory.Add(Commodity.Electronics, savedInv.Electronics, stack);
        inventory.Add(Commodity.Explosives, savedInv.Explosives, stack);

        // Consumer Goods
        inventory.Add(Commodity.Medicines, savedInv.Medicines, stack);
        inventory.Add(Commodity.Textiles, savedInv.Textiles, stack);
        inventory.Add(Commodity.Gems, savedInv.Gems, stack);
        inventory.Add(Commodity.Toys, savedInv.Toys, stack);
        inventory.Add(Commodity.Machines, savedInv.Machines, stack);
        inventory.Add(Commodity.AiCores, savedInv.AiCores, stack);
        inventory.Add(Commodity.Media, savedInv.Media, stack);

        // Vice & Contraband
        inventory.Add(Commodity.Liquor, savedInv.Liquor, stack);
        inventory.Add(Commodity.Stims, savedInv.Stims, stack);
        inventory.Add(Commodity.Downers, savedInv.Downers, stack);
        inventory.Add(Commodity.Trips, savedInv.Trips, stack);
        inventory.Add(Commodity.SmallArms, savedInv.SmallArms, stack);

        // Religious items
        inventory.Add(Commodity.Idols, savedInv.Idols, stack);
        inventory.Add(Commodity.Texts, savedInv.Texts, stack);
        inventory.Add(Commodity.Relics, savedInv.Relics, stack);

        // Segments
        foreach (var savedSegment in savedInv.Segments) {
            var segment = savedSegment.ToGameSegment();
            inventory.Add(segment, stack);
        }
    }

    public static Segment ToGameSegment(this SavedSegment savedSegment) {
        var segmentDef = SegmentEx.NameLookup[savedSegment.DefName];
        var segment = segmentDef.NewSegment(savedSegment.Seed);

        segment.Hits = savedSegment.Hits;
        segment.Packaged = savedSegment.Packaged;
        segment.Activated = savedSegment.Activated;

        if (segment is ReactorSegment reactor) {
            reactor.Charge = savedSegment.Charge;
        }
        if (segment is ShieldSegment shield) {
            shield.ShieldLeft = savedSegment.ShieldLeft;
        }

        return segment;
    }

    public static void RestoreRelationsTo(this SavedCrawler savedCrawler, Crawler crawler, Dictionary<string, IActor> actorLookup) {
        // Convert SavedCrawler relations to ActorToActor.Data
        var actorRelations = savedCrawler.Relations.ToDictionary(
            kvp => kvp.Key,
            kvp => new ActorToActor.Data {
                Flags = (kvp.Value.Hostile ? ActorToActor.EFlags.Hostile : 0) |
                        (kvp.Value.Surrendered ? ActorToActor.EFlags.Surrendered : 0),
                DamageCreated = kvp.Value.DamageCreated,
                DamageInflicted = kvp.Value.DamageInflicted,
                DamageTaken = kvp.Value.DamageTaken,
                Ultimatum = null
            }
        );

        // Use the ActorBase helper method to restore relations
        crawler.RestoreActorRelations(actorRelations, actorLookup);
    }

}
