namespace Crawler.Economy;

/// <summary>
/// A snapshot of prices at a specific location and time.
/// </summary>
public record PriceSnapshot(
    Location Location,
    TimePoint Timestamp,
    EArray<Commodity, float> Prices,
    EArray<Commodity, float> Supply,
    EArray<Commodity, float> Demand
) {
    /// <summary>Create a snapshot from the current state of a location.</summary>
    public static PriceSnapshot FromLocation(Location location, TimePoint time) {
        var prices = new EArray<Commodity, float>();
        var supply = new EArray<Commodity, float>();
        var demand = new EArray<Commodity, float>();

        // Calculate prices based on location
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            prices[commodity] = commodity.MidAt(location);
            // Supply/demand would come from encounter inventory or settlement data
            // For now, use placeholder values based on encounter type
            supply[commodity] = EstimateSupply(location, commodity);
            demand[commodity] = EstimateDemand(location, commodity);
        }

        return new PriceSnapshot(location, time, prices, supply, demand);
    }

    static float EstimateSupply(Location location, Commodity commodity) {
        // Base supply on location type and population
        float baseSupply = location.Type switch {
            EncounterType.Settlement => location.Population * 0.1f,
            EncounterType.Resource => location.Population * 0.3f,
            EncounterType.Crossroads => location.Population * 0.05f,
            _ => 0
        };

        // Adjust by commodity category
        var category = commodity.Category();
        return category switch {
            CommodityCategory.Raw => baseSupply * (location.Type == EncounterType.Resource ? 2.0f : 0.5f),
            CommodityCategory.Essential => baseSupply * 1.0f,
            CommodityCategory.Refined => baseSupply * 0.7f,
            CommodityCategory.Industrial => baseSupply * 0.4f,
            _ => baseSupply * 0.3f
        };
    }

    static float EstimateDemand(Location location, Commodity commodity) {
        float baseDemand = location.Type switch {
            EncounterType.Settlement => location.Population * 0.15f,
            EncounterType.Resource => location.Population * 0.1f,
            EncounterType.Crossroads => location.Population * 0.02f,
            _ => 0
        };

        var category = commodity.Category();
        return category switch {
            CommodityCategory.Essential => baseDemand * 1.5f,
            CommodityCategory.Consumer => baseDemand * 1.2f,
            CommodityCategory.Refined => baseDemand * 0.8f,
            _ => baseDemand * 0.5f
        };
    }
}

/// <summary>
/// Stores known price information about various locations.
/// Knowledge can be shared between actors and relay towers.
/// </summary>
public class PriceKnowledge {
    readonly Dictionary<Location, PriceSnapshot> _snapshots = new();

    /// <summary>All known price snapshots.</summary>
    public IReadOnlyDictionary<Location, PriceSnapshot> Snapshots => _snapshots;

    /// <summary>Update price information for a location.</summary>
    public void UpdatePrice(Location location, TimePoint time) {
        var snapshot = PriceSnapshot.FromLocation(location, time);
        _snapshots[location] = snapshot;
    }

    /// <summary>Update with a specific snapshot.</summary>
    public void UpdatePrice(PriceSnapshot snapshot) {
        // Only update if newer than existing
        if (!_snapshots.TryGetValue(snapshot.Location, out var existing) ||
            snapshot.Timestamp > existing.Timestamp) {
            _snapshots[snapshot.Location] = snapshot;
        }
    }

    /// <summary>Get the price snapshot for a location, if known.</summary>
    public PriceSnapshot? GetSnapshot(Location location) {
        return _snapshots.TryGetValue(location, out var snapshot) ? snapshot : null;
    }

    /// <summary>Get the age of price information for a location.</summary>
    public TimeDuration? PriceAge(Location location, TimePoint currentTime) {
        if (_snapshots.TryGetValue(location, out var snapshot)) {
            return currentTime - snapshot.Timestamp;
        }
        return null;
    }

