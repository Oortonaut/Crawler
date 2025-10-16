using System.Linq;

namespace Crawler;

public enum Commodity {
    Scrap, // Cash
    Fuel,
    Crew,
    Morale,
    Passengers, // not affected by morale
    Soldiers, //     ""

    // Life support
    Air,
    Water,
    Rations,

    // Raw materials
    Biomass,
    Ore,
    Silicates,
    // refined
    Metal,
    Chemicals,
    Glass,
    // Parts
    Ceramics,
    Polymers,
    Alloys,
    Electronics,
    Explosives,

    // Consumer Goods
    Medicines,
    Textiles,
    Gems,
    Toys,
    Machines,
    Computers,
    Media,

    // Vice & Contraband
    Liquor,
    Stims,
    Downers,
    Trips,
    SmallArms,

    // Religious items
    Idols,
    Texts,
    Relics,
}

public enum CommodityFlags {
    None = 0,
    Perishable = 1 << 0,      // Requires power/refrigeration
    Restricted = 1 << 1,       // Illegal at trade settlements
    Contraband = 1 << 2,       // Illegal everywhere except black market
    Bulky = 1 << 3,            // High volume relative to value
    Essential = 1 << 4,        // Essential goods (use old distribution method)
}

public record struct CommodityData(float Volume, float Mass, CommodityFlags Flags);

public static class CommodityEx {
    // Volume in cubic meters per unit, Weight in tons per unit, Flags
    public static EArray<Commodity, CommodityData> Data = [
        new CommodityData(0.001f, 0.005f, CommodityFlags.Essential),                                      // Scrap
        new CommodityData(0.01f,  0.008f, CommodityFlags.Essential),                                      // Fuel
        new CommodityData(1.0f,   0.08f,  CommodityFlags.Essential),                                      // Crew
        new CommodityData(0.0f,   0.0f,   CommodityFlags.Essential),                                      // Morale
        new CommodityData(1.0f,   0.08f,  CommodityFlags.Essential),                                      // Passengers
        new CommodityData(1.2f,   0.10f,  CommodityFlags.Essential),                                      // Soldiers

        // Life support
        new CommodityData(0.05f,  0.001f, CommodityFlags.Essential),                                      // Air
        new CommodityData(0.01f,  0.01f,  CommodityFlags.Essential | CommodityFlags.Bulky),               // Water
        new CommodityData(0.005f, 0.003f, CommodityFlags.Essential | CommodityFlags.Perishable),          // Rations

        // Raw materials
        new CommodityData(0.02f,  0.005f, CommodityFlags.Perishable | CommodityFlags.Bulky),              // Biomass
        new CommodityData(0.005f, 0.015f, CommodityFlags.Bulky),                                          // Ore
        new CommodityData(0.006f, 0.012f, CommodityFlags.Bulky),                                          // Silicates
        // refined
        new CommodityData(0.004f, 0.010f, CommodityFlags.None),                                           // Metal
        new CommodityData(0.008f, 0.006f, CommodityFlags.None),                                           // Chemicals
        new CommodityData(0.012f, 0.008f, CommodityFlags.None),                                           // Glass
        // Parts
        new CommodityData(0.008f, 0.006f, CommodityFlags.None),                                           // Ceramics
        new CommodityData(0.012f, 0.004f, CommodityFlags.None),                                           // Polymers
        new CommodityData(0.006f, 0.012f, CommodityFlags.None),                                           // Alloys
        new CommodityData(0.003f, 0.002f, CommodityFlags.None),                                           // Electronics
        new CommodityData(0.007f, 0.005f, CommodityFlags.Restricted),                                     // Explosives

        // Consumer Goods
        new CommodityData(0.002f, 0.001f, CommodityFlags.Perishable),                                     // Medicines
        new CommodityData(0.015f, 0.003f, CommodityFlags.None),                                           // Textiles
        new CommodityData(0.001f, 0.002f, CommodityFlags.None),                                           // Gems
        new CommodityData(0.010f, 0.004f, CommodityFlags.None),                                           // Toys
        new CommodityData(0.020f, 0.015f, CommodityFlags.None),                                           // Machines
        new CommodityData(0.004f, 0.003f, CommodityFlags.None),                                           // Computers
        new CommodityData(0.003f, 0.001f, CommodityFlags.None),                                           // Media

        // Vice & Contraband
        new CommodityData(0.008f, 0.008f,  CommodityFlags.Restricted),                                    // Liquor
        new CommodityData(0.001f, 0.0005f, CommodityFlags.Contraband),                                    // Stims
        new CommodityData(0.001f, 0.0005f, CommodityFlags.Contraband),                                    // Downers
        new CommodityData(0.0005f, 0.0003f, CommodityFlags.Contraband),                                   // Trips
        new CommodityData(0.008f, 0.006f, CommodityFlags.Restricted),                                     // SmallArms

        // Religious items
        new CommodityData(0.003f, 0.004f, CommodityFlags.None),                                           // Idols
        new CommodityData(0.005f, 0.002f, CommodityFlags.None),                                           // Texts
        new CommodityData(0.002f, 0.003f, CommodityFlags.None),                                           // Relics
    ];

