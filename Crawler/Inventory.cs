using System.Linq;

namespace Crawler;


public enum ContainsResult {
    False,      // Cannot fulfill (even with overdraft)
    True,       // Can fulfill from current inventory
    Overdraft   // Can fulfill, but requires pulling from overdraft
}

public class Inventory {
    public Inventory() { }
    public Inventory(EArray<Commodity, float> InItemCounts, IEnumerable<Segment> InSegments) {
        _commodities = InItemCounts.Clone();
        Segments = InSegments.ToList();
    }
    public Inventory(params Segment[] InSegments) {
        Segments = InSegments.ToList();
    }

    EArray<Commodity, float> _commodities = new();
    public IReadOnlyList<float> Commodities => _commodities.Items;
    public List<Segment> Segments { get; } = new();
    // A linked list of overdraft inventories, which can be used to fulfill requests.
    public Inventory? Overdraft { get; set; } = null;
    public bool IsEmpty => _commodities.All(c => c == 0) && Segments.Count == 0;
    public void Clear() {
        _commodities.Initialize(0);
        Segments.Clear();
    }

    public float this[Commodity c] {
        get => _commodities[c];
        set {
            var currentValue = _commodities[c];
            var newValue = c.Round(Math.Max(0, value));

            // If withdrawing (new value < current value)
            if (newValue < currentValue) {
                var shortfall = currentValue - newValue;
                var available = currentValue;

                // Try to fulfill from current inventory
                if (available >= shortfall) {
                    _commodities[c] = newValue;
                } else {
                    // Need to pull from overdraft (recursively)
                    var deficit = shortfall - available;
                    _commodities[c] = 0; // Drain current inventory
                    if (Overdraft != null) {
                        Overdraft.Remove(c, deficit); // Recursive call
                    }
                }
            } else {
                // Depositing, just set the value
                _commodities[c] = newValue;
            }
        }
    }
    public void Add(Commodity commodity, float addCount) {
        _commodities[commodity] = commodity.Round(_commodities[commodity] + addCount);
    }
    public void Remove(Commodity commodity, float removeCount) {
        var currentValue = _commodities[commodity];
        var newValue = currentValue - removeCount;

        if (newValue >= 0) {
            // Can fulfill from current inventory
            _commodities[commodity] = commodity.Round(newValue);
        } else {
            // Need overdraft (recursively)
            var deficit = -newValue;
            _commodities[commodity] = 0; // Drain current inventory
            if (Overdraft != null) {
                Overdraft.Remove(commodity, deficit); // Recursive call
            }
        }
    }
    public void Add(Segment s) => Segments.Add(s);
    public void Remove(Segment s) {
        if (Segments.Contains(s)) {
            Segments.Remove(s);
        } else if (Overdraft != null) {
            Overdraft.Remove(s);
        } else {
            throw new Exception("Segment not found in inventory.");
        }
    }

    public Inventory Clone() {
        var commoditiesCopy = _commodities.Clone();
        var segmentsCopy = Segments.Select(s => s.Clone()).ToList();
        return new Inventory(commoditiesCopy, segmentsCopy);
    }

    public override string ToString() {
        var result = Brief();

        var segmentCounts = Segments.GroupBy(s => s.SegmentDef).ToDictionary(g => g.Key, g => g.Count());
        if (segmentCounts.Count > 0) {
            if (result.Length > 0) {
                result += "; ";
            }
            result += string.Join(", ",
                segmentCounts
                    .Select(nameCount =>
                        nameCount.Value > 1 ? $"{nameCount.Value} {nameCount.Key.Name}" : $"{nameCount.Key.Name}")
                    .Where(s => !string.IsNullOrEmpty(s)));
        }
        return result;
    }
    public string Brief() {
        if (IsEmpty) {
            return "nothing";
        }
        var result = _commodities
                .Pairs().ToList()
                .Select(kv => kv.Key.CommodityText(kv.Value))
                .Where(s => !string.IsNullOrEmpty(s))
                .StringJoin(", ")
            ;
        return result;
    }
    public void Add(Inventory other) {
        other._commodities
            .Pairs()
            .Do(ii => _commodities[ii.Key] = ii.Key.Round(_commodities[ii.Key] + ii.Value));
        Segments.AddRange(other.Segments);
    }
    public void Remove(Inventory other) {
        var containsResult = Contains(other);
        if (containsResult == ContainsResult.False) {
            throw new Exception("Inventory doesn't contain other.");
        }

        // Remove commodities (with overdraft if needed)
        foreach (var (commodity, amount) in other._commodities.Pairs()) {
            if (amount > 0) {
                Remove(commodity, amount);
            }
        }

        // Remove segments (recursively through overdraft chain)
        foreach (var otherSegment in other.Segments) {
            Remove(otherSegment);
        }
    }

