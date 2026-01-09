namespace Crawler.Economy;

/// <summary>
/// Per-commodity minimum stock levels for production decision-making.
/// Stock targets represent desired inventory levels that override pure profit calculation.
/// </summary>
public class StockTargets {
    readonly EArray<Commodity, float> _targets = new();

    /// <summary>Get or set the target stock level for a commodity.</summary>
    public float this[Commodity c] {
        get => _targets[c];
        set => _targets[c] = value;
    }

    /// <summary>Check if current inventory is below target for a commodity.</summary>
    public bool IsBelowTarget(Commodity c, float currentStock) =>
        currentStock < _targets[c];

    /// <summary>
    /// Get the deficit (target - current) for a commodity.
    /// Returns 0 if at or above target.
    /// </summary>
    public float Deficit(Commodity c, float currentStock) =>
        Math.Max(0, _targets[c] - currentStock);

    /// <summary>
    /// Generate default stock targets based on population.
    /// Scales essential supplies with population size.
    /// </summary>
    public static StockTargets ForPopulation(int population) {
        var targets = new StockTargets();
        float popFactor = population / 100f;

        // Essential supplies - scale with population
        targets[Commodity.Rations] = popFactor * 10f;
        targets[Commodity.Water] = popFactor * 5f;
        targets[Commodity.Air] = popFactor * 2f;
        targets[Commodity.Medicines] = popFactor * 0.5f;

        // Industrial inputs - base amounts plus population scaling
        targets[Commodity.Fuel] = 50f + popFactor * 5f;
        targets[Commodity.SpareParts] = 20f + popFactor * 2f;
        targets[Commodity.Lubricants] = 10f + popFactor * 1f;
        targets[Commodity.Coolant] = 10f + popFactor * 1f;

        return targets;
    }

    /// <summary>
    /// Generate stock targets for a mobile crawler (smaller buffers).
    /// </summary>
    public static StockTargets ForCrawler(int crew) {
        var targets = new StockTargets();

        // Crew essentials - small buffer
        targets[Commodity.Rations] = crew * 2f;
        targets[Commodity.Water] = crew * 1f;
        targets[Commodity.Air] = crew * 0.5f;

        // Operating supplies
        targets[Commodity.Fuel] = 20f;
        targets[Commodity.SpareParts] = 5f;

        return targets;
    }

    /// <summary>Default minimal targets (no automatic stocking).</summary>
    public static StockTargets Default() => new();
}
