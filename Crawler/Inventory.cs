using System.Diagnostics;

namespace Crawler;

public enum Commodity {
    Scrap,
    Fuel,
    Ration,
    Crew,
    Morale,
    _Count
}

public class Inventory {
    public Inventory() {}
    public Inventory(int[] InItemCounts, IEnumerable<Segment> InSegments) {
        Debug.Assert(InItemCounts.Length == ( int ) Commodity._Count);
        ItemCounts = InItemCounts.ToArray();
        Segments = InSegments.ToList();
    }

    public Inventory(IEnumerable<Segment> InSegments) {
        Segments.AddRange(InSegments);
    }
    public Inventory(Segment InSegment) {
        Segments.AddRange(InSegment);
    }
    public Inventory(Commodity InCommodity, int InCount = 1) {
        ItemCounts[(int)InCommodity] = InCount;
    }
    public static Inventory Empty { get; } = new();
    public int[] ItemCounts { get; } = new int[( int ) Commodity._Count];
    public List<Segment> Segments { get; } = new();

    public int this[Commodity c] {
        get => ItemCounts[(int)c];
        set => ItemCounts[(int)c] = value;
    }
    public void Add(Commodity commodity, int addCount) {
        ItemCounts[( int ) commodity] += addCount;
    }
    public void Add(Segment s) => Segments.Add(s);
    public override string ToString() {
        var result = Brief();

        Dictionary<string, int> segmentCounts = Segments.GroupBy(s => s.Name.Substring(2)).ToDictionary(g => g.Key, g => g.Count());
        if (segmentCounts.Count > 0) {
            if (result.Length > 0) {
                result += "; ";
            }
            result += string.Join(", ",
                segmentCounts
                    .Select(nameCount =>
                        nameCount.Value > 1 ? $"{nameCount.Value} {nameCount.Key}" :
                        nameCount.Key)
                    .Where(s => !string.IsNullOrEmpty(s)));
        }
        return result;
    }
    public string Brief() {
        var result = string.Join(", ", ItemCounts
            .Select((count, index) => (( Commodity ) index).CommodityText(count))
            .Where(s => !string.IsNullOrEmpty(s))
        );
        return result;
    }
    public void Add(Inventory other) {
        ItemCounts.Zip(other.ItemCounts, (a, b) => a + b).ToArray().CopyTo(ItemCounts, 0);
        Segments.AddRange(other.Segments);
    }
    public void Remove(Inventory other) {
        if (!Contains(other)) {
            throw new Exception("Inventory doesn't contain other.");
        }

        ItemCounts.Zip(other.ItemCounts, (a, b) => a - b).ToArray().CopyTo(ItemCounts, 0);
        foreach (var otherSegment in other.Segments) {
            for (int i = 0; i < Segments.Count; ++i) {
                //if (Segments[i].Name == otherSegment.Name) {
                if (Segments[i] == otherSegment) {
                    Segments.RemoveAt(i);
                    break;
                }
            }
        }
    }
    public double ItemValue => ItemCounts.Zip(BaseCommodityValues, (a, b) => a * b).Sum();
    public double SegmentValue => Segments.Sum(s => s.Cost);
    public double Value => ItemValue + SegmentValue;
    public static int QuantityBought(Commodity comm, double spent) {
        return ( int ) Math.Floor(spent / BaseCommodityValues[( int ) comm]);
    }

