namespace Crawler.Economy;

/// <summary>
/// Tracks baseline stock levels for settlements and calculates price multipliers.
/// The actual stock is in Cargo - this just tracks what the "normal" level is
/// so prices can respond to supply/demand changes.
/// </summary>
public class SettlementStock {
    readonly EArray<Commodity, float> _baseline = new();
    TimePoint _lastPriceUpdate;

    /// <summary>Get the baseline stock level for a commodity.</summary>
    public float Baseline(Commodity c) => _baseline[c];

    /// <summary>Set the baseline stock level for a commodity.</summary>
    public void SetBaseline(Commodity c, float value) => _baseline[c] = Math.Max(0, value);

    /// <summary>
    /// Calculate price multiplier based on current stock vs baseline.
    /// Below baseline = higher prices (scarcity), above = lower prices (surplus).
    /// Range: 0.5x (double stock) to 2.0x (half stock)
    /// </summary>
    public float PriceMultiplier(Commodity c, float currentStock) {
        if (_baseline[c] <= 0) return 1.0f;
        float ratio = currentStock / _baseline[c];
        // Inverse ratio: low stock = high multiplier, high stock = low multiplier
        return 1.0f / Math.Clamp(ratio, 0.5f, 2.0f);
    }

    /// <summary>Time of last price recalculation.</summary>
    public TimePoint LastPriceUpdate => _lastPriceUpdate;

    /// <summary>Mark prices as recalculated at the given time.</summary>
    public void MarkPriceUpdate(TimePoint time) => _lastPriceUpdate = time;

    /// <summary>
    /// Initialize baseline from current cargo inventory.
    /// </summary>
    public static SettlementStock FromCargo(Inventory cargo) {
        var stock = new SettlementStock();
        foreach (var c in Enum.GetValues<Commodity>()) {
            stock._baseline[c] = cargo[c];
        }
        return stock;
    }

    /// <summary>
    /// Create stock with population-scaled baseline for essential commodities.
    /// Uses cargo amounts as minimums but scales up essentials for larger populations.
    /// </summary>
    public static SettlementStock ForPopulation(int population, Inventory cargo) {
        var stock = FromCargo(cargo);

        // Scale baseline for essentials based on population
        float popFactor = population / 100f;

        // Essentials should have higher baseline (settlements need reserves)
        stock._baseline[Commodity.Rations] = Math.Max(stock._baseline[Commodity.Rations], popFactor * 50f);
        stock._baseline[Commodity.Water] = Math.Max(stock._baseline[Commodity.Water], popFactor * 25f);
        stock._baseline[Commodity.Medicines] = Math.Max(stock._baseline[Commodity.Medicines], popFactor * 5f);

        // Industrial inputs
        stock._baseline[Commodity.Fuel] = Math.Max(stock._baseline[Commodity.Fuel], 100f + popFactor * 10f);
        stock._baseline[Commodity.SpareParts] = Math.Max(stock._baseline[Commodity.SpareParts], 50f + popFactor * 5f);

        return stock;
    }
}
