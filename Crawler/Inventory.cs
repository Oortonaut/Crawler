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

// Trade policy defines how a faction treats a commodity category
public enum TradePolicy {
    Subsidized,   // 0.7x base price - government subsidy or local production
    Legal,        // 1.0x base price - normal trade
    Taxed,        // 1.3x markup - import tariffs
    Restricted,   // 1.75x markup + transaction fee - heavily regulated
    Prohibited    // Cannot trade - illegal in this faction's territory
}

[Flags]
public enum CommodityFlag {
    None = 0,
    Perishable = 1 << 0, // Requires power/refrigeration
    Restricted = 1 << 1, // DEPRECATED - use faction policies instead
    Contraband = 1 << 2, // Only Bandits sell this (illegal everywhere else)
    Bulky = 1 << 3, // High volume relative to value
    Essential = 1 << 4, // Essential goods (use old distribution method)
    Integral = 1 << 5, // Display as integer (no decimal places)
}

public record struct CommodityData(float BaseValue, float Volume, float Mass, CommodityFlag Flag, GameTier Tier);
public static class CommodityEx {
    // Volume in cubic meters per unit, Weight in tons per unit, Flags, Tier
    public static EArray<Commodity, CommodityData> Data = [
        new CommodityData(1, 0.001f, 0.005f, CommodityFlag.Essential, GameTier.Early), // Scrap
        new CommodityData(7.5f, 0.0002f, 0.001f, CommodityFlag.Essential, GameTier.Early), // Fuel
        new CommodityData(4.0f, 1.0f, 0.08f, CommodityFlag.Essential | CommodityFlag.Integral, GameTier.Early), // Crew
        new CommodityData(15f, 0.0f, 0.0f, CommodityFlag.Essential | CommodityFlag.Integral, GameTier.Early), // Morale
        new CommodityData(3.5f, 1.0f, 0.08f, CommodityFlag.Integral, GameTier.Early), // Passengers
        new CommodityData(5.0f, 1.2f, 0.10f, CommodityFlag.Integral, GameTier.Mid), // Soldiers

        // Life support
        new CommodityData(1f, 0.1f, 0.087f, CommodityFlag.Essential, GameTier.Early), // Air
        new CommodityData(30f, 1.0f, 1.0f, CommodityFlag.Essential | CommodityFlag.Bulky, GameTier.Early), // Water
        new CommodityData(30f, 0.5f, 0.1f, CommodityFlag.Essential | CommodityFlag.Perishable, GameTier.Early), // Rations

        // Raw materials
        new CommodityData(40f, 1.0f, 0.5f, CommodityFlag.Perishable | CommodityFlag.Bulky, GameTier.Early), // Biomass
        new CommodityData(120f, 1.0f, 3.0f, CommodityFlag.Bulky, GameTier.Early), // Ore
        new CommodityData(83.33333f, 1.0f, 2.0f, CommodityFlag.Bulky, GameTier.Early), // Silicates
        
        // refined
        new CommodityData(30f, 0.1f, 0.25f, CommodityFlag.None, GameTier.Early), // Metal
        new CommodityData(18.75f, 0.1f, 0.075f, CommodityFlag.None, GameTier.Mid), // Chemicals
        new CommodityData(8.333333f, 0.1f, 0.065f, CommodityFlag.None, GameTier.Early), // Glass
        
        // Parts
        new CommodityData(225f, 1.0f, 0.75f, CommodityFlag.None, GameTier.Mid), // Ceramics
        new CommodityData(175f, 1.0f, 0.4f, CommodityFlag.None, GameTier.Mid), // Polymers
        new CommodityData(425f, 1.0f, 2.0f, CommodityFlag.None, GameTier.Mid), // Alloys
        new CommodityData(135f, 1.0f, 0.6f, CommodityFlag.None, GameTier.Late), // Electronics
        new CommodityData(430f, 1.0f, 0.7f, CommodityFlag.Restricted, GameTier.Mid), // Explosives

        // Consumer Goods
        new CommodityData(4f, 0.001f, 0.0005f, CommodityFlag.Perishable, GameTier.Mid), // Medicines
        new CommodityData(165f, 1.0f, 0.2f, CommodityFlag.None, GameTier.Early), // Textiles
        new CommodityData(20f, 0.001f, 0.002f, CommodityFlag.None, GameTier.Late), // Gems
        new CommodityData(150f, 1.0f, 0.4f, CommodityFlag.None, GameTier.Early), // Toys
        new CommodityData(550f, 1.0f, 0.75f, CommodityFlag.None, GameTier.Mid), // Machines
        new CommodityData(150.0f, 0.025f, 0.04f, CommodityFlag.Restricted | CommodityFlag.Integral, GameTier.Late), // AiCores
        new CommodityData(35f, 0.001f, 0.00033333334f, CommodityFlag.None, GameTier.Mid), // Media

        // Vice & Contraband
        new CommodityData(375f, 1.0f, 1.0f, CommodityFlag.Contraband | CommodityFlag.Bulky, GameTier.Early), // Liquor
        new CommodityData(12f, 0.001f, 0.0005f, CommodityFlag.Contraband, GameTier.Mid), // Stims
        new CommodityData(10f, 0.001f, 0.0005f, CommodityFlag.Contraband, GameTier.Mid), // Downers
        new CommodityData(36f, 0.001f, 0.0006f, CommodityFlag.Contraband, GameTier.Late), // Trips
        new CommodityData(5.625f, 0.01f, 0.0075f, CommodityFlag.Restricted, GameTier.Mid), // SmallArms

        // Religious items
        new CommodityData(6.0f, 0.003f, 0.004f, CommodityFlag.Integral, GameTier.Early), // Idols
        new CommodityData(4.0f, 0.005f, 0.002f, CommodityFlag.Integral, GameTier.Early), // Texts
        new CommodityData(15.0f, 0.002f, 0.003f, CommodityFlag.Integral, GameTier.Late), // Relics
    ];