    public bool Contains(Inventory other) {
        for (int i = 0; i < ItemCounts.Length; i++) {
            if (ItemCounts[i] < other.ItemCounts[i]) {
                return false;
            }
        }
        var countBySegment = Segments.GroupBy(s => s.Name).ToDictionary(g => g.Key, g => g.Count());
        foreach (var segment in other.Segments) {
            if (countBySegment.TryGetValue(segment.Name, out var count)) {
                countBySegment[segment.Name] = --count;
                if (count < 0) {
                    return false;
                }
            } else {
                return false;
            }
        }
        return true;
    }
    public void AddRandomInventory(Location Loc, double Wealth, double CommoditySplit = 0.75, bool includeCore = false, double[]? segmentClassWeights = null) {
        AddCrewInventory(Wealth * CommoditySplit * 0.5);
        AddCommodityInventory(Loc, Wealth * CommoditySplit * 0.5);
        AddSegmentInventory(Loc, Wealth * (1 - CommoditySplit), includeCore, segmentClassWeights);
    }
    public void AddCrewInventory(double Wealth) {
        double startRations = CrawlerEx.NextGaussian(50, 5);
        double startMorale = CrawlerEx.NextGaussian(10, 1);
        double perCrewValue = BaseCommodityValues[(int)Commodity.Crew] +
            startRations * BaseCommodityValues[(int)Commodity.Ration] +
            startMorale * BaseCommodityValues[(int)Commodity.Morale];
        int crewCount = ( int ) Math.Ceiling(Wealth / perCrewValue);
        Add(Commodity.Ration,  (int)(startRations * crewCount));
        Add(Commodity.Morale, (int)(startMorale * crewCount));
        Add(Commodity.Crew, crewCount);
    }
    public void AddCommodityInventory(Location Loc, double Wealth) {
        // Weights of value to spawn by commodity type
        // scrap, fuel, food, crew, morale
        double[] BaseWeights = [5, 3, 1, 1, 1];
        double[] CommodityWeights = [1, 1, 1, 1, 1]; // SFRCM
        double H = 1.5;
        double L = 0.8;
        switch (Loc.Terrain) {
        case TerrainType.Flat:
            CommodityWeights = [1, L, H, 1, 1]; // [ S, F, R, C, M]
            break;
        case TerrainType.Rough:
            CommodityWeights = [1, 1, 1, L, H]; // [ S, F, R, C, M]
            break;
        case TerrainType.Broken:
            CommodityWeights = [1, 1, L, H, 1]; // [ S, F, R, C, M]
            break;
        case TerrainType.Shattered:
            CommodityWeights = [1, H, 1, 1, L]; // [ S, F, R, C, M]
            break;
        }
        // Weight
        var FinalWeights = CommodityWeights
            .Select(w => w * CrawlerEx.NextGaussian(1, 0.075))
            .Zip(BaseWeights, (a, b) => a * b)
            .Normalize()
            .ToArray();
        ItemCounts.Select((curr, index) =>
                curr + QuantityBought(( Commodity ) index, Wealth * FinalWeights[index]))
            .ToArray()
            .CopyTo(ItemCounts, 0);
    }
    public void AddSegmentInventory(Location L, double Wealth, bool includeCore = true, double[]? segmentClassWeightsByTerrain = null) {
        double[] BaseWeights = segmentClassWeightsByTerrain ??
            [1, 1, 1, 1]; // Power, Traction, Weapons, Defense
        double[] Weights;

        switch (L.Terrain) {
        default:
        case TerrainType.Flat:
            Weights = [1, 1.5, 1, 2.5];
            break;
        case TerrainType.Rough:
            Weights = [1.5, 1, 2.5, 1];
            break;
        case TerrainType.Broken:
            Weights = [2.5, 1, 1, 1.5];
            break;
        case TerrainType.Shattered:
            Weights = [1, 2.5, 1.5, 1];
            break;
        }
        Weights = Weights.Zip(BaseWeights, (a, b) => a * b).ToArray();
        Weights = Weights.Select(w => w * CrawlerEx.NextGaussian(1, 0.1)).ToArray();
        var totalWeight = Weights.Sum();
        Weights = Weights.Select(w => w / totalWeight).ToArray();
        var WeightedWealth = Weights.Select(w => w * Wealth).ToArray();
        bool corePower = false, coreTraction = false, coreOffense = false, coreDefense = false;
        foreach (var segmentClass in Enum.GetValues<SegmentClass>().Where(sc => (byte)sc < 128)) {
            double classLimit = WeightedWealth[(int)segmentClass];
            char segmentCode = default;
            
            while (classLimit >= 0) {
                string segmentCodes = string.Empty;
                switch (segmentClass) {
                case SegmentClass.Power:
                    if (includeCore && !corePower) {
                        corePower = true;
                        segmentCodes = Segment.CorePowerSegmentCodes();
                    } else {
                        segmentCodes = Segment.AllPowerSegmentCodes();
                    }
                    break;
                case SegmentClass.Traction:
                    if (includeCore && !coreTraction) {
                        coreTraction = true;
                        segmentCodes = Segment.CoreTractionSegmentCodes();
                    } else {
                        segmentCodes = Segment.AllTractionSegmentCodes();
                    }
                    break;
                case SegmentClass.Offense:
                    if (includeCore && !coreOffense) {
                        coreOffense = true;
                        segmentCodes = Segment.CoreWeaponSegmentCodes();
                    } else {
                        segmentCodes = Segment.AllWeaponSegmentCodes();
                    }
                    break;
                case SegmentClass.Defense:
                    if (includeCore && !coreDefense) {
                        coreDefense = true;
                        segmentCodes = Segment.CoreDefenseSegmentCodes();
                    } else {
                        segmentCodes = Segment.AllDefenseSegmentCodes();
                    }
                    break;
                }
                segmentCode = segmentCodes.ChooseRandom();
                if (segmentCode != default) {
                    var newSegment = Segment.Lookup[segmentCode];
                    Add(newSegment);
                    classLimit -= newSegment.Cost;
                }
            }
        }
    }

    public static double[] BaseCommodityValues = [
        1, // Scrap,
        6, // Fuel,
        1.5, // Rations,
        40, // Crew,
        15, // Morale,
    ];
};
