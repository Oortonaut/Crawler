using System.Diagnostics;
using System.Linq;

namespace Crawler;


/// <summary>
/// What a stack of goods is set aside for. A stack can serve several purposes: fuel in a
/// Supplies|Trade stack can be burned or sold, fuel in a Trade stack can only be sold, and
/// fuel in a Cargo stack is held in reserve until asked for by name.
/// </summary>
[Flags]
public enum Account {
    None = 0,
    Supplies = 1 << 0, // Consumed by the owner: fuel, rations, recipe inputs, cash
    Trade = 1 << 1,    // Offered for sale and exposed to demands
    Cargo = 1 << 2,    // Held in reserve
    All = Supplies | Trade | Cargo,
}

public static class AccountEx {
    /// <summary>Every non-empty stack key, in index order.</summary>
    public static readonly Account[] Stacks = Enumerable.Range(1, (int)Account.All).Select(i => (Account)i).ToArray();

    // For each mask, the stacks that serve it in the order they are drawn down: stacks dedicated to
    // the mask first, multi-purpose stacks last, so spending Supplies eats a Supplies stack before
    // a Supplies|Trade one.
    static readonly Account[][] _drawOrder = Enumerable.Range(0, (int)Account.All + 1)
        .Select(mask => Stacks
            .Where(stack => ((int)stack & mask) != 0)
            .OrderBy(stack => int.PopCount((int)stack & ~mask))
            .ThenBy(stack => (int)stack)
            .ToArray())
        .ToArray();

    public static Account[] DrawOrder(this Account mask) => _drawOrder[(int)(mask & Account.All)];
    public static bool Serves(this Account stack, Account mask) => (stack & mask) != Account.None;
    public static string Label(this Account stack) => stack == Account.None ? "None" : stack.ToString().Replace(", ", "|");
}

/// <summary>
/// Commodities and packaged segments, split into stacks keyed by <see cref="Account"/>.
/// Reads and withdrawals take an account mask and see every stack serving it; deposits take
/// the exact stack to put goods in. The default mask is Supplies, so a bare inventory used as
/// a bag of goods (an offer, a recipe's inputs) keeps everything in its Supplies stack.
/// </summary>
public class Inventory {
    public Inventory() { }
    public Inventory(EArray<Commodity, float> InItemCounts, IEnumerable<Segment> InSegments) {
        _stacks[(int)Account.Supplies] = InItemCounts.Clone();
        Segments = InSegments.ToList();
    }
    public Inventory(params Segment[] InSegments) {
        Segments = InSegments.ToList();
    }

    readonly EArray<Commodity, float>[] _stacks = Enumerable.Range(0, (int)Account.All + 1)
        .Select(_ => new EArray<Commodity, float>())
        .ToArray();
    public List<Segment> Segments { get; } = new();
    public IEnumerable<Segment> SegmentsIn(Account from) => Segments.Where(s => s.Account.Serves(from));
    public bool IsEmpty => _stacks.All(stack => stack.All(c => c == 0)) && Segments.Count == 0;
    public void Clear() {
        foreach (var stack in _stacks) {
            stack.Initialize(0);
        }
        Segments.Clear();
        InvalidateCache();
    }

    /// <summary>
    /// Amount held in stacks serving <paramref name="from"/>. Raising it deposits the
    /// difference into the stack keyed exactly by <paramref name="from"/>; lowering it
    /// withdraws in <see cref="AccountEx.DrawOrder"/>.
    /// </summary>
    public float this[Commodity c, Account from = Account.Supplies] {
        get {
            float held = 0;
            foreach (var stack in from.DrawOrder()) {
                held += _stacks[(int)stack][c];
            }
            return held;
        }
        set {
            var current = this[c, from];
            var target = c.Round(Math.Max(0, value));
            if (target > current) {
                Add(c, target - current, from);
            } else if (target < current) {
                Remove(c, current - target, from);
            }
        }
    }
    /// <summary>Amount in the one stack keyed exactly by <paramref name="stack"/>.</summary>
    public float Stack(Commodity c, Account stack) => _stacks[(int)stack][c];
    /// <summary>Non-empty stacks as (stack, commodity, amount).</summary>
    public IEnumerable<(Account Stack, Commodity Commodity, float Amount)> Stacks =>
        AccountEx.Stacks
            .SelectMany(stack => _stacks[(int)stack].Pairs().Select(kv => (stack, kv.Key, kv.Value)))
            .Where(entry => entry.Value > 0);

