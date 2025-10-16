using YamlDotNet.Serialization;
using System.Numerics;

namespace Crawler;

[YamlSerializable]
public class SaveGameData {
    public string Version { get; set; } = "1.0";
    public long Hour { get; set; }
    public int AP { get; set; }
    public int TurnAP { get; set; }
    public Vector2 CurrentLocationPos { get; set; }
    public SavedCrawler Player { get; set; } = new();
    public SavedMap Map { get; set; } = new();
    public bool Quit { get; set; }
}

[YamlSerializable]
public class SavedCrawler {
    public string Name { get; set; } = "";
    public Faction Faction { get; set; }
    public Vector2 LocationPos { get; set; }
    public SavedInventory Inventory { get; set; } = new();
    public List<SavedSegment> Segments { get; set; } = new();
    public Dictionary<string, SavedActorRelation> Relations { get; set; } = new();
    public int EvilPoints { get; set; } = 0;
}

[YamlSerializable]
public class SavedInventory {
    public float Scrap { get; set; }
    public float Fuel { get; set; }
    public float Rations { get; set; }
    public float Crew { get; set; }
    public float Morale { get; set; }
}

[YamlSerializable]
public class SavedSegment {
    public string DefName { get; set; } = "";
    public int Hits { get; set; }
    public float Charge { get; set; }
    public int ShieldLeft { get; set; }
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
public class SavedMap {
    public int Width { get; set; }
    public int Height { get; set; }
    public List<SavedSector> Sectors { get; set; } = new();
}

[YamlSerializable]
public class SavedSector {
    public int X { get; set; }
    public int Y { get; set; }
    public TerrainType Terrain { get; set; }
    public float Wealth { get; set; }
    public List<SavedLocation> Locations { get; set; } = new();
}

[YamlSerializable]
public class SavedLocation {
    public Vector2 Position { get; set; }
    public TerrainType Terrain { get; set; }
    public EncounterType Type { get; set; }
    public float Wealth { get; set; }
    public SavedEncounter? Encounter { get; set; }
}

[YamlSerializable]
public class SavedEncounter {
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Faction Faction { get; set; }
    public List<SavedCrawler> Actors { get; set; } = new();
}

public static class SaveLoadExtensions {
    public static SaveGameData ToSaveData(this Game game) {
        return new SaveGameData {
            Version = "1.0",
            Hour = game.GetTime(),
            AP = game.GetAP(),
            TurnAP = game.GetTurnAP(),
            CurrentLocationPos = game.CurrentLocation.Position,
            Player = game.GetPlayer().ToSaveData(),
            Map = game.GetMap().ToSaveData(),
            Quit = game.GetQuit()
        };
    }

    public static SavedCrawler ToSaveData(this Crawler crawler) {
        return new SavedCrawler {
            Name = crawler.Name,
            Faction = crawler.Faction,
            LocationPos = crawler.Location.Position,
            Inventory = crawler.Inv.ToSaveData(),
            Segments = crawler.Segments.Select(s => s.ToSaveData()).ToList(),
            Relations = crawler.GetRelations().ToDictionary(
                kvp => kvp.Key.Name,
                kvp => kvp.Value.ToSaveData()
            ),
            EvilPoints = crawler.EvilPoints
        };
    }

    public static SavedInventory ToSaveData(this Inventory inventory) {
        return new SavedInventory {
            Scrap = inventory[Commodity.Scrap],
            Fuel = inventory[Commodity.Fuel],
            Rations = inventory[Commodity.Rations],
            Crew = inventory[Commodity.Crew],
            Morale = inventory[Commodity.Morale]
        };
    }

    public static SavedSegment ToSaveData(this Segment segment) {
        var saved = new SavedSegment {
            DefName = segment.SegmentDef.Name,
            Hits = segment.Hits
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
            Sectors = savedSectors
        };
    }

    public static SavedSector ToSaveData(this Sector sector) {
        return new SavedSector {
            X = sector.X,
            Y = sector.Y,
            Terrain = sector.Terrain,
            Wealth = 0,
            Locations = sector.Locations.Select(l => l.ToSaveData()).ToList()
        };
    }

    public static SavedLocation ToSaveData(this Location location) {
        return new SavedLocation {
            Position = location.Position,
            Terrain = location.Terrain,
            Type = location.Type,
            Wealth = location.Wealth,
            Encounter = location.HasEncounter ? location.Encounter.ToSaveData() : null
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
        var map = new Map(savedMap.Height, savedMap.Width);
        return map;
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
        var inventory = savedCrawler.Inventory.ToGameInventory();

        foreach (var savedSegment in savedCrawler.Segments) {
            var segment = savedSegment.ToGameSegment();
            inventory.Add(segment);
        }

        var crawler = new Crawler(location, inventory) {
            Name = savedCrawler.Name,
            Faction = savedCrawler.Faction,
            EvilPoints = savedCrawler.EvilPoints
        };

        return crawler;
    }

    public static Inventory ToGameInventory(this SavedInventory savedInv) {
        var inventory = new Inventory();
        inventory[Commodity.Scrap] = savedInv.Scrap;
        inventory[Commodity.Fuel] = savedInv.Fuel;
        inventory[Commodity.Rations] = savedInv.Rations;
        inventory[Commodity.Crew] = savedInv.Crew;
        inventory[Commodity.Morale] = savedInv.Morale;
        return inventory;
    }

    public static Segment ToGameSegment(this SavedSegment savedSegment) {
        var segmentDef = SegmentEx.NameLookup[savedSegment.DefName];
        var segment = segmentDef.NewSegment();

        segment.Hits = savedSegment.Hits;

        if (segment is ReactorSegment reactor) {
            reactor.Charge = savedSegment.Charge;
        }
        if (segment is ShieldSegment shield) {
            shield.ShieldLeft = savedSegment.ShieldLeft;
        }

        return segment;
    }

}
