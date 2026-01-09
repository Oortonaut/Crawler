namespace Crawler;

using Economy;
using Production;

/// <summary>
/// AI component that decides which recipes to run on industry segments.
/// Evaluates profitability and stock targets to choose optimal production.
/// </summary>
public class ProductionAIComponent : ActorComponentBase {
    readonly XorShift _rng;
    TimePoint _lastEvaluationTime;

    /// <summary>Minimum interval between production evaluations.</summary>
    static readonly TimeDuration EvaluationInterval = TimeDuration.FromHours(1);

    public ProductionAIComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 250; // Above trade roles (200), below combat

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;
        if (crawler.IsDepowered) return null;

        // Check if we have industry segments
        var segments = crawler.IndustrySegments.ToList();
        if (segments.Count == 0) return null;

        // Throttle evaluation frequency
        var time = crawler.Time;
        if (_lastEvaluationTime.IsValid && time - _lastEvaluationTime < EvaluationInterval) {
            return null;
        }

        // Check for idle segments
        var idleSegments = segments.Where(s => s.IsActive && s.CurrentRecipe == null).ToList();
        var stalledSegments = segments.Where(s => s.IsActive && s.IsStalled).ToList();

        if (idleSegments.Count == 0 && stalledSegments.Count == 0) {
            return null; // All segments are busy and running
        }

        // Evaluate and assign recipes
        if (EvaluateAndAssignRecipes(crawler, idleSegments.Concat(stalledSegments).ToList())) {
            _lastEvaluationTime = time;
        }

        return null; // No event needed - production runs via IndustryComponent.Tick()
    }

    /// <summary>
    /// Evaluate all possible recipes for idle segments and assign the best ones.
    /// </summary>
    bool EvaluateAndAssignRecipes(Crawler crawler, List<IndustrySegment> candidates) {
        var evaluations = new List<RecipeEvaluation>();
        var stockTargets = crawler.StockTargets ?? StockTargets.Default();
        var reservation = crawler.ResourceReservation;
        var location = crawler.Location;

        // Get max tech tier (could come from game state or crawler)
        var maxTier = GameTier.Late; // TODO: Get from Game.Instance or crawler property

        foreach (var segment in candidates) {
            // Skip segments that already have a recipe and aren't stalled
            if (segment.CurrentRecipe != null && !segment.IsStalled) continue;

            var availableRecipes = IndustryComponent.GetAvailableRecipes(segment, maxTier);

            foreach (var recipe in availableRecipes) {
                var eval = EvaluateRecipe(recipe, segment, crawler, stockTargets, reservation, location);
                if (eval != null) {
                    evaluations.Add(eval);
                }
            }
        }

        if (evaluations.Count == 0) return false;

        // Sort by total score (highest first)
        evaluations = evaluations.OrderByDescending(e => e.TotalScore).ToList();

        // Greedy assignment - best scores first
        var assignedSegments = new HashSet<IndustrySegment>();
        bool anyAssigned = false;

        foreach (var eval in evaluations) {
            if (assignedSegments.Contains(eval.Segment)) continue;

            // Check if we can still start this recipe (resources may have been committed)
            if (!reservation.CanStart(eval.Recipe, crawler.Supplies)) continue;

            // Try to assign the recipe
            if (IndustryComponent.TrySetRecipe(crawler, eval.Segment, eval.Recipe)) {
                assignedSegments.Add(eval.Segment);
                anyAssigned = true;
            }
        }

        return anyAssigned;
    }

    /// <summary>
    /// Evaluate a single recipe on a segment.
    /// </summary>
    RecipeEvaluation? EvaluateRecipe(
        ProductionRecipe recipe,
        IndustrySegment segment,
        Crawler crawler,
        StockTargets stockTargets,
        ResourceReservation reservation,
        Location location) {

        // Check basic requirements
        if (!reservation.CanStart(recipe, crawler.Supplies)) return null;
        if (!recipe.HasConsumables(crawler.Supplies)) return null;
        if (crawler.PowerBalance < segment.Drain) return null;
        if (crawler.CrewInv < recipe.CrewRequired) return null;

        // Calculate costs (using base costs - could use local prices if PriceKnowledge available)
        float inputCost = recipe.Inputs.Sum(kv => kv.Key.CostAt(location) * kv.Value);
        float consumableCost = recipe.Consumables.Sum(kv => kv.Key.CostAt(location) * kv.Value);
        float maintenanceCost = recipe.Maintenance.Sum(kv => kv.Key.CostAt(location) * kv.Value);
        float chargeCost = (segment.ActivateCharge + recipe.ActivateCharge) * Tuning.Production.ChargeValue;

        float totalCost = inputCost + consumableCost + maintenanceCost + chargeCost;

        // Calculate output value
        float outputValue = recipe.Outputs.Sum(kv => kv.Key.CostAt(location) * kv.Value * segment.Efficiency);

        // Calculate profit
        float profitPerCycle = outputValue - totalCost;
        float cycleHours = (float)recipe.CycleTime.TotalHours / segment.Throughput;
        float profitPerHour = profitPerCycle / cycleHours;

        // Calculate stock priority bonus
        float stockBonus = 0;
        foreach (var (commodity, amount) in recipe.Outputs) {
            float currentStock = crawler.Supplies[commodity] + crawler.Cargo[commodity];
            if (stockTargets.IsBelowTarget(commodity, currentStock)) {
                float deficit = stockTargets.Deficit(commodity, currentStock);
                // Weight by commodity value and deficit severity
                stockBonus += deficit * commodity.BaseCost() * Tuning.Production.StockDeficitWeight;
            }
        }

        float totalScore = profitPerHour + stockBonus;

        return new RecipeEvaluation(recipe, segment, profitPerCycle, profitPerHour, stockBonus, totalScore);
    }

    /// <summary>Result of evaluating a recipe for a segment.</summary>
    record RecipeEvaluation(
        ProductionRecipe Recipe,
        IndustrySegment Segment,
        float ProfitPerCycle,
        float ProfitPerHour,
        float StockPriority,
        float TotalScore
    );
}