    public void Add(Commodity commodity, float addCount, Account into = Account.Supplies) {
        Debug.Assert(into != Account.None, "Commodities must go into a stack");
        _stacks[(int)into][commodity] = commodity.Round(_stacks[(int)into][commodity] + addCount);
        InvalidateCache();
    }
    /// <summary>Withdraws from stacks serving <paramref name="from"/>; takes what there is if short.</summary>
    public void Remove(Commodity commodity, float removeCount, Account from = Account.Supplies) {
        foreach (var stack in from.DrawOrder()) {
            if (removeCount <= 0) {
                break;
            }
            var held = _stacks[(int)stack][commodity];
            var taken = Math.Min(held, removeCount);
            _stacks[(int)stack][commodity] = commodity.Round(held - taken);
            removeCount -= taken;
        }
        InvalidateCache();
    }
    /// <summary>Moves up to <paramref name="amount"/> between two exact stacks; returns the amount moved.</summary>
    public float Move(Commodity commodity, float amount, Account fromStack, Account intoStack) {
        amount = Math.Min(amount, Stack(commodity, fromStack));
        _stacks[(int)fromStack][commodity] = commodity.Round(_stacks[(int)fromStack][commodity] - amount);
        Add(commodity, amount, intoStack);
        return amount;
    }
    public void Add(Segment s, Account into = Account.Supplies) {
        // Segments in inventory storage should be packaged
        Debug.Assert(s.IsPackaged, "Segments added to Inventory should be packaged");
        s.Account = into;
        Segments.Add(s);
        InvalidateCache();
    }
    public void Remove(Segment s) {
        if (!Segments.Remove(s)) {
            throw new Exception("Segment not found in inventory.");
        }
        InvalidateCache();
    }

    public Inventory Clone() {
        var clone = new Inventory();
        for (int i = 0; i < _stacks.Length; i++) {
            clone._stacks[i] = _stacks[i].Clone();
        }
        clone.Segments.AddRange(Segments.Select(s => s.Clone()));
        return clone;
    }

