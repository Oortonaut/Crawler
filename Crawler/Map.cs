using System.Drawing;
using System.Numerics;
using Crawler.Logging;
using Crawler.Network;
using Noise;

namespace Crawler;

public enum TerrainType {
    Flat,
    Rough,
    Broken,
    Shattered,
    Ruined,
}

/// <summary>
/// High-resolution terrain grid using noise-based generation.
/// Stores terrain values on a ~10km cell grid and maps to TerrainType based on latitude.
/// </summary>
public class TerrainGrid {
    readonly float[,] _values;
    readonly int _gridWidth;
    readonly int _gridHeight;
    readonly float _worldWidth;  // In coordinate units (1 unit = 1000km)
    readonly float _worldHeight;
    readonly float _cellSize;    // In km

    /// <summary>
    /// Create a terrain grid for the given world dimensions.
    /// </summary>
    /// <param name="seed">RNG seed for deterministic generation</param>
    /// <param name="worldWidth">World width in coordinate units (1 unit = 1000km)</param>
    /// <param name="worldHeight">World height in coordinate units</param>
    /// <param name="cellSizeKm">Size of each terrain cell in km (default 10)</param>
    public TerrainGrid(ulong seed, float worldWidth, float worldHeight, float cellSizeKm = 10f) {
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        _cellSize = cellSizeKm;

        // Calculate grid dimensions
        float worldWidthKm = worldWidth * Tuning.Map.KilometersPerUnit;
        float worldHeightKm = worldHeight * Tuning.Map.KilometersPerUnit;
        _gridWidth = (int)Math.Ceiling(worldWidthKm / cellSizeKm);
        _gridHeight = (int)Math.Ceiling(worldHeightKm / cellSizeKm);

        _values = new float[_gridHeight, _gridWidth];
        GenerateTerrain(seed);
    }

    void GenerateTerrain(ulong seed) {
        // Create noise generator with octaves for natural-looking terrain
        var baseNoise = new OpenSimplexNoise((long)seed);
        var octaveNoise = new OctaveNoise(baseNoise, Tuning.Map.TerrainNoiseOctaves, 0.5);
        octaveNoise.InitialSize = 1.0 / Tuning.Map.TerrainNoiseFreq;

        for (int y = 0; y < _gridHeight; y++) {
            for (int x = 0; x < _gridWidth; x++) {
                // Convert grid position to world coordinates (0-1 range for noise)
                double nx = (double)x / _gridWidth;
                double ny = (double)y / _gridHeight;

                // Sample noise - returns value in roughly [-1, 1]
                double noiseValue = octaveNoise.Evaluate(nx, ny);

                // Store as [0, 1] range
                _values[y, x] = (float)((noiseValue + 1) / 2);
            }
        }
    }

    /// <summary>
    /// Get the terrain type at a world position.
    /// </summary>
    /// <param name="position">Position in world coordinates (0-Width, 0-Height)</param>
    public TerrainType TerrainAt(Vector2 position) {
        float noiseValue = SampleNoise(position);
        float latitude = position.Y / _worldHeight; // 0 = south (substellar), 1 = north (rim)
        return MapToTerrainType(noiseValue, latitude);
    }

    /// <summary>
    /// Get the raw noise value at a world position (0-1 range).
    /// </summary>
    public float SampleNoise(Vector2 position) {
        // Convert world position to grid coordinates
        float gx = (position.X / _worldWidth) * _gridWidth;
        float gy = (position.Y / _worldHeight) * _gridHeight;

        // Handle horizontal wrapping
        gx = ((gx % _gridWidth) + _gridWidth) % _gridWidth;
        gy = Math.Clamp(gy, 0, _gridHeight - 1);

        // Bilinear interpolation
        int x0 = (int)gx;
        int y0 = (int)gy;
        int x1 = (x0 + 1) % _gridWidth;
        int y1 = Math.Min(y0 + 1, _gridHeight - 1);

        float fx = gx - x0;
        float fy = gy - y0;

        float v00 = _values[y0, x0];
        float v10 = _values[y0, x1];
        float v01 = _values[y1, x0];
        float v11 = _values[y1, x1];

        float v0 = v00 * (1 - fx) + v10 * fx;
        float v1 = v01 * (1 - fx) + v11 * fx;

        return v0 * (1 - fy) + v1 * fy;
    }