    public static bool IsIllegalAtTrade(this Commodity commodity) {
        var flags = Data[commodity].Flags;
        return (flags & CommodityFlags.Restricted) != 0 || (flags & CommodityFlags.Contraband) != 0;
    }

    public static bool IsPerishable(this Commodity commodity) {
        return (Data[commodity].Flags & CommodityFlags.Perishable) != 0;
    }

    public static bool IsBulky(this Commodity commodity) {
        return (Data[commodity].Flags & CommodityFlags.Bulky) != 0;
    }

    public static bool IsEssential(this Commodity commodity) {
        return (Data[commodity].Flags & CommodityFlags.Essential) != 0;
    }

    public static float Volume(this Commodity commodity) {
        return Data[commodity].Volume;
    }
    public static float Mass(this Commodity commodity) {
        return Data[commodity].Mass;
    }
}

public class Inventory {
    public Inventory() {}
    public Inventory(EArray<Commodity, float> InItemCounts, IEnumerable<Segment> InSegments) {
        _commodities = InItemCounts.Clone();
        Segments = InSegments.ToList();
    }

    EArray<Commodity, float> _commodities = new();
    public IReadOnlyList<float> Commodities => _commodities.Items;
    public List<Segment> Segments { get; } = new();
    public bool IsEmpty => _commodities.All(c => c == 0) && Segments.Count == 0;
    public void Clear() {
        _commodities.Initialize(0);
        Segments.Clear();
    }

    public float this[Commodity c] {
        get => _commodities[c];
        set => _commodities[c] = Math.Max(0, value);
    }
    public void Add(Commodity commodity, float addCount) {
        _commodities[commodity] += addCount;
    }
    public void Remove(Commodity commodity, float removeCount) {
        _commodities[commodity] -= removeCount;
    }
    public void Add(Segment s) => Segments.Add(s);
    public void Remove(Segment s) => Segments.Remove(s);