    public override string ToString() => ToString(Account.All);
    public string ToString(Account from) {
        var result = Brief(from);

        var segmentCounts = SegmentsIn(from).GroupBy(s => s.SegmentDef).ToDictionary(g => g.Key, g => g.Count());
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
    public string Brief(Account from = Account.All) {
        if (IsEmpty) {
            return "nothing";
        }
        var result = Pairs(from)
                .Select(kv => kv.Item1.CommodityText(kv.Item2))
                .Where(s => !string.IsNullOrEmpty(s))
                .StringJoin(", ")
            ;
        return result;
    }
    /// <summary>Adds every stack and segment of <paramref name="other"/>, keeping their accounts.</summary>
    public void Add(Inventory other) {
        for (int i = 0; i < _stacks.Length; i++) {
            foreach (var (commodity, amount) in other._stacks[i].Pairs()) {
                _stacks[i][commodity] = commodity.Round(_stacks[i][commodity] + amount);
            }
        }
        Segments.AddRange(other.Segments);
        InvalidateCache();
    }
    /// <summary>Adds everything in <paramref name="other"/> to the one stack <paramref name="into"/>.</summary>
    public void Add(Inventory other, Account into) {
        foreach (var (commodity, amount) in other.Pairs(Account.All)) {
            if (amount > 0) {
                Add(commodity, amount, into);
            }
        }
        foreach (var segment in other.Segments) {
            segment.Account = into;
            Segments.Add(segment);
        }
        InvalidateCache();
    }
    /// <summary>Withdraws all of <paramref name="other"/> from stacks serving <paramref name="from"/>.</summary>
    public void Remove(Inventory other, Account from = Account.Supplies) {
        if (!Contains(other, from)) {
            throw new Exception("Inventory doesn't contain other.");
        }

        foreach (var (commodity, amount) in other.Pairs(Account.All)) {
            if (amount > 0) {
                Remove(commodity, amount, from);
            }
        }

        foreach (var otherSegment in other.Segments.ToList()) {
            Remove(otherSegment);
        }
    }

    // Cache invalidation
    bool _cacheDirty = true;
    float _cachedMass;
    float _cachedVolume;

    void InvalidateCache() => _cacheDirty = true;

    void EnsureCacheValid() {
        if (_cacheDirty) {
            var totals = Pairs(Account.All).ToList();
            _cachedMass = Segments.Sum(s => s.Weight) + totals.Sum(ii => ii.Item1.Mass() * ii.Item2);
            _cachedVolume = Segments.Sum(s => s.Length) + totals.Sum(ii => ii.Item1.Volume() * ii.Item2);
            _cacheDirty = false;
        }
    }

    public float Mass {
        get {
            EnsureCacheValid();
            return _cachedMass;
        }
    }

    public float Volume {
        get {
            EnsureCacheValid();
            return _cachedVolume;
        }
    }

    /// <summary>Maximum volume capacity. float.MaxValue means unlimited.</summary>
    public float MaxVolume { get; set; } = float.MaxValue;

    /// <summary>Available volume capacity remaining.</summary>
    public float AvailableVolume => MaxVolume - Volume;

    /// <summary>Check if a commodity amount can fit in remaining volume.</summary>
    public bool CanFit(Commodity commodity, float amount) =>
        commodity.Volume() * amount <= AvailableVolume;

    /// <summary>Check if a segment can fit in remaining volume.</summary>
    public bool CanFit(Segment segment) =>
        segment.Length <= AvailableVolume;

    /// <summary>Volume utilization as a fraction (0.0 to 1.0+).</summary>
    public float VolumeUtilization => MaxVolume < float.MaxValue ? Volume / MaxVolume : 0;

    public float ItemValueAt(Location loc, Account from = Account.All) => Pairs(from).Sum(ii => ii.Item1.MidAt(loc) * ii.Item2);
    public float SegmentValueAt(Location loc, Account from = Account.All) => SegmentsIn(from).Sum(s => s.CostAt(loc));
    public float ValueAt(Location loc, Account from = Account.All) => ItemValueAt(loc, from) + SegmentValueAt(loc, from);

    public static float QuantityBought(float spent, Commodity comm, Location location) {
        spent /= comm.MidAt(location);
        return comm.Round(spent);
    }
    public static float QuantitySold(float paid, Commodity comm, Location location) {
        paid /= comm.MidAt(location);
        return comm.Round(paid);
    }

    /// <summary>Whether stacks serving <paramref name="from"/> hold everything in <paramref name="other"/>.</summary>
    public bool Contains(Inventory other, Account from = Account.Supplies) {
        foreach (var (commodity, amount) in other.Pairs(Account.All)) {
            if (amount > this[commodity, from]) {
                return false;
            }
        }

        var countBySegment = SegmentsIn(from)
            .GroupBy(s => s.SegmentDef)
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var segment in other.Segments) {
            if (!countBySegment.TryGetValue(segment.SegmentDef, out var count) || count == 0) {
                return false;
            }
            countBySegment[segment.SegmentDef] = count - 1;
        }
        return true;
    }
    public bool Contains(Commodity commodity, float amt, Account from = Account.Supplies) =>
        this[commodity, from] >= commodity.Round(amt);
    public bool Contains(Segment segment, int n = 1, Account from = Account.Supplies) =>
        SegmentsIn(from).Count(s => s.SegmentDef == segment.SegmentDef) >= n;

