using System.Drawing;
using System.Numerics;

namespace Crawler;

public enum TerrainType {
    Flat,
    Rough,
    Broken,
    Shattered,
    Ruined,
}

public record Location(
    Sector Sector, Vector2 Position,
    EncounterType Type, float wealth,
    Func<Location, Encounter> NewEncounter) {
    public TerrainType Terrain => Sector.Terrain;
    public override string ToString() => $"{Type} @{PosString} Pop:{Population:F1}";
    public Map Map => Sector.Map;
    Encounter? _encounter = null;
    public bool HasEncounter => _encounter != null;
    public Encounter Encounter => _encounter ??= NewEncounter(this);
    public string PosString => $"{(int)(Position.X * 100) % 100:D2},{(int)(Position.Y * 100) % 100:D2}";
    public string EncounterName(IActor visitor) => visitor.To(this).Visited switch {
        true => Encounter.Name,
        false => Type.ToString(),
    };
    float zipf = Random.Shared.NextSingle();
    public float Population => Math.Clamp((12 * (float)Math.Pow(0.025f, zipf)), 0, 10);
    public float Wealth => wealth * Population;

    public Faction ChooseRandomFaction() {
        // Get base weights for this terrain type
        var baseWeights = Tuning.Encounter.crawlerSpawnWeight[Terrain];

        // Adjust bandit weight by dividing by population (more pop = fewer bandits)
        var adjustedWeights = new EArray<Faction, float>();
        for (int i = 0; i < baseWeights.Length; i++) {
            var faction = (Faction)i;
            if (faction == Faction.Bandit) {
                // Divide bandit weight by population (with minimum of 0.1 to avoid divide by zero)
                adjustedWeights[faction] = baseWeights[faction] / Math.Max(Population, 0.1f);
            } else {
                adjustedWeights[faction] = baseWeights[faction];
            }
        }

        return adjustedWeights.Pairs().ChooseWeightedRandom();
    }

    public float Distance(Location other) {
        const float kilometersPerSector = 1000;
        var d = Offset(other) * kilometersPerSector;
        return d.Length();
    }
    public Vector2 Offset(Location other) {
        float dx = Position.X - other.Position.X;
        float dy = Position.Y - other.Position.Y;
        float width = Sector.Map.Width;
        if (dx < -width / 2) {
            dx += width;
        } else if (dx > width / 2) {
            dx -= width;
        }
        return new(dx, dy);
    }
}

public class ActorLocation {
    public bool Visited = false;
    public long ForgetTime = 0;

}

public struct Sector(Map map, string name, int x, int y) {
    public Map Map => map;
    public string Name => name;
    public int X => x;
    public int Y => y;
    public TerrainType Terrain { get; set; }
    public List<Sector> Neighbors { get; } = new();
    public List<Location> Locations { get; } = new();
    public List<IActor> Actors { get; set; } = new();
    public override string ToString() => $"{Name} ({Terrain})";
    public string Look() => $"Sector {Name} ({Terrain})";
    public Point Offset(Sector other) {
        int dx = x - other.X;
        int dy = y - other.Y;
        int width = Map.Width;
        if (dx < -width / 2) {
            dx += width;
        } else if (dx > width / 2) {
            dx -= width;
        }
        return new(dx, dy);
    }

    public EArray<Commodity, float> LocalMarkup = InitLocalOfferRates();
    static EArray<Commodity, float> InitLocalOfferRates() {
        EArray<Commodity, float> result = new();
        result.Initialize(() => CrawlerEx.NextGaussian(Tuning.Trade.rate, Tuning.Trade.sd));
        return result;
    }
    public EArray<SegmentKind, float> LocalSegmentRates = InitLocalSegmentRates();
    static EArray<SegmentKind, float> InitLocalSegmentRates() {
        EArray<SegmentKind, float> result = new();
        result.Initialize(() => CrawlerEx.NextGaussian(Tuning.Trade.rate, Tuning.Trade.sd));
        return result;
    }
}

public class Map {
    public Map(int Height, int Width) {
        Sectors = new Sector[Height, Width];
        float expectation = 2.5f * Height * Width;
        foreach (var (X, Y) in Sectors.Index()) {
            var sectorName = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Y] + $"{X}";
            var sector = new Sector(this, sectorName, X, Y);
            sector.Terrain = ChooseTerrainType(new Vector3(X, Y, 0));

            Sectors[Y, X] = sector;
        }
        int k = CrawlerEx.SamplePoisson(expectation);
        List<Vector3> locations = new();
        uint seed = 123234234;

