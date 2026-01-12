namespace Crawler.Economy;

using Production;

/// <summary>
/// Static production chain graph built from recipes.
/// Used for downstream demand propagation in settlement production AI.
/// </summary>
public static class ProductionChain {
    /// <summary>Maps output commodity -> recipes that produce it.</summary>
    public static Dictionary<Commodity, List<ProductionRecipe>> ProducedBy { get; }

    /// <summary>Maps input commodity -> recipes that consume it.</summary>
    public static Dictionary<Commodity, List<ProductionRecipe>> ConsumedBy { get; }

    /// <summary>
    /// Production tier for each commodity.
    /// Tier 0: Raw materials (from ResourceActor)
    /// Tier 1: Refined (Refinery output)
    /// Tier 2: Parts (Fabricator output)
    /// Tier 3: Consumer/Ammo (Assembler output)
    /// Tier -1: Not in production chain (essentials, waste, etc.)
    /// </summary>
    public static EArray<Commodity, int> Tier { get; }

    static ProductionChain() {
        ProducedBy = [];
        ConsumedBy = [];
        Tier = new();

        // Initialize all commodities to tier -1 (not in chain)
        foreach (var c in Enum.GetValues<Commodity>()) {
            Tier[c] = -1;
        }

        // Build graph from recipes
        foreach (var recipe in RecipeEx.AllRecipes) {
            // Map outputs
            foreach (var (output, _) in recipe.Outputs) {
                if (!ProducedBy.TryGetValue(output, out var producers)) {
                    producers = [];
                    ProducedBy[output] = producers;
                }
                producers.Add(recipe);

                // Assign tier based on industry type
                int tier = recipe.RequiredIndustry switch {
                    IndustryType.Refinery => 1,
                    IndustryType.Fabricator => 2,
                    IndustryType.Assembler => 3,
                    IndustryType.Recycler => 0, // Recycler produces raw
                    _ => -1
                };
                if (tier > Tier[output]) {
                    Tier[output] = tier;
                }
            }

            // Map inputs
            foreach (var (input, _) in recipe.Inputs) {
                if (!ConsumedBy.TryGetValue(input, out var consumers)) {
                    consumers = [];
                    ConsumedBy[input] = consumers;
                }
                consumers.Add(recipe);
            }
        }

        // Assign tier 0 to raw materials (inputs to tier 1 recipes, not produced)
        Commodity[] rawMaterials = [
            Commodity.Ore,
            Commodity.Biomass,
            Commodity.Silicates,
            Commodity.Isotopes,
            Commodity.Gems
        ];
        foreach (var raw in rawMaterials) {
            Tier[raw] = 0;
        }
    }

    /// <summary>
    /// Calculate downstream demand for all commodities based on current deficits.
    /// Propagates demand backwards through the production chain.
    /// </summary>
    /// <param name="getDeficit">Function returning deficit ratio (0-1) for a commodity.</param>
    /// <param name="decayFactor">Decay per tier (default 0.5).</param>
    /// <returns>Demand score (0-1) for each commodity.</returns>
    public static EArray<Commodity, float> PropagateDownstreamDemand(
        Func<Commodity, float> getDeficit,
        float decayFactor = 0.5f) {

        var demand = new EArray<Commodity, float>();

        // Initialize with direct deficits
        foreach (var c in Enum.GetValues<Commodity>()) {
            demand[c] = getDeficit(c);
        }

        // Propagate backwards: Tier 3 -> 2 -> 1 -> 0
        for (int tier = 3; tier >= 0; tier--) {
            foreach (var c in Enum.GetValues<Commodity>()) {
                if (Tier[c] != tier) continue;
                if (demand[c] <= 0) continue;

                // Find recipes that produce this commodity
                if (!ProducedBy.TryGetValue(c, out var recipes)) continue;

                // Propagate demand to inputs
                foreach (var recipe in recipes) {
                    foreach (var (input, _) in recipe.Inputs) {
                        // Add decayed demand to input
                        float propagated = demand[c] * decayFactor;
                        demand[input] = Math.Max(demand[input], propagated);
                    }
                }
            }
        }

        return demand;
    }

    /// <summary>
    /// Get the downstream demand score for a specific recipe's outputs.
    /// </summary>
    public static float GetRecipeDownstreamDemand(
        ProductionRecipe recipe,
        EArray<Commodity, float> demandScores) {

        float totalDemand = 0;
        float totalWeight = 0;

        foreach (var (output, amount) in recipe.Outputs) {
            float weight = output.BaseCost() * amount;
            totalDemand += demandScores[output] * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? totalDemand / totalWeight : 0;
    }
}
