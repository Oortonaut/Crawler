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

    /// <summary>
    /// Tick is now minimal - production is event-driven via ProductionCycleEvent.
    /// This only updates stall status for reporting purposes.
    /// </summary>
    public override void Tick() {
        if (Owner is not Crawler crawler) return;

        foreach (var segment in crawler.IndustrySegments) {
            if (!segment.IsActive) continue;
            if (segment.CurrentRecipe == null) {
                segment.IsStalled = false;
                continue;
            }

            // Update stall status based on current conditions
            var recipe = segment.CurrentRecipe;
            float batchSize = segment.BatchSize;

            // Check power
            if (crawler.PowerBalance < segment.Drain) {
                segment.IsStalled = true;
                continue;
            }

            // Check crew
            if (crawler.CrewInv < recipe.CrewRequired) {
                segment.IsStalled = true;
                continue;
            }

            segment.IsStalled = false;
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