    /// <summary>
    /// Map a noise value to terrain type based on latitude.
    /// Uses the same latitude band distribution as the original sector-based system.
    /// </summary>
    TerrainType MapToTerrainType(float noiseValue, float latitude) {
        // Get terrain weights for this latitude band
        // latitude 0 = substellar (hot, harsh), latitude 1 = rim (cold, flat)
        EArray<TerrainType, float> weights = GetLatitudeWeights(latitude);

        // Normalize weights to cumulative distribution
        float total = 0;
        foreach (var (_, w) in weights.Pairs()) total += w;
        if (total <= 0) return TerrainType.Flat;

        float cumulative = 0;
        foreach (var (terrain, w) in weights.Pairs()) {
            cumulative += w / total;
            if (noiseValue < cumulative) {
                return terrain;
            }
        }
        return TerrainType.Flat;
    }

    /// <summary>
    /// Get terrain type weights for a given latitude (0 = substellar, 1 = rim).
    /// Matches the original sector-based terrain distribution.
    /// </summary>
    static EArray<TerrainType, float> GetLatitudeWeights(float latitude) {
        // 5 latitude bands from original implementation
        int band = (int)(latitude * 5);
        band = Math.Clamp(band, 0, 4);

        return band switch {
            0 => new EArray<TerrainType, float> { [TerrainType.Flat] = 1, [TerrainType.Rough] = 3, [TerrainType.Broken] = 6, [TerrainType.Shattered] = 12, [TerrainType.Ruined] = 0 },
            1 => new EArray<TerrainType, float> { [TerrainType.Flat] = 3, [TerrainType.Rough] = 6, [TerrainType.Broken] = 6, [TerrainType.Shattered] = 3, [TerrainType.Ruined] = 0 },
            2 => new EArray<TerrainType, float> { [TerrainType.Flat] = 6, [TerrainType.Rough] = 6, [TerrainType.Broken] = 3, [TerrainType.Shattered] = 1, [TerrainType.Ruined] = 0 },
            3 => new EArray<TerrainType, float> { [TerrainType.Flat] = 12, [TerrainType.Rough] = 6, [TerrainType.Broken] = 3, [TerrainType.Shattered] = 1, [TerrainType.Ruined] = 0 },
            4 => new EArray<TerrainType, float> { [TerrainType.Flat] = 6, [TerrainType.Rough] = 3, [TerrainType.Broken] = 1, [TerrainType.Shattered] = 0, [TerrainType.Ruined] = 0 },
            _ => new EArray<TerrainType, float> { [TerrainType.Flat] = 1 }
        };
    }
}

public record Location {
    public Location(ulong Seed,
        Map Map, Vector2 Position,
        EncounterType Type, float Wealth,
        Func<Location, Encounter> NewEncounter) {
        if (Wealth <= 0) {
            throw new ArgumentException("Wealth must be positive");
        }
        Rng = new XorShift(Seed);
        this.Seed = Seed;
        this.Map = Map;
        this.Position = Position;
        this.Type = Type;
        this.Wealth = Wealth;
        this.NewEncounter = NewEncounter;
        float Zipf = Rng.NextSingle();
        Population = (int)(MaxPopulation * (float)Math.Pow(Decay, Zipf)) + 1;

        // Initialize local pricing variation
        var gaussian = new GaussianSampler(Rng.Seed());
        LocalMarkup = InitLocalOfferRates(gaussian);
        LocalSegmentRates = InitLocalSegmentRates(gaussian);
    }

    public TerrainType Terrain => Map.TerrainGrid.TerrainAt(Position);
    public override string ToString() => $"Location @{PosString} Pop:{Population:F1} ({Type})";
    public Map Map { get; init; }
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
    public int Population { get; set; }
    public float Wealth { get; protected set; } = 1;
    public float TechLatitude => 2 * (1 - ((Position.Y + 0.5f) / Map.Height));
    public string Code => Type switch {
        EncounterType.None => ".",
        EncounterType.Crossroads => "x",
        EncounterType.Settlement => "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[(int)Math.Log2(Population)].ToString(),
        EncounterType.Resource => "?",
        EncounterType.Hazard => "!",
        _ => "_",
    };
    public Vector2 Position { get; set; }
    public EncounterType Type { get; init; }
    public ulong Seed { get; init; }
    public Func<Location, Encounter> NewEncounter { get; set; }

    // Per-location faction control (replaces Sector.ControllingFaction)
    public Factions ControllingFaction { get; set; } = Factions.Independent;

    // Per-location pricing variation (moved from Sector)
    public EArray<Commodity, float> LocalMarkup { get; init; }
    public EArray<SegmentKind, float> LocalSegmentRates { get; init; }

