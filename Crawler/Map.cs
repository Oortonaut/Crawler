using System.Drawing;
using System.Numerics;
using Crawler.Logging;
using Crawler.Network;

namespace Crawler;

public enum TerrainType {
    Flat,
    Rough,
    Broken,
    Shattered,
    Ruined,
}

public record Location {
    public Location(ulong Seed,
        Sector Sector, Vector2 Position,
        EncounterType Type, float Wealth,
        Func<Location, Encounter> NewEncounter) {
        if (Wealth <= 0) {
            throw new ArgumentException("Wealth must be positive");
        }
        Rng = new XorShift(Seed);
        this.Seed = Seed;
        this.Sector = Sector;
        this.Position = Position;
        this.Type = Type;
        this.Wealth = Wealth;
        this.NewEncounter = NewEncounter;
        float Zipf = Rng.NextSingle();
        Population = ( int ) (MaxPopulation * ( float ) Math.Pow(Decay, Zipf)) + 1;
    }

    public TerrainType Terrain => Sector.Terrain;
    public override string ToString() => $"Location @{PosString} Pop:{Population:F1} ({Type})";
    public Map Map => Sector.Map;
    Encounter? _encounter = null;
    public bool HasEncounter => _encounter != null;
    public void SetEncounter(Encounter encounter) => _encounter = encounter;
    public Encounter GetEncounter() => _encounter ??= NewEncounter(this);
    public string PosString => $"{(int)(Position.X * 100) % 100:D2},{(int)(Position.Y * 100) % 100:D2}";
    public string Description => (HasEncounter ? _encounter!.Name : $"<{Type}>") + $" @{PosString}";
    public string EncounterName(IActor visitor) => (visitor.To(this).Visited && HasEncounter) switch {
        true => _encounter!.Name,
        false => $"<{Type}>",
    };
    // Pareto distribution Type I - seeded deterministically from Seed
    public const float MaxPopulation = 500;
    public const float Decay = 0.0005f;
    internal XorShift Rng;
    public int Population { get; protected set; }
    public float Wealth { get; protected set; } = 1;
    public float TechLatitude => 2 * (1 - ((Position.Y + 0.5f) / Map.Height));
    public string Code => Type switch {
        EncounterType.None => ".",
        EncounterType.Crossroads => "x",
        EncounterType.Settlement => "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[( int ) Math.Log2(Population)].ToString(),
        EncounterType.Resource => "?",
        EncounterType.Hazard => "!",
        _ => "_",
    };
    public Sector Sector { get; init; }
    public Vector2 Position { get; set; }
    public EncounterType Type { get; init; }
    public ulong Seed { get; init; }
    public Func<Location, Encounter> NewEncounter { get; set; }

    // Transit properties for actors traveling on roads
    public Road? TransitRoad { get; set; }
    public float? TransitProgress { get; set; }

    public Factions ChooseRandomFaction() {
        // Get base weights for this terrain type
        var baseWeights = Tuning.Encounter.crawlerSpawnWeight[Terrain];
        var adjustedWeights = new EArray<Factions, float>();
        foreach (var (faction, weight) in baseWeights.Pairs()) {
            adjustedWeights[faction] = weight;
            if (faction == Factions.Player) {
                adjustedWeights[faction] = 0;
            } else if (faction == Factions.Independent) {
            } else if (faction == Factions.Bandit) {
                // Adjust bandit weight by dividing by population (more pop = fewer bandits)
                adjustedWeights[faction] /= Math.Min(1.0f, Population / 100.0f);
            } else if (faction < Map.FactionEnd) {
                adjustedWeights[faction] = baseWeights[Factions.Independent]; // zero in data, code controlled
                if (faction == Sector.ControllingFaction) {
                    adjustedWeights[faction] *= 3.5f;
                } else {
                    adjustedWeights[faction] *= 0.8f;
                }
            } else {
                adjustedWeights[faction] = 0;
            }
        }

        var result = adjustedWeights.Pairs().ChooseWeightedRandom(ref Rng);
        if (result < Game.Instance?.Map.FactionEnd) {
            return result;
        }
        return Factions.Independent;
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

    // Data structure for serialization
    public record class Data {
        public bool Visited { get; set; }
    }

    public Data ToData() {
        return new Data {
            Visited = this.Visited
        };
    }

    public void FromData(Data data) {
        this.Visited = data.Visited;
    }
}

public record FactionData(string Name, Color Color, Capital? Capital);

public record Capital(string Name, Crawler Settlement, float Influence) {
    public Location Location => Settlement.Location;
}

public class Sector {
    public Sector(ulong seed, Map map, string name, int x, int y) {
        Map = map;
        X = x;
        Y = y;
        Name = name;
        Rng = new XorShift(seed);
        Gaussian = new GaussianSampler(Rng.Seed());
        LocalMarkup = InitLocalOfferRates(Rng.Seed());
        LocalSegmentRates = InitLocalSegmentRates(Rng.Seed());
    }
    public Map Map { get; }
    public string Name { get; set; }
    public int X { get; }
    public int Y { get; }
    XorShift Rng;
    GaussianSampler Gaussian;
    public Point Position => new(X, Y);
    public TerrainType Terrain;
    public Factions ControllingFaction = Factions.Independent; // Default to Trade for unassigned
    public readonly List<Sector> Neighbors = new();
    public readonly List<Location> Locations = new();
    public IEnumerable<Location> Settlements => Locations.Where(loc => loc.Type == EncounterType.Settlement);
    public override string ToString() => $"Sector {Name} ({Terrain})";
    public string Look() {
        var result = $"{ControllingFaction.Name()} - {Name} ({Terrain} Terrain) ";
        var locationSummary = Locations.GroupBy(loc => loc.Type).Select(g => $"{g.Key}" + (g.Count() > 1 ? $" ({g.Count()})" : ""));
        result += string.Join(", ", locationSummary);
        return result;
    }
    public Point Offset(Sector other) {
        int dx = X - other.X;
        int dy = Y - other.Y;
        int width = Map.Width;
        if (dx < -width / 2) {
            dx += width;
        } else if (dx > width / 2) {
            dx -= width;
        }
        return new(dx, dy);
    }

