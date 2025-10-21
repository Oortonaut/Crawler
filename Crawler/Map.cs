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
    public Encounter GetEncounter() => _encounter ??= NewEncounter(this);
    public string PosString => $"{(int)(Position.X * 100) % 100:D2},{(int)(Position.Y * 100) % 100:D2}";
    public string Name => HasEncounter ? _encounter!.Name : Type.ToString();
    public string EncounterName(IActor visitor) => (visitor.To(this).Visited && HasEncounter) switch {
        true => _encounter!.Name,
        false => Type.ToString(),
    };
    float zipf = Random.Shared.NextSingle();
    // Pareto distribution Type I
    public const float MaxPopulation = 500;
    public float Population => Math.Clamp((MaxPopulation * (float)Math.Pow(0.005f, zipf)), 0, MaxPopulation);
    public float Wealth => wealth;
    public float TechLatitude => 2 * (1 - ((Position.Y + 0.5f) / Map.Height));
    public string Code => Type switch {
        EncounterType.None => ".",
        EncounterType.Crossroads => "x",
        EncounterType.Settlement => "o123456789ABCDEFGHIJK"[( int ) Math.Log2(Population)].ToString(),
        EncounterType.Resource => "?",
        EncounterType.Hazard => "!",
        _ => "_",
    };

    public Faction ChooseRandomFaction() {
        // Get base weights for this terrain type
        var baseWeights = Tuning.Encounter.crawlerSpawnWeight[Terrain];

        // Adjust bandit weight by dividing by population (more pop = fewer bandits)
        var adjustedWeights = new EArray<Faction, float>();
        foreach (var (faction, weight) in baseWeights.Pairs()) {
            if (faction == Faction.Bandit) {
                adjustedWeights[faction] = baseWeights[faction] / (Population / 100);
            } else if (baseWeights[faction] > 0) {
                adjustedWeights[faction] = baseWeights[faction];
            } else if (faction == Sector.ControllingFaction) {
                adjustedWeights[faction] = 2;
            } else if (faction < Map.FactionEnd) {
                adjustedWeights[faction] = 0.5f;
            } else {
                adjustedWeights[faction] = 0;
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

public class LocationActor {
    public bool Visited = false;
    public long ForgetTime = 0;
}

public record FactionData(string Name, Color Color, Capital? Capital);

public record Capital(Crawler Settlement, float Influence) {
    public string Name => Settlement.Name;
    public Location Location => Settlement.Location;
}

public class Sector(Map map, string name, int x, int y) {
    public Map Map => map;
    public string Name { get; set; } = name;
    public int X => x;
    public int Y => y;
    public Point Position => new(x, y);
    public TerrainType Terrain { get; set; }
    public Faction ControllingFaction { get; set; } = Faction.Independent; // Default to Trade for unassigned
    public List<Sector> Neighbors { get; } = new();
    public List<Location> Locations { get; } = new();
    public IEnumerable<Location> Settlements => Locations.Where(loc => loc.Type == EncounterType.Settlement);
    public List<IActor> Actors { get; set; } = new();
    public override string ToString() => $"{Name} ({Terrain})";
    public string Look() {
        var result = $"{ControllingFaction.Name()} - {Name} ({Terrain} Terrain) ";
        var locationSummary = Locations.GroupBy(loc => loc.Type).Select(g => $"{g.Key}" + (g.Count() > 1 ? $" ({g.Count()})" : ""));
        result += string.Join(", ", locationSummary);
        return result;
    }
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
        result.Initialize(() => Tuning.Trade.LocalMarkup());
        return result;
    }
    public EArray<SegmentKind, float> LocalSegmentRates = InitLocalSegmentRates();
    static EArray<SegmentKind, float> InitLocalSegmentRates() {
        EArray<SegmentKind, float> result = new();
        result.Initialize(() => Tuning.Trade.LocalMarkup());
        return result;
    }
}

public class Map {
    // Private constructor for deserialization - doesn't generate locations
    private Map(int Height, int Width, bool skipGeneration) {
        Sectors = new Sector[Height, Width];
        foreach (var (X, Y) in Sectors.Index()) {
            var sectorName = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Y] + $"{X}";
            var sector = new Sector(this, sectorName, X, Y);
            Sectors[Y, X] = sector;
        }
    }

    // Factory method for creating map from save data
    internal static Map FromSaveData(int Height, int Width) => new Map(Height, Width, skipGeneration: true);

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
            float wealth = 1000 / (tLat + 0.15f); // ( 1000 / ( tlat + 0.25f ))

            var encounterLocation = new Location(
                sector,
                new (loc.X, loc.Y),
                encounterType,
                wealth,
                loc => new Encounter(loc).Generate()
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

        // Identify faction capitals and assign sectors via weighted Voronoi
        IdentifyFactionCapitals();
        AssignSectorFactions();
    }

    public int NumFactions { get; protected set; }
    public Faction FactionEnd { get; protected set; }
    void IdentifyFactionCapitals() {
        // Collect all settlement locations
        var settlementLocations = new List<Location>();
        foreach (var (X, Y) in Sectors.Index()) {
            var sector = Sectors[Y, X];
            settlementLocations.AddRange(sector.Locations.Where(loc => loc.Type == EncounterType.Settlement));
        }

        // Sort by population descending
        settlementLocations = settlementLocations.OrderByDescending(loc => loc.Population).DistinctBy(loc => loc.Sector.Name).ToList();

        NumFactions = Math.Min(Height * 3 / 4, 20);
        FactionEnd = 1 + (Faction)Math.Min((int)Faction.Civilian19, (int)Faction.Civilian1 + NumFactions - 1);

        FactionData[Faction.Player] = new ("Player", Color.White, null);
        FactionData[Faction.Independent] = new ("Independent", Color.Blue, null);
        FactionData[Faction.Bandit] = new ("Bandit", Color.Red, null);

        Faction currFaction = Faction.Civilian0;
        for (int i = 0; i < settlementLocations.Count && currFaction < FactionEnd; i++) {
            var setLocation = settlementLocations[i];
            var sector = setLocation.Sector;
            var faction = currFaction;
            sector.ControllingFaction = faction;
            var encounter = setLocation.GetEncounter();
            var name = setLocation.Name;
            var sectorPopulation = sector.Locations.Sum(loc => loc.Population);
            foreach (var crawler in encounter.Settlements.OfType<Crawler>().Take(1)) {
                name = crawler.Name;
                float influence = 8 + ( float ) Math.Log2(sectorPopulation);
                var capital = new Capital(crawler, influence);
                FactionData[faction] = new (name, FactionEx._factionColors[faction], capital);
                currFaction++;
            }
            sector.Name = $"{name} Capital";
        }

        // Generate policies for each civilian faction
        foreach (var (faction, data) in FactionData.Pairs()) {
            if (data?.Capital is {} capital) {
                Tuning.FactionPolicies.Policies[faction] = GenerateFactionPolicy(capital);
            }
        }
    }

    void AssignSectorFactions() {
        // Weighted Voronoi: assign each sector to nearest faction capital weighted by population
        foreach (var (X, Y) in Sectors.Index()) {
            var sector = Sectors[Y, X];

            // Calculate weighted distance to each capital
            float minWeightedDistance = float.MaxValue;
            Faction closestFaction = Faction.Independent;

            foreach (var (faction, data) in FactionData.Pairs()) {
                if (data?.Capital is { } capital) {
                    float distance = sector.Offset(capital.Location.Sector).Length();
                    float weightedDistance = distance / capital.Influence;

                    if (weightedDistance < minWeightedDistance) {
                        minWeightedDistance = weightedDistance;
                        closestFaction = faction;
                    }
                }
            }

            sector.ControllingFaction = closestFaction;
        }
    }

    EArray<Commodity, TradePolicy> GenerateFactionPolicy(Capital capital) {
        // Generate procedural policies based on capital characteristics
        var policy = Tuning.FactionPolicies.CreateDefaultPolicy(TradePolicy.Legal);

        // Use terrain and tech to influence policies
        //var terrain = capital.Location.Terrain;
        //var tech = capital.Location.TechLatitude;
        var seed = capital.Name.GetHashCode();
        var random = new Random((int)seed);

        // Determine faction archetype
        int archetypeRoll = random.Next(100);

        if (archetypeRoll < 30) {
            // Religious faction - subsidize religious items, prohibit drugs
            policy[Commodity.Idols] = TradePolicy.Subsidized;
            policy[Commodity.Texts] = TradePolicy.Subsidized;
            policy[Commodity.Relics] = TradePolicy.Subsidized;
            policy[Commodity.Liquor] = TradePolicy.Prohibited;
            policy[Commodity.Stims] = TradePolicy.Prohibited;
            policy[Commodity.Downers] = TradePolicy.Prohibited;
            policy[Commodity.Trips] = TradePolicy.Prohibited;
            policy[Commodity.SmallArms] = TradePolicy.Controlled;
            policy[Commodity.Explosives] = TradePolicy.Controlled;
        } else if (archetypeRoll < 60) {
            // Industrial/Mining faction - subsidize raw materials, tax weapons
            policy[Commodity.Ore] = TradePolicy.Subsidized;
            policy[Commodity.Silicates] = TradePolicy.Subsidized;
            policy[Commodity.Metal] = TradePolicy.Subsidized;
            policy[Commodity.Explosives] = TradePolicy.Taxed;
            policy[Commodity.SmallArms] = TradePolicy.Taxed;
            policy[Commodity.Liquor] = TradePolicy.Taxed;
            policy[Commodity.Stims] = TradePolicy.Taxed;
        } else {
            // Authoritarian/Restrictive faction - prohibit many things
            policy[Commodity.SmallArms] = TradePolicy.Prohibited;
            policy[Commodity.Explosives] = TradePolicy.Prohibited;
            policy[Commodity.Liquor] = TradePolicy.Controlled;
            policy[Commodity.Stims] = TradePolicy.Prohibited;
            policy[Commodity.Downers] = TradePolicy.Prohibited;
            policy[Commodity.Trips] = TradePolicy.Prohibited;
            policy[Commodity.Idols] = TradePolicy.Controlled;
            policy[Commodity.Texts] = TradePolicy.Controlled;
            policy[Commodity.Relics] = TradePolicy.Controlled;
            policy[Commodity.AiCores] = TradePolicy.Controlled;
        }

        return policy;
    }

    public Location GetStartingLocation() {
        for (int Y = Height - 1; Y >= 0; --Y) {
            int DX = Random.Shared.Next(Width);
            for (int X = 0; X < Width; ++X) {
                var X2 = (X + DX) % Width;
                var sector = Sectors[Y, X2];
                var locs = sector.Locations
                    .Where(loc => loc.Type == EncounterType.Settlement)
                    .Where(loc => loc.Sector.ControllingFaction != Faction.Bandit)
                    .Where(loc => loc.Terrain <= TerrainType.Rough)
                    .ToList();
                if (locs.Count > 0) {
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
        loc.GetEncounter().AddActor(actor);
    }
    public void RemoveActor(IActor actor) {
        var loc = actor.Location;
        var sector = Sectors[(int)loc.Position.Y, (int)loc.Position.X];
        sector.Actors.Remove(actor);
        loc.GetEncounter().RemoveActor(actor);
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
    public EArray<Faction, FactionData?> FactionData { get; } = new();

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
            sectorMap[cy + 1, cx] = location.Code[0];
            if (players.Any(c => c.Location == location)) {
                sectorMap[cy + 1, cx + 1] = '@';
            }
            var idx = "M" + (i + 1).ToString();
            int col = cx;
            foreach (var c in idx) {
                sectorMap[cy, col] = c;
                if (++col == DrawWidth) {
                    break;
                }
            }
        }
        string result = "";
        for (int i = 0; i < DrawHeight; ++i) {
            for (int j = 0; j < DrawWidth; ++j) {
                result += sectorMap[i, j];
            }
            if (i < DrawHeight - 1) {
                result += '\n';
            }
        }
        return result;
    }

    public string DumpMap(params List<IActor> players) {
        string defaultStyle = Style.MenuNormal.StyleString();
        string titleStyle = Style.MenuUnvisited.StyleString();


        var worldMapWidth = Width * 3 + 2;
        var header = defaultStyle + "┌[" + titleStyle + "Global Map" + defaultStyle + "]";
        header += new string('─', Math.Max(0, worldMapWidth - 12)) + "╖\n";
        var footer = defaultStyle + $"╘{new string('═', worldMapWidth)}╝\n";

        string result = header;

        result += "│ :" + titleStyle;
        for (int x = 0; x < Width; x++) {
            result += $"{x,3}";
        }
        result += defaultStyle + "║\n";
        for (int y = 0; y < Height; y++) {
            string map = defaultStyle + "│" + titleStyle + (char)('A' + y) + defaultStyle + ":";
            string map2 = defaultStyle + "│ :";
            for (int x = 0; x < Width; x++) {
                var sector = GetSector(x, y);
                var sectorColor = sector.ControllingFaction.GetColor();
                var sectorStyle = sectorColor.On(Color.Black);
                int playerIndex = players.FindIndex(p => p.Location.Sector.Position == sector.Position);
                if (playerIndex >= 0) {
                    sectorStyle = sectorColor.On(Color.DarkBlue);
                }
                char terrainChar = TerrainCode(sector.Terrain);
                string top = sectorStyle;
                string bottom = sectorStyle;
                int topWidth = 0;
                int bottomWidth = 0;

                // Mark settlements
                foreach (var settlement in sector.Locations.Where(loc => loc.Type == EncounterType.Settlement)) {
                    bottom += settlement.Code;
                    ++bottomWidth;
                }

                top += new string(terrainChar, 3 - topWidth);
                bottom += new string(terrainChar, 3 - bottomWidth);

                map += top;
                map2 += bottom;
            }
            map += defaultStyle + "║";
            map2 += defaultStyle + "║";

            // Add civilian faction info for this row
            // if (y < FactionCapitals.Count) {
            //     var capital = FactionCapitals[y];
            //     var option = Style.MenuOption.Format($"{y + 1}");
            //     var capitalString = capital.Faction.GetColor().On(Color.Black) + capital.Name + defaultStyle;
            //     map += $" [{option}] {capitalString} ({capital.Faction})";
            //     /* TODO: Move this policy text into a helper function
            //     var policy = Tuning.FactionPolicies.Policies[capital.Faction];
            //
            //     // Get restricted/prohibited commodities
            //     var restrictedList = policy.Pairs()
            //         .Where(p => p.Item2 == TradePolicy.Controlled || p.Item2 == TradePolicy.Prohibited)
            //         .Select(p => $"{p.Item1}:{(p.Item2 == TradePolicy.Controlled ? '-' : 'X')}")
            //         .ToList();
            //
            //     // Get subsidized commodities
            //     var subsidizedList = policy.Pairs()
            //         .Where(p => p.Item2 == TradePolicy.Subsidized)
            //         .Select(p => $"{p.Item1}:+")
            //         .ToList();
            //
            //     int factionIndex = capital.Faction.CivilianIndex();
            //     string factionName = factionIndex < 10 ? factionIndex.ToString() : ((char)('A' + (factionIndex - 10))).ToString();
            //
            //     var restrictions = restrictedList.Concat(subsidizedList).ToList();
            //     factionInfo = restrictions.Any()
            //         ? $" [{factionName}] {string.Join(", ", restrictions)}"
            //         : $" [{factionName}] Open Trade";
            //         */
            // }

            result += $"{map}\n{map2}\n";
        }
        result += footer;
        return result;
    }
}