    /// <summary>Check if price information is stale (older than threshold).</summary>
    public bool IsStale(Location location, TimePoint currentTime, TimeDuration threshold) {
        var age = PriceAge(location, currentTime);
        return age == null || age > threshold;
    }

    /// <summary>
    /// Merge knowledge from another source, keeping newer information.
    /// </summary>
    public void MergeKnowledge(PriceKnowledge other) {
        foreach (var (location, snapshot) in other._snapshots) {
            UpdatePrice(snapshot);
        }
    }

    /// <summary>
    /// Find the best trade opportunity: buy at one location, sell at another.
    /// Returns (buyLocation, sellLocation, commodity, profitPerUnit)
    /// </summary>
    public (Location? buy, Location? sell, Commodity commodity, float profit) FindBestTrade(
        TimePoint currentTime,
        TimeDuration maxAge) {
        Location? bestBuy = null;
        Location? bestSell = null;
        Commodity bestCommodity = Commodity.Scrap;
        float bestProfit = 0;

        var freshSnapshots = _snapshots.Values
            .Where(s => currentTime - s.Timestamp <= maxAge)
            .ToList();

        foreach (var commodity in Enum.GetValues<Commodity>()) {
            // Skip essentials and non-tradeable commodities
            if (commodity.Category() == CommodityCategory.Essential) continue;
            if (commodity.Flags().HasFlag(CommodityFlag.Integral)) continue;

            foreach (var buySnapshot in freshSnapshots) {
                float buyPrice = buySnapshot.Prices[commodity];
                if (buyPrice <= 0 || buySnapshot.Supply[commodity] <= 0) continue;

                foreach (var sellSnapshot in freshSnapshots) {
                    if (sellSnapshot.Location == buySnapshot.Location) continue;

                    float sellPrice = sellSnapshot.Prices[commodity];
                    if (sellPrice <= 0 || sellSnapshot.Demand[commodity] <= 0) continue;

                    float profit = sellPrice - buyPrice;
                    if (profit > bestProfit) {
                        bestProfit = profit;
                        bestBuy = buySnapshot.Location;
                        bestSell = sellSnapshot.Location;
                        bestCommodity = commodity;
                    }
                }
            }
        }

        return (bestBuy, bestSell, bestCommodity, bestProfit);
    }

    /// <summary>
    /// Find trade opportunities sorted by profit margin.
    /// </summary>
    public IEnumerable<(Location buy, Location sell, Commodity commodity, float profit, float margin)>
        FindTradeOpportunities(TimePoint currentTime, TimeDuration maxAge, int limit = 10) {
        var opportunities = new List<(Location, Location, Commodity, float, float)>();

        var freshSnapshots = _snapshots.Values
            .Where(s => currentTime - s.Timestamp <= maxAge)
            .ToList();

        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commodity.Category() == CommodityCategory.Essential) continue;
            if (commodity.Flags().HasFlag(CommodityFlag.Integral)) continue;

            foreach (var buySnapshot in freshSnapshots) {
                float buyPrice = buySnapshot.Prices[commodity];
                if (buyPrice <= 0) continue;

                foreach (var sellSnapshot in freshSnapshots) {
                    if (sellSnapshot.Location == buySnapshot.Location) continue;

                    float sellPrice = sellSnapshot.Prices[commodity];
                    if (sellPrice <= 0) continue;

                    float profit = sellPrice - buyPrice;
                    float margin = profit / buyPrice;

                    if (margin > 0.1f) { // At least 10% margin
                        opportunities.Add((buySnapshot.Location, sellSnapshot.Location,
                            commodity, profit, margin));
                    }
                }
            }
        }

        return opportunities
            .OrderByDescending(o => o.Item5) // Sort by margin
            .Take(limit);
    }

    /// <summary>Clone the price knowledge.</summary>
    public PriceKnowledge Clone() {
        var clone = new PriceKnowledge();
        foreach (var (location, snapshot) in _snapshots) {
            clone._snapshots[location] = snapshot;
        }
        return clone;
    }
}
