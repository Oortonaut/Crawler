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
    AiCores,
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

public enum GameTier {
    Early,
    Mid,
    Late
}
[Flags]
public enum CommodityFlags {
    None = 0,
    Perishable = 1 << 0, // Requires power/refrigeration
    Restricted = 1 << 1, // Illegal at trade settlements
    Contraband = 1 << 2, // Illegal everywhere except black market
    Bulky = 1 << 3, // High volume relative to value
    Essential = 1 << 4, // Essential goods (use old distribution method)
    Integral = 1 << 5, // Display as integer (no decimal places)
}
public enum Scarcity {
    Common,
    Uncommon,
    Rare,
}

public record struct CommodityData(float BaseValue, float Volume, float Mass, CommodityFlags Flags, GameTier Tier);
public static class CommodityEx {
    // Volume in cubic meters per unit, Weight in tons per unit, Flags, Tier
    public static EArray<Commodity, CommodityData> Data = [
        new CommodityData(1, 0.001f, 0.005f, CommodityFlags.Essential, GameTier.Early), // Scrap
        new CommodityData(125f, 1.0f, 0.8f, CommodityFlags.Essential, GameTier.Early), // Fuel
        new CommodityData(4.0f, 1.0f, 0.08f, CommodityFlags.Essential | CommodityFlags.Integral, GameTier.Early), // Crew
        new CommodityData(15f, 0.0f, 0.0f, CommodityFlags.Essential | CommodityFlags.Integral, GameTier.Early), // Morale
        new CommodityData(3.5f, 1.0f, 0.08f, CommodityFlags.Integral, GameTier.Early), // Passengers
        new CommodityData(5.0f, 1.2f, 0.10f, CommodityFlags.Integral, GameTier.Mid), // Soldiers

        // Life support
        new CommodityData(10f, 1.0f, 0.02f, CommodityFlags.Essential, GameTier.Early), // Air
        new CommodityData(30f, 1.0f, 1.0f, CommodityFlags.Essential | CommodityFlags.Bulky, GameTier.Early), // Water
        new CommodityData(300f, 1.0f, 0.6f, CommodityFlags.Essential | CommodityFlags.Perishable, GameTier.Early), // Rations

        // Raw materials
        new CommodityData(40f, 1.0f, 0.25f, CommodityFlags.Perishable | CommodityFlags.Bulky, GameTier.Early), // Biomass
        new CommodityData(120f, 1.0f, 3.0f, CommodityFlags.Bulky, GameTier.Early), // Ore
        new CommodityData(83.33333f, 1.0f, 2.0f, CommodityFlags.Bulky, GameTier.Early), // Silicates
        
        // refined
        new CommodityData(300f, 1.0f, 2.5f, CommodityFlags.None, GameTier.Early), // Metal
        new CommodityData(187.5f, 1.0f, 0.75f, CommodityFlags.None, GameTier.Mid), // Chemicals
        new CommodityData(83.33333f, 1.0f, 0.6666667f, CommodityFlags.None, GameTier.Early), // Glass
        
        // Parts
        new CommodityData(225f, 1.0f, 0.75f, CommodityFlags.None, GameTier.Mid), // Ceramics
        new CommodityData(166.66667f, 1.0f, 0.33333334f, CommodityFlags.None, GameTier.Mid), // Polymers
        new CommodityData(416.66666f, 1.0f, 2.0f, CommodityFlags.None, GameTier.Mid), // Alloys
        new CommodityData(1333.3334f, 1.0f, 0.6666667f, CommodityFlags.None, GameTier.Late), // Electronics
        new CommodityData(428.57144f, 1.0f, 0.71428573f, CommodityFlags.Restricted, GameTier.Mid), // Explosives

        // Consumer Goods
        new CommodityData(4000f, 1.0f, 0.5f, CommodityFlags.Perishable, GameTier.Mid), // Medicines
        new CommodityData(166.66667f, 1.0f, 0.2f, CommodityFlags.None, GameTier.Early), // Textiles
        new CommodityData(20000f, 1.0f, 2.0f, CommodityFlags.None, GameTier.Late), // Gems
        new CommodityData(150f, 1.0f, 0.4f, CommodityFlags.None, GameTier.Early), // Toys
        new CommodityData(250f, 1.0f, 0.75f, CommodityFlags.None, GameTier.Mid), // Machines
        new CommodityData(25.0f, 0.004f, 0.015f, CommodityFlags.Restricted | CommodityFlags.Integral, GameTier.Late), // AiCores
        new CommodityData(666.6667f, 1.0f, 0.33333334f, CommodityFlags.None, GameTier.Mid), // Media

        // Vice & Contraband
        new CommodityData(375f, 1.0f, 1.0f, CommodityFlags.Contraband | CommodityFlags.Bulky, GameTier.Early), // Liquor
        new CommodityData(12000f, 1.0f, 0.5f, CommodityFlags.Contraband, GameTier.Mid), // Stims
        new CommodityData(10000f, 1.0f, 0.5f, CommodityFlags.Contraband, GameTier.Mid), // Downers
        new CommodityData(36000f, 1.0f, 0.6f, CommodityFlags.Contraband, GameTier.Late), // Trips
        new CommodityData(562.5f, 1.0f, 0.75f, CommodityFlags.Restricted, GameTier.Mid), // SmallArms

        // Religious items
        new CommodityData(6.0f, 0.003f, 0.004f, CommodityFlags.Integral, GameTier.Early), // Idols
        new CommodityData(4.0f, 0.005f, 0.002f, CommodityFlags.Integral, GameTier.Early), // Texts
        new CommodityData(15.0f, 0.002f, 0.003f, CommodityFlags.Integral, GameTier.Late), // Relics
    ];

