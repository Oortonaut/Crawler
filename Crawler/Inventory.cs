using System.Linq;

namespace Crawler;


public enum FromInventory {
    None,      // Cannot fulfill (even with overdraft)
    Primary,       // Can fulfill from current inventory
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
        if (containsResult == FromInventory.None) {
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

    public FromInventory Contains(Inventory other) {
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
            return FromInventory.Primary;
        }

        // If no overdraft available, return False
        if (Overdraft == null) {
            return FromInventory.None;
        }

        // Recursively check if overdraft can fulfill the shortfall
        var shortfallInventory = new Inventory(shortfallCommodities, shortfallSegments);
        var overdraftResult = Overdraft.Contains(shortfallInventory);

        // If overdraft can fulfill (either directly or via its own overdraft chain)
        if (overdraftResult != FromInventory.None) {
            return FromInventory.Overdraft;
        }

        return FromInventory.None;
    }
    public FromInventory Contains(Commodity commodity, float amt) {
        amt = commodity.Round(amt);
        var held = _commodities[commodity];
        if (held >= amt) {
            return FromInventory.Primary;
        } else if (Overdraft == null) {
            return FromInventory.None;
        } else {
            return Overdraft.Contains(commodity, amt - held) == FromInventory.None ? FromInventory.None : FromInventory.Overdraft;
        }
    }
    public FromInventory Contains(Segment segment, int n = 1) {
        var here = Segments.Count(s => s.SegmentDef == segment.SegmentDef);
        if (here <= n) {
            return FromInventory.Primary;
        } else if (Overdraft == null) {
            return FromInventory.None;
        } else {
            n -= here;
            return Overdraft.Contains(segment, n) == FromInventory.None ? FromInventory.None : FromInventory.Overdraft;
        }
    }
    public void AddRandomInventory(ulong seed, Location Loc, int crew, float supplyDays, float goodsWealth, float segmentWealth, bool includeCore = false, EArray<SegmentKind, float>? segmentClassWeights = null, Factions faction = Factions.Player) {
        var Rng = new XorShift(seed);
        var crewSeed = Rng.Seed();
        var goodsSeed = Rng.Seed();
        var segmentSeed = Rng.Seed();
        AddSegments(segmentSeed, Loc, segmentWealth, includeCore, segmentClassWeights);
        AddEssentials(crewSeed, Loc, crew, supplyDays);
        AddCargo(goodsSeed, Loc, goodsWealth, faction);
    }
    public void AddEssentials(ulong seed, Location location, int crew, float supplyDays = 10) {
        var Gaussians = new GaussianSampler(seed);
        float supplyHours = supplyDays * 24;
        float supplyCrewHours = supplyHours * crew;
        float crewSupplyDays = supplyDays * crew;

        float rations = Tuning.Crawler.RationsPerCrewDay * crewSupplyDays;
        float water = Tuning.Crawler.WaterPerCrew * crewSupplyDays;
        float waterLeakage = Tuning.Crawler.WaterRecyclingLossPerHour * supplyHours;
        float air = Tuning.Crawler.AirPerPerson * crew;
        float airLeakage = Tuning.Crawler.AirLeakagePerDamagedSegment * supplyHours; // one segment leaking
        float startMorale = Gaussians.NextSingle(10, 1);
        var appxDraw = Segments.Sum(seg => seg.Drain);
        float fuelPerHr = Tuning.Crawler.StandbyFraction * appxDraw;
        float hoursOfFuel = supplyDays * 24;
        float initialFuel = fuelPerHr * hoursOfFuel;
        float mileageFuel = 250 * supplyDays * Tuning.Crawler.FuelPerKm / 0.4f;
        float wealth = location.Wealth * 0.1f;

        this[Commodity.Scrap] += wealth;
        this[Commodity.Crew] += crew;
        this[Commodity.Rations] += rations;
        this[Commodity.Water] += water + waterLeakage;
        this[Commodity.Air] += air + airLeakage;
        this[Commodity.Morale] += startMorale * crew;
        this[Commodity.Fuel] += initialFuel + mileageFuel;
    }
    public void AddCargo(ulong seed, Location Loc, float Wealth, Factions faction = Factions.Player) {
        var rng = new XorShift(seed);
        var gaussian = new GaussianSampler(rng.Seed());
        // Get faction-specific commodity weights from Tuning
        var FactionWeights = Tuning.Crawler.CommodityWeights[faction];

        float expectation = ( float ) Math.Pow(Wealth, 0.6f) * 0.15f;
        int numGoods = CrawlerEx.PoissonQuantileAt(expectation, rng.NextSingle());
        var goodsIndices = FactionWeights.Pairs()
            .Where(pair =>
                pair.Key.AvailabilityAt(Loc) > 0 &&
                pair.Value > 0 &&
                pair.Key.Category() is not CommodityCategory.Essential)
            .ChooseWeightedRandomK(numGoods, rng.Branch());
        numGoods = goodsIndices.Count;
        float spend = Wealth / numGoods;

        foreach (var c  in goodsIndices) {
            float scale = gaussian.NextSingle(1, 0.125f);
            float itemSpend = Math.Max(0, spend * scale);
            this[c] += QuantityBought(itemSpend, c, Loc);
            Wealth -= itemSpend;
        }
    }
    public void AddSegments(ulong seed, Location location, float Wealth, bool includeCore = true, EArray<SegmentKind, float>? baseWeights = null) {
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

    public Inventory SetOverdraft(Inventory od) {
        Overdraft = od;
        return this;
    }
    public IEnumerable<(Commodity, float)> Pairs => _commodities.Pairs();
};