    public Inventory Copy() {
        var commoditiesCopy = _commodities.Clone();
        var segmentsCopy = Segments.Select(s => s.SegmentDef.NewSegment()).ToList();
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
                        nameCount.Value > 1 ? $"{nameCount.Value} {nameCount.Key.Name}" :
                        $"{nameCount.Key.Name}")
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
            .Do(ii => this[ii.Key] += ii.Value);
        Segments.AddRange(other.Segments);
    }
    public void Remove(Inventory other) {
        if (!Contains(other)) {
            throw new Exception("Inventory doesn't contain other.");
        }

        other._commodities
            .Pairs()
            .Do(ii => this[ii.Key] -= ii.Value);
        foreach (var otherSegment in other.Segments) {
            Segments.Remove(otherSegment);
        }
    }

    public float Mass => Segments.Sum(s => s.Weight) + _commodities.Pairs().Sum(ii => ii.Key.Mass() * ii.Value);
    public float ItemValueAt(Location loc) => _commodities.Pairs().Sum(ii => ii.Key.Value(loc) * ii.Value);
    public float SegmentValueAt(Location loc) => Segments.Sum(s => s.Cost * Tuning.Economy.LocalMarkup(s.SegmentKind, loc));
    public float ValueAt(Location loc) => ItemValueAt(loc) + SegmentValueAt(loc);

    public static float QuantityBought(float spent, Commodity comm, Location location) {
        spent /= comm.Value(location);
        return spent;
    }
    public static float QuantitySold(float paid, Commodity comm, Location location) {
        paid /= comm.Value(location);
        return paid;
    }

    public bool Contains(Inventory other) {
        if (Commodities.Zip(other.Commodities).Any(ii => ii.First < ii.Second)) {
            return false;
        }
        var countBySegment = Segments
            .GroupBy(s => s.SegmentDef)
            .Select(g => (g.Key, g.Count()))
            .ToDictionary();
        foreach (var segment in other.Segments) {
            if (countBySegment.TryGetValue(segment.SegmentDef, out var count)) {
                countBySegment[segment.SegmentDef] = --count;
                if (count < 0) {
                    return false;
                }
            } else {
                return false;
            }
        }
        return true;
    }
    public void AddRandomInventory(Location Loc, float Wealth, float CrewSplit = 0.375f, float GoodsSplit = 0.375f, bool includeCore = false, EArray<SegmentKind, float>? segmentClassWeights = null, Faction faction = Faction.Player) {
        float SegmentSplit = 1.0f - CrewSplit - GoodsSplit;
        AddCrewInventory(Wealth * CrewSplit);
        AddCommodityInventory(Loc, Wealth * GoodsSplit, faction);
        AddSegmentInventory(Loc, Wealth * SegmentSplit, includeCore, segmentClassWeights);
    }
    public void AddCrewInventory(float Wealth) {
        float startRations = (float)CrawlerEx.NextGaussian(50, 5);
        float startMorale = (float)CrawlerEx.NextGaussian(10, 1);
        float perCrewValue = Tuning.Economy.BaseCommodityValues[Commodity.Crew] +
            startRations * Tuning.Economy.BaseCommodityValues[Commodity.Rations] +
            startMorale * Tuning.Economy.BaseCommodityValues[Commodity.Morale];
        int crewCount = (int)Math.Ceiling(Wealth / perCrewValue);
        this[Commodity.Rations] +=  startRations * crewCount;
        this[Commodity.Morale] += startMorale * crewCount;
        this[Commodity.Crew] += crewCount;
    }
    public void AddCommodityInventory(Location Loc, float Wealth, Faction faction = Faction.Player) {
        // Get faction-specific commodity weights from Tuning
        var FactionWeights = Tuning.Crawler.CommodityWeights[faction];

        // Separate essentials and goods
        var essentialIndices = _commodities.Keys.Where(c => c.IsEssential()).ToList();
        var goodsIndices = _commodities.Keys.Where(c => !c.IsEssential()).ToList();

        // Handle essentials with old method
        var essentialWeights = FactionWeights
            .Pairs()
            .Where(kv => kv.Key.IsEssential())
            .Select(kv => kv.Value * (float)CrawlerEx.NextGaussian(1, 0.075f))
            .ToArray()
            .Normalize();

        var essentialSpend = essentialWeights.Select(w => w * Wealth).ToArray();
        essentialIndices
            .Zip(essentialSpend)
            .Do(keyWeight => this[keyWeight.First] += QuantityBought(keyWeight.Second, keyWeight.First, Loc));

        // Handle goods with weighted choice based on sqrt of value
        var goodsWeights = goodsIndices
            .Select(c => (Item: c, Weight: FactionWeights[c] * CrawlerEx.NextGaussian(1, 0.075f)))
            .ToList();

        // Choose N based on sqrt of value
        int N = Math.Max(1, (int)Math.Sqrt(Wealth) / 20);
        float spend = Wealth / N;

        // Weighted choice to select N goods
        for (int i = 0; i < N; i++) {
            var commodity = goodsWeights.ChooseWeightedRandom()!;
            var commoditySpend = CrawlerEx.NextGaussian(spend, spend * 0.1f);
            commoditySpend = Math.Min(commoditySpend, Wealth);
            Wealth -= commoditySpend;
            this[commodity] += QuantityBought(commoditySpend, commodity, Loc);
        }
    }
    public void AddSegmentInventory(Location location, float Wealth, bool includeCore = true, EArray<SegmentKind, float>? baseWeights = null) {
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
        Weights = Weights
            //.ToArray()
            //.ToList()
            .Zip(BaseWeights)
            .Select(ab=> ab.First * ab.Second * (float)CrawlerEx.NextGaussian(1, 0.1f))
            .ToEArray<SegmentKind, float>();
        var totalWeight = Weights.Sum();
        var weightedWealth = Weights
            .Select(w => w / totalWeight * Wealth)
            .ToEArray<SegmentKind, float>();
        if (includeCore) {
            if (Weights[SegmentKind.Power] > 0) {
                _add(SegmentEx.CorePowerDefs.ChooseRandom());
            }
            if (Weights[SegmentKind.Traction] > 0) {
                _add(SegmentEx.CoreTractionDefs.Where(td => td.TerrainLimit <= location.Terrain).ChooseRandom());
            }
            if (Weights[SegmentKind.Offense] > 0) {
                _add(SegmentEx.WeaponDefs.ChooseRandom());
            }
            if (Weights[SegmentKind.Defense] > 0) {
                _add(SegmentEx.CoreDefenseDefs.ChooseRandom());
            }
        }
        foreach (var segmentClass in Enum.GetValues<SegmentKind>()) {
            while (weightedWealth[segmentClass] > 0) {
                _add(SegmentEx.SegmentClassLookup[segmentClass].ChooseRandom());
            }
        }
        //////////////////////////////
        return;
        //////////////////////////////
        void _add(SegmentDef? def) {
            if (def != null) {
                weightedWealth[def.SegmentKind] -= def.Cost;
                Add(def.NewSegment());
            }
        }
    }
};
