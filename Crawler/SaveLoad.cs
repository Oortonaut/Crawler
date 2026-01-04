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
    public SavedInventory Supplies { get; set; } = new();
    public SavedInventory Cargo { get; set; } = new();
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
    public List<SavedSector> Sectors { get; set; } = new();
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
public class SavedSector {
    public int X { get; set; }
    public int Y { get; set; }
    public TerrainType Terrain { get; set; }
    public float Wealth { get; set; }
    public Factions ControllingFaction { get; set; } = Factions.Independent;
    public Dictionary<Commodity, float> LocalMarkup { get; set; } = new();
    public Dictionary<SegmentKind, float> LocalSegmentRates { get; set; } = new();
    public List<SavedLocation> Locations { get; set; } = new();
    // RNG state
    public ulong RngState { get; set; }
    public ulong GaussianRngState { get; set; }
    public bool GaussianPrimed { get; set; }
    public double GaussianZSin { get; set; }
}

[YamlSerializable]
public class SavedLocation {
    public Vector2 Position { get; set; }
    public TerrainType Terrain { get; set; }
    public EncounterType Type { get; set; }
    public float Wealth { get; set; }
    public ulong Seed { get; set; }
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
            Supplies = data.Init.Supplies.ToSaveData(),
            Cargo = data.Init.Cargo.ToSaveData(),
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