    static EArray<Commodity, float> InitLocalOfferRates(GaussianSampler gaussians) {
        EArray<Commodity, float> result = new();
        result.Initialize(() => Tuning.Trade.LocalMarkup(gaussians));
        return result;
    }

    static EArray<SegmentKind, float> InitLocalSegmentRates(GaussianSampler gaussians) {
        EArray<SegmentKind, float> result = new();
        result.Initialize(() => Tuning.Trade.LocalMarkup(gaussians));
        return result;
    }

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
                if (faction == ControllingFaction) {
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
        const float kilometersPerUnit = 1000;
        var d = Offset(other) * kilometersPerUnit;
        return d.Length();
    }

    public float Distance(Vector2 otherPosition) {
        const float kilometersPerUnit = 1000;
        var d = Offset(otherPosition) * kilometersPerUnit;
        return d.Length();
    }

    public Vector2 Offset(Location other) => Offset(other.Position);

    public Vector2 Offset(Vector2 otherPosition) {
        float dx = Position.X - otherPosition.X;
        float dy = Position.Y - otherPosition.Y;
        float width = Map.Width;
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

public class Map {
    XorShift Rng;
    GaussianSampler Gaussian;

    public Map(ulong seed, int height, int width) {
        using var activity = LogCat.Game.StartActivity($"new Map({height}, {width})")?
            .AddTag("seed", seed.ToString("X16"));
        Seed = seed;
        Rng = new(seed);
        Gaussian = new(Rng.Seed());
        Height = height;
        Width = width;

        // Create high-resolution terrain grid
        TerrainGrid = new TerrainGrid(Rng.Seed(), width, height, Tuning.Map.TerrainCellSize);

        // Generate locations directly (no sectors)
        CreateLocations();
    }

    /// <summary>The seed used to generate this map.</summary>
    public ulong Seed { get; }

    public void Construct() {
        CreateFactionCapitals();
        AssignLocationFactions();
        GenerateTradeNetwork();
    }

    void GenerateTradeNetwork() {
        using var activity = LogCat.Game.StartActivity($"{nameof(GenerateTradeNetwork)}");
        TradeNetwork = Network.TradeNetwork.Generate(this, Rng);
    }

    public int NumFactions { get; protected set; }
    public Factions FactionEnd { get; protected set; }

    void CreateLocations() {
        // Expected locations based on area (same density as before: ~2.5 per sector-equivalent)
        var expectation = Tuning.Map.LocationDensity * Width * Height;
        int k = CrawlerEx.PoissonQuantile(expectation, ref Rng);

        uint xseed = (uint)Rng.Next() & 0xFFFFFF;
        uint yseed = (uint)Rng.Next() & 0xFFFFFF;
        uint zseed = (uint)Rng.Next() & 0xFFFFFF;

        for (int i = 0; i < k; ++i) {
            float tx = CrawlerEx.HaltonSequence(2, xseed + (uint)i);
            float ty = CrawlerEx.HaltonSequence(5, yseed + (uint)i);
            float x = tx * Width;
            float y = ty * Height;

            var position = new Vector2(x, y);
            var terrain = TerrainGrid.TerrainAt(position);

            // Choose encounter type based on terrain
            EncounterType encounterType = ChooseEncounterType(terrain, (uint)(zseed + i));

            // Skip if terrain produces no locations (Ruined)
            if (encounterType == EncounterType.None && terrain == TerrainType.Ruined) {
                continue;
            }

            // Calculate wealth based on latitude
            float tLat = 1 - y / (float)Height;
            tLat += Gaussian.NextSingle() * 0.05f;
            tLat = Math.Clamp(tLat, 0.0f, 1.0f);
            float wealth = 300 * (float)Math.Pow(50, tLat);

            // Create location
            var newEncounter = (Location loc) => new Encounter(Rng.Seed(), loc).Create(Tuning.StartGameTime);
            var locationSeed = (ulong)zseed + (ulong)i;
            var location = new Location(locationSeed, this, position, encounterType, wealth, newEncounter);
            _locations.Add(location);
        }
    }

    void CreateFactionCapitals() {
        using var activity = LogCat.Game.StartActivity($"{nameof(CreateFactionCapitals)}");

        // Collect all settlement locations sorted by population
        var settlementLocations = _locations
            .Where(loc => loc.Type == EncounterType.Settlement)
            .Where(loc => !loc.HasEncounter)
            .OrderByDescending(loc => loc.Population)
            .ToList();

        NumFactions = Math.Min(Height * 3 / 4, 20);
        NumFactions = Math.Min(NumFactions, settlementLocations.Count);
        FactionEnd = Factions.Civilian0 + NumFactions;

        FactionData[Factions.Player] = new("Player", Color.White, null);
        FactionData[Factions.Independent] = new("Independent", Color.Blue, null);
        FactionData[Factions.Bandit] = new("Bandit", Color.Red, null);

        var policies = FactionEx.GenerateFactionPolicies(NumFactions, Rng.Seed()).ToArray();
        for (int j = 0; j < NumFactions; ++j) {
            var faction = Factions.Civilian0 + j;
            faction.SetPolicy(policies[j]);
        }

        for (Factions faction = Factions.Civilian0; faction < FactionEnd; faction++) {
            var setLocation = settlementLocations[faction - Factions.Civilian0];
            setLocation.ControllingFaction = faction;

            var encounter = new Encounter(Rng.Seed(), setLocation, faction);
            var crawler = encounter.CreateCapital(Rng.Seed());
            encounter.AddActorAt(crawler, encounter.EncounterTime);

            float influence = 5 + crawler.Domes;
            var factionName = crawler.Name.MakeFactionName(Rng.Seed());
            var capitalName = encounter.Name.MakeCapitalName(Rng.Seed());
            var capital = new Capital(capitalName, crawler, influence);
            FactionData[faction] = new(factionName, FactionEx._factionColors[faction], capital);
        }
    }

    void AssignLocationFactions() {
        using var activity = LogCat.Game.StartActivity($"{nameof(AssignLocationFactions)}");

        // Weighted Voronoi: assign each location to nearest faction capital
        foreach (var location in _locations) {
            // Skip capitals (already assigned)
            if (location.HasEncounter) continue;

            float minWeightedDistance = float.MaxValue;
            Factions closestFaction = Factions.Independent;

            foreach (var (faction, data) in FactionData.Pairs()) {
                if (data?.Capital is { } capital) {
                    float distance = location.Distance(capital.Location);
                    float weightedDistance = distance / (capital.Influence * Tuning.Map.KilometersPerUnit);

                    if (weightedDistance < minWeightedDistance) {
                        minWeightedDistance = weightedDistance;
                        closestFaction = faction;
                    }
                }
            }

            location.ControllingFaction = closestFaction;
        }
    }

    public Location GetStartingLocation() {
        // Find a settlement in the rim (high Y) that's not bandit-controlled
        var candidates = _locations
            .Where(loc => loc.Type == EncounterType.Settlement)
            .Where(loc => loc.ControllingFaction != Factions.Bandit)
            .Where(loc => loc.Terrain <= TerrainType.Rough)
            .OrderByDescending(loc => loc.Position.Y)  // Prefer rim (high Y)
            .ThenBy(loc => Rng.NextSingle())  // Random among top candidates
            .ToList();

        if (candidates.Count > 0) {
            return candidates[0];
        }
        throw new Exception("No starting location found");
    }

    EncounterType ChooseEncounterType(TerrainType terrain, uint haltonSeed) {
        float tEncounter = (float)CrawlerEx.HaltonSequence(11, haltonSeed);
        EArray<EncounterType, float> encounterWeights = terrain switch {
            // None, Crossroads, Settlement, Resource, Hazard
            TerrainType.Flat => [0, 4f, 1.5f, 5f, 2.5f],
            TerrainType.Rough => [0, 4.5f, 1, 5f, 2.5f],
            TerrainType.Broken => [0, 3f, 0.75f, 6f, 3.25f],
            TerrainType.Shattered => [0, 1.5f, 0.5f, 7f, 3f],
            TerrainType.Ruined => [0, 0, 0, 0, 0],
            _ => [0, 4f, 1.5f, 5f, 2.5f]
        };
        return encounterWeights.Pairs().ChooseWeightedAt(tEncounter);
    }

    // Core map properties
    public readonly int Height;
    public readonly int Width;
    public TerrainGrid TerrainGrid { get; }
    readonly List<Location> _locations = [];

    public EArray<Factions, FactionData?> FactionData { get; } = new();

    /// <summary>The trade network connecting settlements and crossroads.</summary>
    public Network.TradeNetwork? TradeNetwork { get; private set; }

    /// <summary>Iterate over all locations in the map.</summary>
    public IEnumerable<Location> AllLocations => _locations;

    /// <summary>Find all locations within a radius (in coordinate units, where 1 unit = 1000km).</summary>
    public IEnumerable<Location> FindLocationsInRadius(Vector2 center, float radiusUnits) {
        foreach (var location in _locations) {
            if (location.Distance(center) <= radiusUnits * Tuning.Map.KilometersPerUnit) {
                yield return location;
            }
        }
    }

    /// <summary>Find all locations within a radius in km.</summary>
    public IEnumerable<Location> FindLocationsInRadiusKm(Vector2 center, float radiusKm) {
        foreach (var location in _locations) {
            if (location.Distance(center) <= radiusKm) {
                yield return location;
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

    /// <summary>Draw a local area map around a position.</summary>
    public string DumpLocalArea(Vector2 center, int drawWidth = 32, int drawHeight = 16, params Crawler[] players) {
        // Find nearby locations
        float rangeKm = Tuning.Map.LocalRange;
        float rangeUnits = rangeKm / Tuning.Map.KilometersPerUnit;
        var nearbyLocations = FindLocationsInRadiusKm(center, rangeKm)
            .OrderBy(loc => loc.Distance(center))
            .Take(20)
            .ToList();

        var localMap = new char[drawHeight, drawWidth];

        // Fill with terrain based on center position
        var centerTerrain = TerrainGrid.TerrainAt(center);
        var bg = TerrainCode(centerTerrain);
        localMap.Fill(bg);

        // Plot locations
        foreach (var (i, location) in nearbyLocations.Index()) {
            var offset = location.Offset(center);
            // Map offset to screen coordinates
            int cx = (int)((offset.X / rangeUnits + 1) * (drawWidth - 2) / 2);
            int cy = (int)((offset.Y / rangeUnits + 1) * (drawHeight - 2) / 2);

            if (cx >= 0 && cx < drawWidth - 1 && cy >= 0 && cy < drawHeight - 1) {
                localMap[cy + 1, cx] = location.Code[0];
                if (players.Any(c => c.Location == location)) {
                    localMap[cy + 1, cx + 1] = '@';
                }
                // Add index label
                var idx = "M" + (i + 1).ToString();
                int col = cx;
                foreach (var c in idx) {
                    if (col < drawWidth) {
                        localMap[cy, col] = c;
                        col++;
                    }
                }
            }
        }

        // Build result string
        string result = "";
        for (int i = 0; i < drawHeight; ++i) {
            for (int j = 0; j < drawWidth; ++j) {
                result += localMap[i, j];
            }
            if (i < drawHeight - 1) {
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

        // Group locations by grid cell for display
        for (int y = 0; y < Height; y++) {
            string map = defaultStyle + "│" + titleStyle + (char)('A' + y) + defaultStyle + ":";
            string map2 = defaultStyle + "│ :";

            for (int x = 0; x < Width; x++) {
                // Get locations in this grid cell
                var cellLocations = _locations.Where(loc =>
                    (int)loc.Position.X == x && (int)loc.Position.Y == y).ToList();

                // Determine dominant faction in cell
                var factionCounts = cellLocations
                    .GroupBy(loc => loc.ControllingFaction)
                    .OrderByDescending(g => g.Count())
                    .ToList();
                var dominantFaction = factionCounts.FirstOrDefault()?.Key ?? Factions.Independent;

                var cellColor = dominantFaction.GetColor();
                var cellStyle = cellColor.On(Color.Black);

                // Check if player is in this cell
                int playerIndex = players.FindIndex(p =>
                    (int)p.Location.Position.X == x && (int)p.Location.Position.Y == y);
                if (playerIndex >= 0) {
                    cellStyle = cellColor.On(Color.DarkBlue);
                }

                // Sample terrain at cell center
                var cellCenter = new Vector2(x + 0.5f, y + 0.5f);
                var terrain = TerrainGrid.TerrainAt(cellCenter);
                char terrainChar = TerrainCode(terrain);

                string top = cellStyle;
                string bottom = cellStyle;
                int bottomWidth = 0;

                // Mark settlements
                foreach (var settlement in cellLocations.Where(loc => loc.Type == EncounterType.Settlement)) {
                    if (bottomWidth < 3) {
                        bottom += settlement.Code;
                        ++bottomWidth;
                    }
                }

                top += new string(terrainChar, 3);
                bottom += new string(terrainChar, 3 - bottomWidth);

                map += top;
                map2 += bottom;
            }
            map += defaultStyle + "║";
            map2 += defaultStyle + "║";

            // Add civilian faction info for this row
            var faction = Factions.Civilian0 + y;
            if (faction < FactionEnd) {
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
