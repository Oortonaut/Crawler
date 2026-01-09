namespace Crawler;

using Production;

/// <summary>
/// Component that manages industrial production on a crawler.
/// Processes recipes on Industry segments, consuming inputs and producing outputs.
/// </summary>
public class IndustryComponent : ActorComponentBase {
    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }

    public override void Tick() {
        if (Owner is not Crawler crawler) return;

        foreach (var segment in crawler.IndustrySegments) {
            if (!segment.IsActive) continue;
            if (segment.CurrentRecipe == null) continue;

            ProcessProduction(crawler, segment, Owner.Elapsed);
        }
    }

    /// <summary>
    /// Process production for a single industry segment.
    /// </summary>
    void ProcessProduction(Crawler crawler, IndustrySegment segment, TimeDuration elapsed) {
        var recipe = segment.CurrentRecipe!;
        float hours = (float)elapsed.TotalHours;

        // Check if we have enough power generation for baseload
        float baseloadRequired = segment.Drain;
        if (crawler.PowerBalance < baseloadRequired) {
            segment.IsStalled = true;
            return; // Not enough power generation
        }

        // Check inputs
        if (!recipe.HasInputs(crawler.Supplies)) {
            segment.IsStalled = true;
            return;
        }

        // Check consumables
        if (!recipe.HasConsumables(crawler.Supplies)) {
            segment.IsStalled = true;
            return;
        }

        // Check crew availability
        if (crawler.CrewInv < recipe.CrewRequired) {
            segment.IsStalled = true;
            return;
        }

        segment.IsStalled = false;

        // Calculate production rate
        // Progress per hour = throughput / cycle time in hours
        float progressPerHour = segment.Throughput / (float)recipe.CycleTime.TotalHours;
        float progressThisTick = progressPerHour * hours;

        // Consume inputs proportionally to progress
        recipe.ConsumeInputs(crawler.Supplies, progressThisTick);

        // Consume consumables proportionally
        recipe.ConsumeConsumables(crawler.Supplies, progressThisTick);

        // Apply maintenance (returns fraction satisfied)
        float maintenanceSatisfied = recipe.ConsumeMaintenance(crawler.Supplies, progressThisTick);

        // If maintenance not fully satisfied, apply wear to segment
        if (maintenanceSatisfied < 1.0f) {
            // TODO: Apply wear damage to segment based on missing maintenance
            // For now, just note that maintenance was partially satisfied
        }

        // Add progress
        segment.ProductionProgress += progressThisTick;

        // Complete production cycles
        while (segment.ProductionProgress >= 1.0f) {
            // Check if we have enough reactor charge for this cycle
            float chargeRequired = segment.ActivateCharge + recipe.ActivateCharge;
            if (!crawler.ConsumeBurstPower(chargeRequired)) {
                // Not enough charge - stall until charge accumulates
                segment.ProductionProgress = 1.0f; // Cap at 1.0 until we can complete
                segment.IsStalled = true;
                return;
            }

            segment.ProductionProgress -= 1.0f;

            // Produce outputs with efficiency bonus
            recipe.ProduceOutputs(crawler.Cargo, segment.Efficiency);

            // Produce waste
            recipe.ProduceWaste(crawler.Cargo);
        }
    }

    /// <summary>
    /// Get available recipes for a given industry segment.
    /// </summary>
    public static IEnumerable<ProductionRecipe> GetAvailableRecipes(IndustrySegment segment, GameTier maxTier) {
        return RecipeEx.RecipesFor(segment.IndustryType)
            .Where(r => r.TechTier <= maxTier);
    }

    /// <summary>
    /// Set the recipe for an industry segment.
    /// </summary>
    public static void SetRecipe(IndustrySegment segment, ProductionRecipe? recipe) {
        if (recipe != null && recipe.RequiredIndustry != segment.IndustryType) {
            throw new ArgumentException($"Recipe requires {recipe.RequiredIndustry} but segment is {segment.IndustryType}");
        }
        segment.CurrentRecipe = recipe;
        segment.ProductionProgress = 0;
        segment.IsStalled = false;
    }

    /// <summary>
    /// Stop production on an industry segment.
    /// </summary>
    public static void StopProduction(IndustrySegment segment) {
        segment.CurrentRecipe = null;
        segment.ProductionProgress = 0;
        segment.IsStalled = false;
    }
}