    public static SavedInventory ToSaveData(this Inventory inventory) {
        return new SavedInventory {
            // Essential/Currency
            Scrap = inventory[Commodity.Scrap],
            Fuel = inventory[Commodity.Fuel],
            Crew = inventory[Commodity.Crew],
            Morale = inventory[Commodity.Morale],
            Isotopes = inventory[Commodity.Isotopes],
            Nanomaterials = inventory[Commodity.Nanomaterials],

            // Life support
            Air = inventory[Commodity.Air],
            Water = inventory[Commodity.Water],
            Rations = inventory[Commodity.Rations],

            // Raw materials
            Biomass = inventory[Commodity.Biomass],
            Ore = inventory[Commodity.Ore],
            Silicates = inventory[Commodity.Silicates],

            // Refined materials
            Metal = inventory[Commodity.Metal],
            Chemicals = inventory[Commodity.Chemicals],
            Glass = inventory[Commodity.Glass],

            // Parts
            Ceramics = inventory[Commodity.Ceramics],
            Polymers = inventory[Commodity.Polymers],
            Alloys = inventory[Commodity.Alloys],
            Electronics = inventory[Commodity.Electronics],
            Explosives = inventory[Commodity.Explosives],

            // Consumer Goods
            Medicines = inventory[Commodity.Medicines],
            Textiles = inventory[Commodity.Textiles],
            Gems = inventory[Commodity.Gems],
            Toys = inventory[Commodity.Toys],
            Machines = inventory[Commodity.Machines],
            AiCores = inventory[Commodity.AiCores],
            Media = inventory[Commodity.Media],

            // Vice & Contraband
            Liquor = inventory[Commodity.Liquor],
            Stims = inventory[Commodity.Stims],
            Downers = inventory[Commodity.Downers],
            Trips = inventory[Commodity.Trips],
            SmallArms = inventory[Commodity.SmallArms],

            // Religious items
            Idols = inventory[Commodity.Idols],
            Texts = inventory[Commodity.Texts],
            Relics = inventory[Commodity.Relics],

            // Segments
            Segments = inventory.Segments.Select(s => s.ToSaveData()).ToList()
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
        var savedSectors = new List<SavedSector>();

        for (int y = 0; y < map.Height; y++) {
            for (int x = 0; x < map.Width; x++) {
                var sector = map.GetSector(x, y);
                savedSectors.Add(sector.ToSaveData());
            }
        }

        return new SavedMap {
            Width = map.Width,
            Height = map.Height,
            Sectors = savedSectors,
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

    public static SavedSector ToSaveData(this Sector sector) {
        return new SavedSector {
            X = sector.X,
            Y = sector.Y,
            Terrain = sector.Terrain,
            Wealth = 0,
            ControllingFaction = sector.ControllingFaction,
            LocalMarkup = sector.LocalMarkup.Pairs().ToDictionary(kv => kv.Key, kv => kv.Value),
            LocalSegmentRates = sector.LocalSegmentRates.Pairs().ToDictionary(kv => kv.Key, kv => kv.Value),
            Locations = sector.Locations.Select(l => l.ToSaveData()).ToList(),
            RngState = sector.GetRngState(),
            GaussianRngState = sector.GetGaussianRngState(),
            GaussianPrimed = sector.GetGaussianPrimed(),
            GaussianZSin = sector.GetGaussianZSin()
        };
    }

    public static SavedLocation ToSaveData(this Location location) {
        return new SavedLocation {
            Position = location.Position,
            Terrain = location.Terrain,
            Type = location.Type,
            Wealth = location.Wealth,
            Seed = location.Seed,
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
        // Use a seed based on map dimensions for reconstruction
        var map = new Map((ulong)(savedMap.Height * 1000 + savedMap.Width), savedMap.Height, savedMap.Width);

        // Restore sector terrain and locations
        foreach (var savedSector in savedMap.Sectors) {
            var sector = map.GetSector(savedSector.X, savedSector.Y);
            sector.Terrain = savedSector.Terrain;
            sector.ControllingFaction = savedSector.ControllingFaction;

            // Restore RNG state
            sector.SetRngState(savedSector.RngState);
            sector.SetGaussianRngState(savedSector.GaussianRngState);
            sector.SetGaussianPrimed(savedSector.GaussianPrimed);
            sector.SetGaussianZSin(savedSector.GaussianZSin);

            // Restore LocalMarkup
            foreach (var kvp in savedSector.LocalMarkup) {
                sector.LocalMarkup[kvp.Key] = kvp.Value;
            }

            // Restore LocalSegmentRates
            foreach (var kvp in savedSector.LocalSegmentRates) {
                sector.LocalSegmentRates[kvp.Key] = kvp.Value;
            }

            // Restore locations
            foreach (var savedLocation in savedSector.Locations) {
                var location = savedLocation.ToGameLocation(sector);
                sector.Locations.Add(location);
            }
        }

        // Restore faction data (must happen after locations are restored)
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
        for (int y = 0; y < map.Height; y++) {
            for (int x = 0; x < map.Width; x++) {
                var sector = map.GetSector(x, y);
                foreach (var location in sector.Locations.Where(l => l.HasEncounter)) {
                    foreach (var actor in location.GetEncounter().Actors.OfType<Crawler>()) {
                        actorLookup[actor.Name] = actor;
                    }
                }
            }
        }

        // Second pass: Restore Relations for all crawlers
        for (int y = 0; y < map.Height; y++) {
            for (int x = 0; x < map.Width; x++) {
                var sector = map.GetSector(x, y);
                foreach (var location in sector.Locations.Where(l => l.HasEncounter)) {
                    var encounter = location.GetEncounter();
                    foreach (var crawler in encounter.Actors.OfType<Crawler>()) {
                        var savedCrawler = savedMap.Sectors
                            .SelectMany(s => s.Locations)
                            .SelectMany(l => l.Encounter?.Actors ?? [])
                            .FirstOrDefault(sc => sc.Name == crawler.Name);

                        if (savedCrawler != null) {
                            savedCrawler.RestoreRelationsTo(crawler, actorLookup);
                        }
                    }
                }
            }
        }

        return map;
    }

    public static Capital ToGameCapital(this SavedCapital savedCapital, Map map) {
        var settlement = savedCapital.Settlement.ToGameCrawler(map);
        return new Capital(savedCapital.Name, settlement, savedCapital.Influence);
    }

    public static Location ToGameLocation(this SavedLocation savedLocation, Sector sector) {
        var location = new Location(savedLocation.Seed,
            sector, savedLocation.Position, savedLocation.Type, savedLocation.Wealth, loc => savedLocation.Encounter?.ToGameEncounter(loc) ?? new Encounter(savedLocation.Seed, loc).Create(100_000_000_000));
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
        for (int y = 0; y < map.Height; y++) {
            for (int x = 0; x < map.Width; x++) {
                var sector = map.GetSector(x, y);
                foreach (var location in sector.Locations) {
                    if (Vector2.Distance(location.Position, position) < 0.001f) {
                        return location;
                    }
                }
            }
        }
        throw new InvalidOperationException($"Location not found at position {position}");
    }

    public static Crawler ToGameCrawler(this SavedCrawler savedCrawler, Map map) {
        var location = map.FindLocationByPosition(savedCrawler.LocationPos);
        var supplies = savedCrawler.Supplies.ToGameInventory();
        var cargo = savedCrawler.Cargo.ToGameInventory();

        // Create Init structure
        var init = new Crawler.Init {
            Seed = savedCrawler.RngState, // Use saved RNG state as seed
            Name = savedCrawler.Name,
            Brief = "",
            Faction = savedCrawler.Faction,
            Location = location,
            Supplies = supplies,
            Cargo = cargo,
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

    public static Inventory ToGameInventory(this SavedInventory savedInv) {
        var inventory = new Inventory();
        // Essential/Currency
        inventory[Commodity.Scrap] = savedInv.Scrap;
        inventory[Commodity.Fuel] = savedInv.Fuel;
        inventory[Commodity.Crew] = savedInv.Crew;
        inventory[Commodity.Morale] = savedInv.Morale;
        inventory[Commodity.Isotopes] = savedInv.Isotopes;
        inventory[Commodity.Nanomaterials] = savedInv.Nanomaterials;

        // Life support
        inventory[Commodity.Air] = savedInv.Air;
        inventory[Commodity.Water] = savedInv.Water;
        inventory[Commodity.Rations] = savedInv.Rations;

        // Raw materials
        inventory[Commodity.Biomass] = savedInv.Biomass;
        inventory[Commodity.Ore] = savedInv.Ore;
        inventory[Commodity.Silicates] = savedInv.Silicates;

        // Refined materials
        inventory[Commodity.Metal] = savedInv.Metal;
        inventory[Commodity.Chemicals] = savedInv.Chemicals;
        inventory[Commodity.Glass] = savedInv.Glass;

        // Parts
        inventory[Commodity.Ceramics] = savedInv.Ceramics;
        inventory[Commodity.Polymers] = savedInv.Polymers;
        inventory[Commodity.Alloys] = savedInv.Alloys;
        inventory[Commodity.Electronics] = savedInv.Electronics;
        inventory[Commodity.Explosives] = savedInv.Explosives;

        // Consumer Goods
        inventory[Commodity.Medicines] = savedInv.Medicines;
        inventory[Commodity.Textiles] = savedInv.Textiles;
        inventory[Commodity.Gems] = savedInv.Gems;
        inventory[Commodity.Toys] = savedInv.Toys;
        inventory[Commodity.Machines] = savedInv.Machines;
        inventory[Commodity.AiCores] = savedInv.AiCores;
        inventory[Commodity.Media] = savedInv.Media;

        // Vice & Contraband
        inventory[Commodity.Liquor] = savedInv.Liquor;
        inventory[Commodity.Stims] = savedInv.Stims;
        inventory[Commodity.Downers] = savedInv.Downers;
        inventory[Commodity.Trips] = savedInv.Trips;
        inventory[Commodity.SmallArms] = savedInv.SmallArms;

        // Religious items
        inventory[Commodity.Idols] = savedInv.Idols;
        inventory[Commodity.Texts] = savedInv.Texts;
        inventory[Commodity.Relics] = savedInv.Relics;

        // Segments
        foreach (var savedSegment in savedInv.Segments) {
            var segment = savedSegment.ToGameSegment();
            inventory.Add(segment);
        }

        return inventory;
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
