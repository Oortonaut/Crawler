namespace Crawler;

using Production;

/// <summary>
/// Component that manages industrial production on a crawler.
/// Processes recipes on Industry segments, consuming inputs and producing outputs.
/// Integrates with ResourceReservation to prevent double-booking inputs across segments.
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
        var reservation = crawler.ResourceReservation;
        float hours = (float)elapsed.TotalHours;
        float batchSize = segment.BatchSize;

        // Check if we have enough power generation for baseload
        float baseloadRequired = segment.Drain;
        if (crawler.PowerBalance < baseloadRequired) {
            segment.IsStalled = true;
            return; // Not enough power generation
        }

        // Check inputs using reservation-aware availability
        if (!HasInputsWithReservation(recipe, crawler, segment, batchSize)) {
            segment.IsStalled = true;
            return;
        }

        // Check consumables
        if (!recipe.HasConsumables(crawler.Supplies, batchSize)) {
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

        // Consume inputs proportionally to progress (scaled by batch size)
        recipe.ConsumeInputs(crawler.Supplies, progressThisTick, batchSize);

        // Update reservation to reflect consumed inputs
        reservation.UpdateCommitment(segment, progressThisTick);

        // Consume consumables proportionally (scaled by batch size)
        recipe.ConsumeConsumables(crawler.Supplies, progressThisTick, batchSize);

        // Try to consume maintenance - if available, no wear; if not, accumulate wear damage
        float maintenanceSatisfied = recipe.ConsumeMaintenance(crawler.Supplies, progressThisTick, batchSize);

        if (maintenanceSatisfied < 1.0f) {
            // Maintenance not satisfied - accumulate wear proportional to progress
            segment.WearAccumulator += recipe.Wear * progressThisTick;

            // Apply accumulated wear as integer hits
            while (segment.WearAccumulator >= 1.0f) {
                segment.Hits++;
                segment.WearAccumulator -= 1.0f;
            }
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

            // Release the completed cycle's reservation
            reservation.Release(segment);

            // Produce outputs with efficiency bonus (scaled by batch size)
            recipe.ProduceOutputs(crawler.Cargo, segment.Efficiency, batchSize);

            // Produce waste (scaled by batch size)
            recipe.ProduceWaste(crawler.Cargo, batchSize);

            // If continuing to next cycle, try to commit resources
            if (segment.ProductionProgress > 0) {
                if (!reservation.TryCommit(segment, recipe, crawler.Supplies, batchSize)) {
                    // Can't commit for next cycle - will stall on next tick
                    break;
                }
            }
        }

        // Ensure we have a commitment for ongoing production
        if (segment.ProductionProgress > 0 && !reservation.HasCommitment(segment)) {
            reservation.TryCommit(segment, recipe, crawler.Supplies, batchSize);
        }
    }

    /// <summary>
    /// Check if inputs are available, considering this segment's existing commitment.
    /// </summary>
    static bool HasInputsWithReservation(ProductionRecipe recipe, Crawler crawler, IndustrySegment segment, float batchSize) {
        var reservation = crawler.ResourceReservation;

        // If this segment already has a commitment, check against that
        if (reservation.HasCommitment(segment)) {
            // Segment has reserved its inputs, check if they're still in inventory
            foreach (var (commodity, _) in recipe.Inputs) {
                // Just check that we haven't gone negative (shouldn't happen normally)
                if (crawler.Supplies[commodity] < 0) return false;
            }
            return true;
        }

        // No commitment yet - check if we can start fresh
        return reservation.CanStart(recipe, crawler.Supplies, batchSize);
    }

    /// <summary>
    /// Get available recipes for a given industry segment.
    /// </summary>
    public static IEnumerable<ProductionRecipe> GetAvailableRecipes(IndustrySegment segment, GameTier maxTier) {
        return RecipeEx.RecipesFor(segment.IndustryType)
            .Where(r => r.TechTier <= maxTier);
    }

    /// <summary>
    /// Try to set a recipe for an industry segment, committing required resources.
    /// Returns true if the recipe was set successfully.
    /// </summary>
    public static bool TrySetRecipe(Crawler crawler, IndustrySegment segment, ProductionRecipe recipe) {
        if (recipe.RequiredIndustry != segment.IndustryType) {
            return false;
        }

        float batchSize = segment.BatchSize;

        // Release any existing commitment
        crawler.ResourceReservation.Release(segment);

        // Try to commit resources for the new recipe
        if (!crawler.ResourceReservation.TryCommit(segment, recipe, crawler.Supplies, batchSize)) {
            return false;
        }

        segment.CurrentRecipe = recipe;
        segment.ProductionProgress = 0;
        segment.IsStalled = false;
        return true;
    }

    /// <summary>
    /// Set the recipe for an industry segment (legacy method without reservation).
    /// Prefer TrySetRecipe when a Crawler is available.
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
    /// Stop production on an industry segment and release any resource commitments.
    /// </summary>
    public static void StopProduction(Crawler crawler, IndustrySegment segment) {
        crawler.ResourceReservation.Release(segment);
        segment.CurrentRecipe = null;
        segment.ProductionProgress = 0;
        segment.IsStalled = false;
    }

    /// <summary>
    /// Stop production on an industry segment (legacy method without reservation cleanup).
    /// Prefer the overload that takes a Crawler when available.
    /// </summary>
    public static void StopProduction(IndustrySegment segment) {
        segment.CurrentRecipe = null;
        segment.ProductionProgress = 0;
        segment.IsStalled = false;
    }
}