    public EArray<Commodity, float> LocalMarkup;
    static EArray<Commodity, float> InitLocalOfferRates(ulong seed) {
        var gaussians = new GaussianSampler(seed);
        EArray<Commodity, float> result = new();
        result.Initialize(() => Tuning.Trade.LocalMarkup(gaussians));
        return result;
    }
    public EArray<SegmentKind, float> LocalSegmentRates;
    static EArray<SegmentKind, float> InitLocalSegmentRates(ulong seed) {
        var gaussians = new GaussianSampler(seed);
        EArray<SegmentKind, float> result = new();
        result.Initialize(() => Tuning.Trade.LocalMarkup(gaussians));
        return result;
    }

    // RNG state accessors for save/load
    public ulong GetRngState() => Rng.GetState();
    public void SetRngState(ulong state) => Rng.SetState(state);
    public ulong GetGaussianRngState() => Gaussian.GetRngState();
    public void SetGaussianRngState(ulong state) => Gaussian.SetRngState(state);
    public bool GetGaussianPrimed() => Gaussian.GetPrimed();
    public void SetGaussianPrimed(bool primed) => Gaussian.SetPrimed(primed);
    public double GetGaussianZSin() => Gaussian.GetZSin();
    public void SetGaussianZSin(double zSin) => Gaussian.SetZSin(zSin);
}

public class Map {
    XorShift Rng;
    GaussianSampler Gaussian;
    public Map(ulong seed, int height, int width) {
        using var activity = LogCat.Game.StartActivity($"new Map({height}, {width})")?
            .AddTag("seed", seed.ToString("X16"));
        Rng = new(seed);
        Gaussian = new(Rng.Seed());
        Height = height;
        Width = width;
        CreateSectors(out Sectors);
        CreateLocations();
        CollectNeighbors();
    }
    public void Construct() {
        CreateFactionCapitals();
        AssignSectorFactions();
        GenerateTradeNetwork();
    }

