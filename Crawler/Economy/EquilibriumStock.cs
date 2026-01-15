namespace Crawler.Economy;

using Production;

/// <summary>
/// Calculates equilibrium stock levels for settlements based on
/// consumption rates and production capacity.
/// </summary>
public static class EquilibriumStock {
    /// <summary>
    /// Buffer days by commodity category.
    /// Determines how many days of consumption to stock.
    /// </summary>
    static readonly EArray<CommodityCategory, float> BufferDays = [
        7f,   // Essential - critical survival items
        2f,   // Raw - harvesters resupply frequently
        3f,   // Refined - intermediate goods
        3f,   // Parts - intermediate goods
        3f,   // Consumer - moderate buffer
        3f,   // Luxury
        2f,   // Vice
        5f,   // Dangerous - weapons/explosives
        2f,   // Religious
        5f,   // Industrial - maintenance supplies
        7f,   // Ammunition - combat reserves
        0f,   // Waste - no buffer needed
    ];

    /// <summary>
    /// Calculate hourly consumption rates for all commodities based on population.
    /// </summary>
    public static EArray<Commodity, float> ConsumptionRates(int population) {
        var rates = new EArray<Commodity, float>();

        // Essential consumption (from PopulationConsumptionComponent)
        rates[Commodity.Rations] = population * Tuning.Settlement.RationsPerPopPerHour;
        rates[Commodity.Water] = population * Tuning.Settlement.WaterPerPopPerHour;
        rates[Commodity.Medicines] = population * Tuning.Settlement.MedicinesPerPopPerHour;

        // Consumer goods (distributed across categories)
        float goodsRate = population * Tuning.Settlement.GoodsPerPopPerHour;
        rates[Commodity.Textiles] = goodsRate * 0.25f;
        rates[Commodity.Toys] = goodsRate * 0.25f;
        rates[Commodity.Media] = goodsRate * 0.25f;
        rates[Commodity.Liquor] = goodsRate * 0.25f;

        return rates;
    }

    /// <summary>
    /// Calculate theoretical hourly production capacity for all commodities
    /// based on available industry segments.
    /// Assumes one recipe per segment type, running at full capacity.
    /// </summary>
    public static EArray<Commodity, float> ProductionCapacity(IEnumerable<IndustrySegment> segments, Location location) {
        var capacity = new EArray<Commodity, float>();

        foreach (var segment in segments.Where(s => s.IsActive || !s.Packaged)) {
            // Get all recipes this segment could run
            var recipes = RecipeEx.RecipesFor(segment.IndustryType);

            // Find highest-value recipe for capacity estimation
            var bestRecipe = recipes
                .OrderByDescending(r => r.Outputs.Sum(kv => kv.Key.MidAt(location) * kv.Value))
                .FirstOrDefault();

            if (bestRecipe == null) continue;

            // Cycles per hour = throughput / cycle_time_hours
            float cyclesPerHour = segment.Throughput / (float)bestRecipe.CycleTime.TotalHours;
            float batchSize = segment.BatchSize;

            foreach (var (output, amount) in bestRecipe.Outputs) {
                capacity[output] += amount * cyclesPerHour * segment.Efficiency * batchSize;
            }
        }

        return capacity;
    }

    /// <summary>
    /// Calculate equilibrium stock levels for a settlement.
    /// </summary>
    public static EArray<Commodity, float> Calculate(
        int population,
        IEnumerable<IndustrySegment> industrySegments,
        Location location) {

        var consumption = ConsumptionRates(population);
        var production = ProductionCapacity(industrySegments, location);
        var equilibrium = new EArray<Commodity, float>();

        // Hours per day in game time (10 hours per day)
        const float hoursPerDay = 10f;

        foreach (var commodity in Enum.GetValues<Commodity>()) {
            float hourlyConsumption = consumption[commodity];
            float hourlyProduction = production[commodity];

            // Get buffer days for this category
            float bufferDays = BufferDays[commodity.Category()];
            float bufferHours = bufferDays * hoursPerDay;

            if (hourlyConsumption <= 0 && hourlyProduction <= 0) {
                // Not consumed or produced - skip
                continue;
            }

            if (hourlyConsumption > 0) {
                // Consumed commodity: stock = consumption buffer
                equilibrium[commodity] = hourlyConsumption * bufferHours;

                // If production doesn't cover consumption, add trade margin
                if (hourlyProduction < hourlyConsumption) {
                    float deficit = hourlyConsumption - hourlyProduction;
                    equilibrium[commodity] += deficit * bufferHours * 0.5f;
                }
            } else if (hourlyProduction > 0) {
                // Produced but not consumed locally - trade goods
                // Stock a smaller buffer for export
                equilibrium[commodity] = hourlyProduction * bufferHours * 0.5f;
            }
        }

        return equilibrium;
    }

    /// <summary>
    /// Initialize a settlement's cargo with equilibrium stock levels.
    /// </summary>
    public static void InitializeCargo(Crawler settlement) {
        var equilibrium = Calculate(
            settlement.Location.Population,
            settlement.IndustrySegments,
            settlement.Location);

        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commodity == Commodity.Scrap) {
                // Scrap (cash) scales with population and wealth
                float scrap = settlement.Location.Wealth *
                    (1 + settlement.Location.Population * 0.01f);
                settlement.Cargo.Add(Commodity.Scrap, scrap);
            } else if (commodity == Commodity.Crew || commodity == Commodity.Morale) {
                // Crew/Morale handled separately
                continue;
            } else if (equilibrium[commodity] > 0) {
                settlement.Cargo.Add(commodity, equilibrium[commodity]);
            }
        }
    }

    /// <summary>
    /// Calculate equilibrium stock baselines for SettlementStock.
    /// </summary>
    public static EArray<Commodity, float> CalculateBaselines(
        int population,
        IEnumerable<IndustrySegment> industrySegments,
        Location location) {

        // Baselines are the equilibrium targets - price multiplier = 1.0
        // when current stock equals baseline
        return Calculate(population, industrySegments, location);
    }
}