    public static CommodityFlags Flags(this Commodity commodity) => Data[commodity].Flags;
    public static bool IsIllegalAtTrade(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlags.Restricted | CommodityFlags.Contraband);
    public static bool IsPerishable(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlags.Perishable);
    public static bool IsBulky(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlags.Bulky);
    public static bool IsEssential(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlags.Essential);
    public static bool IsIntegral(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlags.Integral);
    public static float BaseValue(this Commodity commodity) => Data[commodity].BaseValue;
    public static GameTier Tier(this Commodity commodity) => Data[commodity].Tier;
    public static float Mass(this Commodity commodity) => Data[commodity].Mass;
    public static float Volume(this Commodity commodity) => Data[commodity].Volume;
    // Calculate availability weight for a commodity based on settlement characteristics
    // population: 0-1 scale representing settlement size
    // techLevel: 0-1 scale representing settlement technology/quality
    public static float AvailabilityWeight(this Commodity commodity, float population, float techLevel) {
        var tier = commodity.Tier();
        
        // Base weight curve: larger settlements stock more variety
        float sizeWeight = 0.2f + 0.8f * population;
        
        // Tech level affects what tiers are available
        float tierWeight = tier switch {
            GameTier.Early => 1.0f - 0.3f * techLevel, // Early goods become less common in high-tech areas
            GameTier.Mid => (float)Math.Pow(Math.Sin(techLevel * Math.PI), 0.5), // Peak availability at mid-tech
            GameTier.Late => (float)Math.Pow(techLevel, 1.5), // Late goods require high tech
            _ => 1.0f
        };
        
        return sizeWeight * tierWeight;
    }

}
public class Inventory {
    public Inventory() { }
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
        float startRations = ( float ) CrawlerEx.NextGaussian(50, 5);
        float startMorale = ( float ) CrawlerEx.NextGaussian(10, 1);
        float perCrewValue = Commodity.Crew.BaseValue() +
                             startRations * Commodity.Rations.BaseValue() +
                             startMorale * Commodity.Morale.BaseValue();
        int crewCount = ( int ) Math.Ceiling(Wealth / perCrewValue);
        this[Commodity.Rations] += startRations * crewCount;
        this[Commodity.Morale] += startMorale * crewCount;
        this[Commodity.Crew] += crewCount;
    }
    public void AddCommodityInventory(Location Loc, float Wealth, Faction faction = Faction.Player) {
        // Get faction-specific commodity weights from Tuning
        var FactionWeights = Tuning.Crawler.CommodityWeights[faction];

        float population = Loc.Population;
        float techLevel = Loc.TechLevel;

        // Separate essentials and goods
        var essentialIndices = _commodities.Keys.Where(c => c.IsEssential()).ToList();
        var goodsIndices = _commodities.Keys.Where(c => !c.IsEssential()).ToList();

        // Handle essentials with old method
        var essentialWeights = FactionWeights
            .Pairs()
            .Where(kv => kv.Key.IsEssential())
            .Select(kv => kv.Value * ( float ) CrawlerEx.NextGaussian(1, 0.075f))
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
                float availWeight = c.AvailabilityWeight(population, techLevel);
                float gaussian = (float)CrawlerEx.NextGaussian(1, 0.075f);
                return (Item: c, Weight: factionWeight * availWeight * gaussian);
            })
            .Where(x => x.Weight > 0.01f) // Filter out effectively unavailable goods
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
            .Select(ab => ab.First * ab.Second * ( float ) CrawlerEx.NextGaussian(1, 0.1f))
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