    public float Mass => Segments.Sum(s => s.Weight) + _commodities.Pairs().Sum(ii => ii.Key.Mass() * ii.Value);
    public float ItemValueAt(Location loc) => _commodities.Pairs().Sum(ii => ii.Key.CostAt(loc) * ii.Value);
    public float SegmentValueAt(Location loc) => Segments.Sum(s => s.Cost * Tuning.Economy.LocalMarkup(s.SegmentKind, loc));
    public float ValueAt(Location loc) => ItemValueAt(loc) + SegmentValueAt(loc);

    public static float QuantityBought(float spent, Commodity comm, Location location) {
        spent /= comm.CostAt(location);
        return comm.Round(spent);
    }
    public static float QuantitySold(float paid, Commodity comm, Location location) {
        paid /= comm.CostAt(location);
        return comm.Round(paid);
    }

    public ContainsResult Contains(Inventory other) {
        // Check if current inventory alone can fulfill
        var needsOverdraft = false;
        var shortfallCommodities = new EArray<Commodity, float>();
        var shortfallSegments = new List<Segment>();

        // Check commodities and calculate shortfalls
        for (int i = 0; i < Commodities.Count; i++) {
            var shortfall = other.Commodities[i] - Commodities[i];
            if (shortfall > 0) {
                needsOverdraft = true;
                shortfallCommodities[(Commodity)i] = shortfall;
            }
        }

        // Check segments and calculate shortfalls
        var countBySegment = Segments
            .GroupBy(s => s.SegmentDef)
            .Select(g => (g.Key, g.Count()))
            .ToDictionary();

        foreach (var segment in other.Segments) {
            if (countBySegment.TryGetValue(segment.SegmentDef, out var count) && count > 0) {
                countBySegment[segment.SegmentDef]--;
            } else {
                needsOverdraft = true;
                shortfallSegments.Add(segment);
            }
        }

        // If current inventory is sufficient, return True
        if (!needsOverdraft) {
            return ContainsResult.True;
        }

        // If no overdraft available, return False
        if (Overdraft == null) {
            return ContainsResult.False;
        }

        // Recursively check if overdraft can fulfill the shortfall
        var shortfallInventory = new Inventory(shortfallCommodities, shortfallSegments);
        var overdraftResult = Overdraft.Contains(shortfallInventory);

        // If overdraft can fulfill (either directly or via its own overdraft chain)
        if (overdraftResult != ContainsResult.False) {
            return ContainsResult.Overdraft;
        }

        return ContainsResult.False;
    }
    public ContainsResult Contains(Commodity commodity, float amt) {
        amt = commodity.Round(amt);
        var held = _commodities[commodity];
        if (held >= amt) {
            return ContainsResult.True;
        } else if (Overdraft == null) {
            return ContainsResult.False;
        } else {
            return Overdraft.Contains(commodity, amt - held) == ContainsResult.False ? ContainsResult.False : ContainsResult.Overdraft;
        }
    }
    public ContainsResult Contains(Segment segment, int n = 1) {
        var here = Segments.Count(s => s.SegmentDef == segment.SegmentDef);
        if (here <= n) {
            return ContainsResult.True;
        } else if (Overdraft == null) {
            return ContainsResult.False;
        } else {
            n -= here;
            return Overdraft.Contains(segment, n) == ContainsResult.False ? ContainsResult.False : ContainsResult.Overdraft;
        }
    }
    public void AddRandomInventory(ulong seed, Location Loc, int crew, float supplyDays, float goodsWealth, float segmentWealth, bool includeCore = false, EArray<SegmentKind, float>? segmentClassWeights = null, Faction faction = Faction.Player) {
        var Rng = new XorShift(seed);
        var crewSeed = Rng.Seed();
        var goodsSeed = Rng.Seed();
        var segmentSeed = Rng.Seed();
        AddCrewInventory(crewSeed, crew, supplyDays);
        AddCommodityInventory(goodsSeed, Loc, goodsWealth, faction);
        AddSegmentInventory(segmentSeed, Loc, segmentWealth, includeCore, segmentClassWeights);
    }
    public void AddCrewInventory(ulong seed, int crew, float supplyDays = 10) {
        var Gaussians = new GaussianSampler(seed);
        float rationsPerCrewDay = Tuning.Crawler.RationsPerCrewDay;
        float waterPerCrewDay = Tuning.Crawler.WaterPerCrew * 24 * Tuning.Crawler.WaterRecyclingLossPerHour;
        float airPerCrewDay = Tuning.Crawler.AirPerPerson * 24 * Tuning.Crawler.AirRecyclingLossPerHour;
        float startMorale = Gaussians.NextSingle(10, 1);

        this[Commodity.Crew] = Commodity.Crew.Round(this[Commodity.Crew] + crew);
        this[Commodity.Rations] = Commodity.Rations.Round(this[Commodity.Rations] + rationsPerCrewDay * crew * supplyDays);
        this[Commodity.Water] = Commodity.Water.Round(this[Commodity.Water] + waterPerCrewDay * crew * supplyDays);
        this[Commodity.Air] = Commodity.Air.Round(this[Commodity.Air] + airPerCrewDay * crew * supplyDays);
        this[Commodity.Morale] = Commodity.Morale.Round(this[Commodity.Morale] + startMorale * crew);
    }
    public void AddCommodityInventory(ulong seed, Location Loc, float Wealth, Faction faction = Faction.Player) {
        // Get faction-specific commodity weights from Tuning
        var FactionWeights = Tuning.Crawler.CommodityWeights[faction];

        // Separate essentials and goods
        var essentialIndices = _commodities.Keys.Where(c => c.IsEssential()).ToList();
        var goodsIndices = _commodities.Keys.Where(c => !c.IsEssential()).ToList();

        // Handle essentials with old method
        var gaussian = new GaussianSampler(seed);
        var essentialWeights = FactionWeights
            .Pairs()
            .Where(kv => kv.Key.IsEssential())
            .Select(kv => kv.Value * gaussian.NextSingle(1, 0.075f))
            .ToArray()
            .Normalize();

        var essentialSpend = essentialWeights.Select(w => w * Wealth).ToArray();
        essentialIndices
            .Zip(essentialSpend)
            .Do(keyWeight => this[keyWeight.First] += QuantityBought(keyWeight.Second, keyWeight.First, Loc));

        // Handle goods with weighted choice based on sqrt of value AND availability
        var goodsWeights = goodsIndices
            .Select(c => {
                float factionWeight = FactionWeights[c];
                float availability = c.AvailabilityAt(Loc);
                float gaussianValue = gaussian.NextSingle(1, 0.075f);
                return (Item: c, Weight: factionWeight * availability * gaussianValue);
            })
            .Where(x => x.Weight > 0.01f) // Filter out effectively unavailable goods
            .ToList();

        // Choose N based on sqrt of value
        int N = Math.Max(1, (int)Math.Sqrt(Wealth) / 20);
        float spend = Wealth / N;

        // Weighted choice to select N goods
        var rng = new XorShift(seed);
        for (int i = 0; i < N; i++) {
            var commodity = goodsWeights.ChooseWeightedAt(rng.NextSingle())!;
            var commoditySpend = gaussian.NextSingle(spend, spend * 0.1f);
            commoditySpend = Math.Min(commoditySpend, Wealth);
            Wealth -= commoditySpend;
            this[commodity] += QuantityBought(commoditySpend, commodity, Loc);
        }
    }
    public void AddSegmentInventory(ulong seed, Location location, float Wealth, bool includeCore = true, EArray<SegmentKind, float>? baseWeights = null) {
        var rng = new XorShift(seed);
        EArray<SegmentKind, float> BaseWeights = baseWeights ?? [1, 1, 1, 1]; // Power, Traction, Weapons, Defense
        EArray<SegmentKind, float> Weights;

        float M = 1.5f;
        float H = 2.5f;

        switch (location.Terrain) {
        default:
        case TerrainType.Flat:
            Weights = [1, M, 1, H];
            break;
        case TerrainType.Rough:
            Weights = [M, 1, H, 1];
            break;
        case TerrainType.Broken:
            Weights = [H, 1, 1, M];
            break;
        case TerrainType.Shattered:
            Weights = [1, H, M, 1];
            break;
        }
        var gaussian = new GaussianSampler(seed);
        Weights = Weights
            //.ToArray()
            //.ToList()
            .Zip(BaseWeights)
            .Select(ab => ab.First * ab.Second * gaussian.NextSingle(1, 0.1f))
            .ToEArray<SegmentKind, float>();
        var totalWeight = Weights.Sum();
        var weightedWealth = Weights
            .Select(w => w / totalWeight * Wealth)
            .ToEArray<SegmentKind, float>();
        if (includeCore) {
            if (Weights[SegmentKind.Power] > 0) {
                _add(rng.ChooseRandom(SegmentEx.CorePowerDefs));
            }
            if (Weights[SegmentKind.Traction] > 0) {
                _add(rng.ChooseRandom(SegmentEx.CoreTractionDefs.Where(td => td.TerrainLimit <= location.Terrain)));
            }
            if (Weights[SegmentKind.Offense] > 0) {
                _add(rng.ChooseRandom(SegmentEx.WeaponDefs));
            }
            if (Weights[SegmentKind.Defense] > 0) {
                _add(rng.ChooseRandom(SegmentEx.CoreDefenseDefs));
            }
        }
        foreach (var segmentClass in Enum.GetValues<SegmentKind>()) {
            while (weightedWealth[segmentClass] > 0) {
                _add(rng.ChooseRandom(SegmentEx.SegmentClassLookup[segmentClass]));
            }
        }
        //////////////////////////////
        return;

        //////////////////////////////
        void _add(SegmentDef? def) {
            if (def != null) {
                weightedWealth[def.SegmentKind] -= def.Cost;
                Add(def.NewSegment(seed));
            }
        }
    }

    public Inventory WithOverdraft(Inventory od) {
        Overdraft = od;
        return this;
    }
};