    void GenerateTradeNetwork() {
        using var activity = LogCat.Game.StartActivity($"{nameof(GenerateTradeNetwork)}");
        TradeNetwork = Network.TradeNetwork.Generate(this, Rng);
    }
    public int NumFactions { get; protected set; }
    public Factions FactionEnd { get; protected set; }
    void CreateSectors(out Sector[,] sectors) {
        sectors = new Sector[Height, Width];
        foreach (var (X, Y) in sectors.Index()) {
            var sectorName = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Y] + $"{X}";
            var sector = new Sector(Rng.Seed(), this, sectorName, X, Y);
            sector.Terrain = ChooseTerrainType(new Vector3(X, Y, 0));

            sectors[Y, X] = sector;
        }
    }
    void CreateLocations() {
        var expectation = 2.5f * Width * Height;
        int k = CrawlerEx.PoissonQuantile(expectation, ref Rng);
        List<Vector3> locations = new();

        uint xseed = ( uint ) Rng.Next() & 0xFFFFFF;
        uint yseed = ( uint ) Rng.Next() & 0xFFFFFF;
        uint zseed = ( uint ) Rng.Next() & 0xFFFFFF;
        for (int i = 0; i < k; ++i) {
            float tx = CrawlerEx.HaltonSequence(2, xseed + ( uint ) i);
            float ty = CrawlerEx.HaltonSequence(5, yseed + ( uint ) i);
            float x = tx * Width;
            float y = ty * Height;

            float xFrac = CrawlerEx.Frac(x);
            float yFrac = CrawlerEx.Frac(y);
            float xFloor = ( float ) Math.Floor(x);
            float yFloor = ( float ) Math.Floor(y);
            var sector = Sectors[( int ) yFloor, ( int ) xFloor];

            locations.Add(new(x, y, ( uint ) zseed + i));
        }
        foreach (var loc in locations) {
            var sector = Sectors[( int ) loc.Y, ( int ) loc.X];
            Vector2 frac = new(CrawlerEx.Frac(loc.X), CrawlerEx.Frac(loc.Y));

            EncounterType encounterType = ChooseEncounterTypes(sector.Terrain, loc);
            float tLat = 1 - loc.Y / ( float ) Height;
            tLat += Gaussian.NextSingle() * 0.05f;
            tLat = Math.Clamp(tLat, 0.0f, 1.0f);
            float wealth = 300 * ( float ) Math.Pow(50, tLat);

            // Encounters are created at map generation time, use initial time
            var newEncounter = (Location loc) => new Encounter(Rng.Seed(), loc).Create(Tuning.StartGameTime);
            var locationSeed = ( ulong ) loc.Z;
            var encounterLocation = new Location(locationSeed,
                sector, new(loc.X, loc.Y), encounterType, wealth, newEncounter);
            sector.Locations.Add(encounterLocation);
        }
    }
    void CollectNeighbors() {
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
    void CreateFactionCapitals() {
        using var activity = LogCat.Game.StartActivity($"{nameof(CreateFactionCapitals)}");
        // Collect all settlement locations
        var settlementLocations = new List<Location>();
        var sectorPopulations = new Dictionary<Sector, float>();
        foreach (var (X, Y) in Sectors.Index()) {
            var sector = Sectors[Y, X];
            var sectorSettlementLocations = sector.Locations.Where(loc => loc.Type == EncounterType.Settlement).ToList();
            settlementLocations.AddRange(sectorSettlementLocations);
        }

        // Sort by population descending
        settlementLocations = settlementLocations
            .Where(loc => !loc.HasEncounter)
            .OrderByDescending(loc => loc.Population)
            .ToList();

        NumFactions = Math.Min(Height * 3 / 4, 20);
        NumFactions = Math.Min(NumFactions, settlementLocations.Count);
        FactionEnd = Factions.Civilian0 + NumFactions;

        FactionData[Factions.Player] = new ("Player", Color.White, null);
        FactionData[Factions.Independent] = new ("Independent", Color.Blue, null);
        FactionData[Factions.Bandit] = new ("Bandit", Color.Red, null);

        var policies = FactionEx.GenerateFactionPolicies(NumFactions, Rng.Seed()).ToArray();
        for (int j = 0; j < NumFactions; ++j) {
            var faction = Factions.Civilian0 + j;
            faction.SetPolicy(policies[j]);
        }

        for (Factions faction = Factions.Civilian0; faction < FactionEnd; faction++) {
            var setLocation = settlementLocations[faction - Factions.Civilian0];
            var sector = setLocation.Sector;
            sector.ControllingFaction = faction;
            var encounter = new Encounter(Rng.Seed(), setLocation, faction);
            var crawler = encounter.CreateCapital(Rng.Seed());
            encounter.AddActorAt(crawler, encounter.EncounterTime);
            // Capital creation happens at map generation, use initial time
            //encounter.SpawnDynamicCrawlers(encounter.EncounterTime, Tuning.StartGameTime);
            //var sectorPopulation = sector.Locations.Sum(loc => loc.Population);
            float influence = 5 + crawler.Domes;
            var factionName = crawler.Name.MakeFactionName(Rng.Seed());
            var capitalName = encounter.Name.MakeCapitalName(Rng.Seed());
            var capital = new Capital(capitalName, crawler, influence);
            FactionData[faction] = new (factionName, FactionEx._factionColors[faction], capital);
            sector.Name = factionName + " Capital";
        }
    }

    void AssignSectorFactions() {
        using var activity = LogCat.Game.StartActivity($"{nameof(AssignSectorFactions)}");
        // Weighted Voronoi: assign each sector to nearest faction capital weighted by population
        foreach (var (X, Y) in Sectors.Index()) {
            var sector = Sectors[Y, X];

            // Calculate weighted distance to each capital
            float minWeightedDistance = float.MaxValue;
            Factions closestFaction = Factions.Independent;

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

    public Location GetStartingLocation() {
        for (int Y = Height - 1; Y >= 0; --Y) {
            int DX = Rng.NextInt(Width);
            for (int X = 0; X < Width; ++X) {
                var X2 = (X + DX) % Width;
                var sector = Sectors[Y, X2];
                var locs = sector.Locations
                    .Where(loc => loc.Type == EncounterType.Settlement)
                    .Where(loc => loc.Sector.ControllingFaction != Factions.Bandit)
                    .Where(loc => loc.Terrain <= TerrainType.Rough)
                    .ToList();
                if (locs.Count > 0) {
                    return sector.Locations[0];
                }
            }
        }
        throw new Exception("No starting location found");
    }
    TerrainType ChooseTerrainType(Vector3 position) {
        float tTerrain = Rng.NextSingle();
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
    EncounterType ChooseEncounterTypes(TerrainType terrain, Vector3 loc) {
        // loc.Z contains the base halton index
        float tEncounter = ( float ) CrawlerEx.HaltonSequence(11, (uint)loc.Z);
        EArray<EncounterType, float> encounterWeights = [];
        switch (terrain) {
            // None, Crossroads, Settlement, Resource, Hazard
        case TerrainType.Flat:
            encounterWeights = [0, 4f, 1.5f, 5f, 2.5f];  // More resources in accessible terrain
            break;
        case TerrainType.Rough:
            encounterWeights = [0, 4.5f, 1, 5f, 2.5f];
            break;
        case TerrainType.Broken:
            encounterWeights = [0, 3f, 0.75f, 6f, 3.25f];  // Rich resource areas
            break;
        case TerrainType.Shattered:
            encounterWeights = [0, 1.5f, 0.5f, 7f, 3f];  // Abundant but dangerous resources
            break;
        case TerrainType.Ruined:
            encounterWeights = [0, 0, 0, 0, 0];
            break;
        }
        return encounterWeights.Pairs().ChooseWeightedAt(tEncounter);
    }
    public readonly int Height;
    public readonly int Width;
    Sector[,] Sectors;
    public EArray<Factions, FactionData?> FactionData { get; } = new();

    /// <summary>The trade network connecting settlements and crossroads.</summary>
    public Network.TradeNetwork? TradeNetwork { get; private set; }

    /// <summary>Iterate over all sectors in the map.</summary>
    public IEnumerable<Sector> AllSectors {
        get {
            foreach (var (x, y) in Sectors.Index()) {
                yield return Sectors[y, x];
            }
        }
    }

    /// <summary>Iterate over all locations in the map.</summary>
    public IEnumerable<Location> AllLocations => AllSectors.SelectMany(s => s.Locations);

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
        header += new string('─', Math.Max(0, worldMapWidth - 12)) + "╖";
        header += FactionLine1(Factions.Independent) + "\n";
        var footer = defaultStyle + $"╘{new string('═', worldMapWidth)}╝\n";

        string result = header;

        result += "│ :" + titleStyle;
        for (int x = 0; x < Width; x++) {
            result += $"{x,3}";
        }
        result += defaultStyle + "║";
        result += FactionLine2(Factions.Independent) + "\n";
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
            var faction = Factions.Civilian0 + y;
            if (faction < FactionEnd) {
                var data = FactionData[faction]!;
                var option = Style.MenuOption.Format($"{y + 1}");
                var capitalString = faction.GetColor().On(Color.Black) + data.Name + defaultStyle;
                var policy = faction.GetPolicy();
                map += FactionLine1(faction);
                map2 += FactionLine2(faction);
            }

            result += $"{map}\n{map2}\n";
        }
        result += footer;
        return result;
    }
    string FactionLine1(Factions faction) {
        var data = FactionData[faction]!;
        var index = faction.CivilianIndex();
        var option = Style.MenuOption.Format($"{index + 1}");
        var policy = faction.GetPolicy().Description;
        if (data.Capital is { } capital) {
            var capitalString = faction.GetColor().On(Color.Black) + $"{data.Name} {faction.GetFlag()}" + Style.MenuNormal.StyleString();
            return $" [{option}] {capitalString}: {capital.Name} at {capital.Location.GetEncounter().Name} | {policy}";
        } else {
            return $" [{option}] {data.Name} {policy}";
        }
    }
    string FactionLine2(Factions faction) {
        string result = " ";
        var policy = faction.GetPolicy();

        // we want to build a set of categories by TradePolicy
        EArray<TradePolicy, List<string>> categoriesByPolicy = new();
        categoriesByPolicy.Initialize(() => new List<string>());
        foreach (var (commodityCategory, tradePolicy) in policy.Commodities.Pairs()) {
            categoriesByPolicy[tradePolicy].Add(commodityCategory.ToString());
        }
        foreach (var (segmentKind, tradePolicy) in policy.Segments.Pairs()) {
            categoriesByPolicy[tradePolicy].Add($"{segmentKind} Segments");
        }
        foreach (var (tradePolicy, categories) in categoriesByPolicy.Pairs()) {
            if (tradePolicy is not TradePolicy.Legal && categories.Count > 0) {
                result += string.Join(", ", categories) + $": {tradePolicy}. ";
            }
        }
        return result;
    }
}