    public static CommodityFlag Flags(this Commodity commodity) => Data[commodity].Flag;
    public static bool IsIllegalAtTrade(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Restricted | CommodityFlag.Contraband);
    public static bool IsPerishable(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Perishable);
    public static bool IsBulky(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Bulky);
    public static bool IsEssential(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Essential);
    public static bool IsIntegral(this Commodity commodity) => commodity.Flags().HasFlag(CommodityFlag.Integral);
    public static float BaseCost(this Commodity commodity) => Data[commodity].BaseValue;
    public static GameTier Tier(this Commodity commodity) => Data[commodity].Tier;
    public static float Mass(this Commodity commodity) => Data[commodity].Mass;
    public static float Volume(this Commodity commodity) => Data[commodity].Volume;
    public static float AvailabilityAt(this Commodity commodity, Location location) {
        float commodityTech =  (float)commodity.Tier() - 1;

        return CommodityAvlByPopTech.Value(location.Population / 100, location.TechLatitude + commodityTech);
    }
    public static float ScarcityPremium(this Commodity commodity, Location location) {
        float availability = commodity.AvailabilityAt(location);
        float scarcity = 1.0f - availability;

        // Essential goods have lower scarcity premium
        float weight = commodity.IsEssential() ?
            Tuning.Trade.scarcityEssential :
            Tuning.Trade.scarcityLuxury;

        return 1.0f + scarcity * Tuning.Trade.scarcityWeight * weight;
    }
    public static PowerScaling CommodityAvlByPopTech = new(0.15f, new(5.0f, 3.0f), "primitive", "tech");
    public static float Round(this Commodity commodity, float value) => ( float ) Math.Round(value, commodity.Flags().HasFlag(CommodityFlag.Integral) ? 0 : 1);
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
        set => _commodities[c] = c.Round(Math.Max(0, value));
    }
    public void Add(Commodity commodity, float addCount) {
        _commodities[commodity] = commodity.Round(_commodities[commodity] + addCount);
    }
    public void Remove(Commodity commodity, float removeCount) {
        _commodities[commodity] = commodity.Round(_commodities[commodity] - removeCount);
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
            .Do(ii => _commodities[ii.Key] = ii.Key.Round(_commodities[ii.Key] + ii.Value));
        Segments.AddRange(other.Segments);
    }
    public void Remove(Inventory other) {
        if (!Contains(other)) {
            throw new Exception("Inventory doesn't contain other.");
        }

        other._commodities
            .Pairs()
            .Do(ii => _commodities[ii.Key] = ii.Key.Round(_commodities[ii.Key] - ii.Value));
        foreach (var otherSegment in other.Segments) {
            Segments.Remove(otherSegment);
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
    public void AddRandomInventory(Location Loc, int crew, float supplyDays, float goodsWealth, float segmentWealth, bool includeCore = false, EArray<SegmentKind, float>? segmentClassWeights = null, Faction faction = Faction.Player) {
        AddCrewInventory(crew, supplyDays);
        AddCommodityInventory(Loc, goodsWealth, faction);
        AddSegmentInventory(Loc, segmentWealth, includeCore, segmentClassWeights);
    }
    public void AddCrewInventory(int crew, float supplyDays = 10) {
        float rationsPerCrewDay = Tuning.Crawler.RationsPerCrewDay;
        float waterPerCrewDay = Tuning.Crawler.WaterPerCrew * 24 * Tuning.Crawler.WaterRecyclingLossPerHour;
        float airPerCrewDay = Tuning.Crawler.AirPerPerson * 24 * Tuning.Crawler.AirRecyclingLossPerHour;
        float startMorale = ( float ) CrawlerEx.NextGaussian(10, 1);

        this[Commodity.Crew] = Commodity.Crew.Round(this[Commodity.Crew] + crew);
        this[Commodity.Rations] = Commodity.Rations.Round(this[Commodity.Rations] + rationsPerCrewDay * crew * supplyDays);
        this[Commodity.Water] = Commodity.Water.Round(this[Commodity.Water] + waterPerCrewDay * crew * supplyDays);
        this[Commodity.Air] = Commodity.Air.Round(this[Commodity.Air] + airPerCrewDay * crew * supplyDays);
        this[Commodity.Morale] = Commodity.Morale.Round(this[Commodity.Morale] + startMorale * crew);
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
                float availability = c.AvailabilityAt(Loc);
                float gaussian = (float)CrawlerEx.NextGaussian(1, 0.075f);
                return (Item: c, Weight: factionWeight * availability * gaussian);
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