    /// <summary>
    /// A bag holding the stacks serving <paramref name="from"/> (and not <paramref name="except"/>)
    /// under the same keys, sharing the segment objects so they can be removed by reference.
    /// </summary>
    public Inventory Subset(Account from, Account except = Account.None) {
        var subset = new Inventory();
        foreach (var stack in AccountEx.Stacks.Where(s => s.Serves(from) && !s.Serves(except))) {
            subset._stacks[(int)stack] = _stacks[(int)stack].Clone();
        }
        subset.Segments.AddRange(Segments.Where(s => s.Account.Serves(from) && !s.Account.Serves(except)));
        return subset;
    }
    /// <summary>Whether every stack holds at least the same stack of <paramref name="other"/>.</summary>
    public bool ContainsStacks(Inventory other) {
        foreach (var stack in AccountEx.Stacks) {
            foreach (var (commodity, amount) in other._stacks[(int)stack].Pairs()) {
                if (amount > _stacks[(int)stack][commodity]) {
                    return false;
                }
            }
        }
        return other.Segments.All(Segments.Contains);
    }
    /// <summary>Withdraws <paramref name="other"/> stack for stack; segments are removed by reference.</summary>
    public void RemoveStacks(Inventory other) {
        if (!ContainsStacks(other)) {
            throw new Exception("Inventory doesn't contain other.");
        }
        foreach (var stack in AccountEx.Stacks) {
            foreach (var (commodity, amount) in other._stacks[(int)stack].Pairs()) {
                _stacks[(int)stack][commodity] = commodity.Round(_stacks[(int)stack][commodity] - amount);
            }
        }
        foreach (var segment in other.Segments.ToList()) {
            Remove(segment);
        }
        InvalidateCache();
    }
    public static void RandomEssentials(ulong seed, Location location, int crew, float supplyDays = 10) => new Inventory().AddEssentials(seed, location, crew, supplyDays);
    public Inventory AddEssentials(ulong seed, Location location, int crew, float supplyDays = 10) {
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

        return this;
    }
    public static Inventory RandomTradeGoods(ulong seed, Location Loc, float Wealth, Factions faction = Factions.Player, Account into = Account.Supplies) => new Inventory().AddTradeGoods(seed, Loc, Wealth, faction, into);
    public Inventory AddTradeGoods(ulong seed, Location Loc, float Wealth, Factions faction = Factions.Player, Account into = Account.Supplies) {
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
            Add(c, QuantityBought(itemSpend, c, Loc), into);
            Wealth -= itemSpend;
        }
        return this;
    }
    public static List<Segment> GenerateCoreSegments(ulong seed, Location location, float Wealth, EArray<SegmentKind, float>? baseWeights = null, bool forSettlement = false) => GenerateSegments(seed, location, Wealth, true, baseWeights, forSettlement);
    public static List<Segment> GenerateTradeSegments(ulong seed, Location location, float Wealth, EArray<SegmentKind, float>? baseWeights = null) => GenerateSegments(seed, location, Wealth, false, baseWeights, false);
    static List<Segment> GenerateSegments(ulong seed, Location location, float Wealth, bool includeCore, EArray<SegmentKind, float>? baseWeights, bool forSettlement) {
        var segments = new List<Segment>();
        var rng = new XorShift(seed);
        // Power, Traction, Offense, Defense, Industry, Storage, Harvest, Habitat
        EArray<SegmentKind, float> BaseWeights = baseWeights ?? [1, 1, 1, 1, 0, 0, 0, 0];
        EArray<SegmentKind, float> Weights;

        float M = 1.5f;
        float H = 2.5f;

        // Terrain weights for combat segments (Power, Traction, Offense, Defense)
        // Industry, Storage, Harvest, Habitat use baseWeights directly (terrain doesn't affect them)
        switch (location.Terrain) {
        default:
        case TerrainType.Flat:
            Weights = [1, M, 1, H, 1, 1, 1, 1];
            break;
        case TerrainType.Rough:
            Weights = [M, 1, H, 1, 1, 1, 1, 1];
            break;
        case TerrainType.Broken:
            Weights = [H, 1, 1, M, 1, 1, 1, 1];
            break;
        case TerrainType.Shattered:
            Weights = [1, H, M, 1, 1, 1, 1, 1];
            break;
        }
        var gaussian = new GaussianSampler(seed);
        Weights = Weights
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
        return segments;

        //////////////////////////////
        void _add(SegmentDef? def) {
            if (def != null) {
                // For settlements, embiggen most segment kinds by 2-4 tiers
                // Power needs extra boost to support industry drain
                if (forSettlement) {
                    int boost = def.SegmentKind switch {
                        SegmentKind.Power => 4 + CrawlerEx.PoissonQuantile(0.5f, ref rng),    // 4-6 tiers (more power for industry)
                        SegmentKind.Industry => 2 + CrawlerEx.PoissonQuantile(0.5f, ref rng), // 2-4 tiers
                        SegmentKind.Defense => 2 + CrawlerEx.PoissonQuantile(0.5f, ref rng),  // 2-4 tiers
                        SegmentKind.Storage => 2 + CrawlerEx.PoissonQuantile(0.5f, ref rng),  // 2-4 tiers
                        SegmentKind.Offense => 1 + CrawlerEx.PoissonQuantile(0.5f, ref rng),  // 1-3 tiers
                        _ => 0 // Traction, Harvest, Habitat - no boost
                    };
                    if (boost > 0) {
                        int newSize = (int)def.Size.Size + boost;
                        def = def.Resize(Math.Clamp(newSize, 3, 9));
                    }
                }

                weightedWealth[def.SegmentKind] -= def.Cost;
                var segment = def.NewSegment(rng.Seed());
                // Core segments (working segments) should be unpackaged
                // Trade segments should remain packaged (default)
                if (includeCore) {
                    segment.Packaged = false;
                }
                segments.Add(segment);
            }
        }
    }

    public void AddSegments(IEnumerable<Segment> segments, Account into = Account.Supplies) {
        foreach (var segment in segments) {
            Add(segment, into);
        }
    }
    public void AddSegments(ulong seed, Location location, float Wealth, EArray<SegmentKind, float>? baseWeights = null, Account into = Account.Supplies) => AddSegments(GenerateTradeSegments(seed, location, Wealth, baseWeights), into);

    /// <summary>Per-commodity amounts summed over stacks serving <paramref name="from"/>.</summary>
    public IEnumerable<(Commodity, float)> Pairs(Account from) =>
        Enum.GetValues<Commodity>().Select(c => (c, this[c, from]));

    // Data structure for serialization
    public record class Data {
        public Dictionary<Account, Dictionary<Commodity, float>> Stacks { get; set; } = new();
        public List<Segment.Data> Segments { get; set; } = new();
    }

    public Data ToData() {
        return new Data {
            Stacks = AccountEx.Stacks
                .Where(stack => _stacks[(int)stack].Any<float>(c => c > 0))
                .ToDictionary(stack => stack, stack => _stacks[(int)stack].Pairs()
                    .Where(kv => kv.Value > 0)
                    .ToDictionary(kv => kv.Key, kv => kv.Value)),
            Segments = Segments.Select(s => s.ToData()).ToList()
        };
    }

    public void FromData(Data data) {
        Clear();
        foreach (var (stack, commodities) in data.Stacks) {
            foreach (var (commodity, amount) in commodities) {
                _stacks[(int)stack][commodity] = amount;
            }
        }
        foreach (var segmentData in data.Segments) {
            // Reconstruct segment from data
            if (SegmentEx.NameLookup.TryGetValue(segmentData.DefName, out var segmentDef)) {
                var segment = segmentDef.NewSegment(segmentData.Seed);
                segment.FromData(segmentData);
                Segments.Add(segment);
            }
        }
    }
};