        seed &= 0x7FFFFF;
        for (int i = 0; i < k; ++i) {
            float tx = ( float ) CrawlerEx.HaltonSequence(2, seed + ( uint ) i);
            float ty = ( float ) CrawlerEx.HaltonSequence(5, seed + ( uint ) i + 34234234);
            float x = tx * Width;
            float y = ty * Height;

            float xFrac = CrawlerEx.Frac(x);
            float yFrac = CrawlerEx.Frac(y);
            float xFloor = (float)Math.Floor(x);
            float yFloor = (float)Math.Floor(y);
            var sector = Sectors[(int)yFloor, (int)xFloor];

            locations.Add(new(x, y, seed + i));
        }
        foreach (var loc in locations) {
            var sector = Sectors[(int)loc.Y, (int)loc.X];
            Vector2 frac = new(CrawlerEx.Frac(loc.X), CrawlerEx.Frac(loc.Y));

            EncounterType encounterType = ChooseEncounterTypes(sector.Terrain, loc);
            float tLat = loc.Y / ( float ) Height;
            tLat += CrawlerEx.NextGaussian() * 0.05f;
            tLat = Math.Clamp(tLat, 0.0f, 1.0f);
            float wealth = 300 / (tLat + 0.15f); // ( 1000 / ( tlat + 0.25f ))

            var encounterLocation = new Location(
                sector,
                new (loc.X, loc.Y),
                encounterType,
                wealth,
                loc => new Encounter(loc)
            );
            sector.Locations.Add(encounterLocation);
        }
        foreach (var (X, Y) in Sectors.Index()) {
            var sector = Sectors[Y, X];
            var neighbors = new List<Sector>();
            neighbors.Add(Sectors[Y, (X + Width - 1) % Width]);
            neighbors.Add(Sectors[Y, (X + 1) % Width]);
            if (Y > 0) {
                neighbors.Add(Sectors[Y - 1, X]);
            }
            if (Y < Height - 1) {
                neighbors.Add(Sectors[Y + 1, X]);
            }
            sector.Neighbors.AddRange(neighbors.Where(s => s.Locations.Count > 0));
        }
    }
    public Location GetStartingLocation() {
        int tries = Width;
        int X = Random.Shared.Next(Width);
        int Y = Height * 3 / 4;
        while (Y >= 0) {
            while (tries-- > 0) {
                var X2 = (X + Width) % Width;
                var sector = Sectors[Y, X2];
                if (sector.Locations.Count > 0) {
                    return sector.Locations[0];
                }
            }
        }
        throw new Exception("No starting location found");
    }
    public void AddActor(IActor actor) {
        var loc = actor.Location;
        var sector = Sectors[(int)loc.Position.Y, (int)loc.Position.X];
        sector.Actors.Add(actor);
        loc.Encounter.AddActor(actor);
    }
    public void RemoveActor(IActor actor) {
        var loc = actor.Location;
        var sector = Sectors[(int)loc.Position.Y, (int)loc.Position.X];
        sector.Actors.Remove(actor);
        loc.Encounter.RemoveActor(actor);
    }
    public void MoveActor(IActor actor, Location newPos) {
        if (actor.Location == newPos) {
            return;
        }
        RemoveActor(actor);
        actor.Location = newPos;
        AddActor(actor);
    }
    TerrainType ChooseTerrainType(Vector3 position) {
        float tTerrain = Random.Shared.NextSingle();
        float tLat = position.Y / ( float ) Height;
        EArray<TerrainType, float> terrainWeights = [];
        switch (( int ) (tLat * 5)) {
        case 0: // Substellar
            terrainWeights = [1, 3, 6, 12, 0];
            break;
        case 1:
            terrainWeights = [3, 6, 6, 3, 0];
            break;
        case 2: // Mid-latitudes
            terrainWeights = [6, 6, 3, 1, 0];
            break;
        case 3:
            terrainWeights = [12, 6, 3, 1, 0];
            break;
        case 4: // Rim
            terrainWeights = [6, 3, 1, 0, 0];
            break;
        }
        return terrainWeights.Pairs().ChooseWeightedAt(tTerrain);
    }
    EncounterType ChooseEncounterTypes(TerrainType terrain, Vector3 position) {
        float tEncounter = ( float ) CrawlerEx.HaltonSequence(11, (uint)position.Z + 8920348);
        EArray<EncounterType, float> encounterWeights = [];
        switch (terrain) {
            // None, Crawler, Settlement, Resource, Hazard,
        case TerrainType.Flat:
            encounterWeights = [0, 5, 5, 3, 3];
            break;
        case TerrainType.Rough:
            encounterWeights = [0, 5, 3, 3, 3];
            break;
        case TerrainType.Broken:
            encounterWeights = [0, 4, 1.5f, 4, 4];
            break;
        case TerrainType.Shattered:
            encounterWeights = [0, 3, 0.5f, 5, 5];
            break;
        case TerrainType.Ruined:
            encounterWeights = [0, 0, 0, 0, 0];
            break;
        }
        return encounterWeights.Pairs().ChooseWeightedAt(tEncounter);
    }
    public int Height => Sectors.GetLength(0);
    public int Width => Sectors.GetLength(1);
    Sector[,] Sectors { get; }

    public Sector GetSector(int x, int y) => Sectors[y, x];
    public IEnumerable<Location> FindLocationsInRadius(Vector2 center, float radius) {
        int L = ( int ) Math.Floor(center.X - radius);
        int R = ( int ) Math.Ceiling(center.X + radius);
        int T = ( int ) Math.Floor(center.Y - radius);
        T = Math.Max(T, 0);
        int B = ( int ) Math.Ceiling(center.Y + radius);
        B = Math.Min(B, Height);
        for (int y = T; y < B; ++y) {
            for (int x = L; x < R; ++x) {
                int x2 = (x + Width) % Width;
                var sector = Sectors[y, x2];
                foreach (var location in sector.Locations) {
                    var locPos = location.Position;
                    locPos.X -= (x2 - x);
                    if (Vector2.Distance(location.Position, center) <= radius) {
                        yield return location;
                    }
                }
            }
        }
    }
    public char TerrainCode(TerrainType t) => t switch {
        TerrainType.Flat => '`',
        TerrainType.Rough => '-',
        TerrainType.Broken => '=',
        TerrainType.Shattered => '≡',
        TerrainType.Ruined => 'X',
        _ => '_',
    };
    public string DumpSector(int X, int Y, int DrawWidth = 16, int DrawHeight = 9, params Crawler[] players) {
        var sector = Sectors[Y, X];
        var sectorMap = new char[DrawHeight, DrawWidth];
        var sectorBg = TerrainCode(sector.Terrain);
        sectorMap.Fill(sectorBg);
        foreach (var (i, location) in sector.Locations.Index()) {
            int cx = (int)(CrawlerEx.Frac(location.Position.X) * (DrawWidth - 2));
            int cy = (int)(CrawlerEx.Frac(location.Position.Y) * (DrawHeight - 1));
            sectorMap[cy, cx] = location.Type switch {
                EncounterType.None => '.',
                EncounterType.Crossroads => players.Any(c => c.Location == location) ? '@' : 'c',
                EncounterType.Settlement => 'S',
                EncounterType.Resource => '?',
                EncounterType.Hazard => '!',
                _ => '_',
            };
            var idx = (i + 1).ToString();
            sectorMap[cy, cx + 1] = idx[0];
            if (idx.Length > 1) {
                sectorMap[cy, cx + 2] = idx[1];
            }
        }
        string result = "";
        for (int i = 0; i < DrawHeight; ++i) {
            for (int j = 0; j < DrawWidth; ++j) {
                result += sectorMap[i, j];
            }
            result += '\n';
        }
        return result;
    }

    public string DumpMap(params IActor[] players) {
        string result = "";
        for (int y = 0; y < Height; y++) {
            string map = string.Empty;
            string map2 = string.Empty;
            for (int x = 0; x < Width; x++) {
                var sector = GetSector(x, y);
                char terrainChar = TerrainCode(sector.Terrain);
                char indicator = terrainChar;
                char indicator2 = terrainChar;

                // Mark settlements
                bool hasSettlement = sector.Locations.Any(loc => loc.Type == EncounterType.Settlement);
                if (hasSettlement) {
                    indicator = 'S';
                }

                // Mark player position
                foreach (var (i, player) in players.Index()) {
                    if (player.Location.Sector.Name == sector.Name) {
                        indicator = (char)('1' + i);
                        break;
                    }
                }

                map += $"{terrainChar}{indicator2}{terrainChar}";
                map2 += $"{terrainChar}{indicator}{terrainChar}";
            }
            result += $"{map}\n{map2}\n";
        }
        return result.Trim();
    }
}
