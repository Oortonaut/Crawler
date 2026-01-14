namespace Crawler;

using Economy;
using Production;

/// <summary>
/// AI component that decides which recipes to run on industry segments.
/// For settlements: Uses fuzzy priorities based on deficit urgency, input availability,
/// downstream demand, and profit margin.
/// For mobile crawlers: Uses profit-first scoring with stock targets.
/// </summary>
public class ProductionAIComponent : ActorComponentBase {
    readonly XorShift _rng;
    TimePoint _lastEvaluationTime;

    // Cached downstream demand for settlements (refreshed each evaluation)
    EArray<Commodity, float> _downstreamDemand;
    bool _hasDownstreamDemand;

    /// <summary>Minimum interval between production evaluations.</summary>
    static readonly TimeDuration EvaluationInterval = TimeDuration.FromHours(1);

    public ProductionAIComponent(ulong seed) {
        _rng = new XorShift(seed);
        _downstreamDemand = new();
    }

    public override int Priority => 250; // Above trade roles (200), below combat

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    /// <summary>Whether this crawler is a settlement (uses demand-driven scoring).</summary>
    bool IsSettlement(Crawler crawler) => crawler.Role == Roles.Settlement;

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;
        if (crawler.IsDepowered) return null;

        // Check if we have industry segments
        var segments = crawler.IndustrySegments.ToList();
        if (segments.Count == 0) return null;

        // Check for idle segments (no recipe assigned)
        var idleSegments = segments.Where(s => s.IsActive && s.CurrentRecipe == null).ToList();

        if (idleSegments.Count == 0) {
            return null; // All segments are busy
        }

        // Evaluate and find the best recipe for an idle segment
        var bestEval = EvaluateBestRecipe(crawler, idleSegments);
        if (bestEval == null) {
            return null;
        }

        _lastEvaluationTime = crawler.Time;

        // Calculate cycle duration based on recipe and segment throughput
        var cycleDuration = TimeDuration.FromHours(
            bestEval.Recipe.CycleTime.TotalHours / bestEval.Segment.Throughput);

        // Mark the segment as having a recipe (prevents re-evaluation)
        bestEval.Segment.CurrentRecipe = bestEval.Recipe;

        // Return the production cycle event
        return new ActorEvent.ProductionCycleEvent(
            crawler,
            crawler.Time + cycleDuration,
            bestEval.Segment,
            bestEval.Recipe
        );
    }

    /// <summary>
    /// Evaluate all possible recipes for idle segments and return the best one.
    /// </summary>
    RecipeEvaluation? EvaluateBestRecipe(Crawler crawler, List<IndustrySegment> candidates) {
        var stockTargets = crawler.StockTargets ?? StockTargets.Default();
        var reservation = crawler.ResourceReservation;
        var location = crawler.Location;
        bool isSettlement = IsSettlement(crawler);

        // For settlements, calculate downstream demand propagation
        if (isSettlement) {
            _downstreamDemand = ProductionChain.PropagateDownstreamDemand(
                c => CalculateDeficitRatio(crawler, c),
                Tuning.SettlementProduction.DemandDecayFactor);
            _hasDownstreamDemand = true;
        } else {
            _hasDownstreamDemand = false;
        }

        // Get max tech tier (could come from game state or crawler)
        var maxTier = GameTier.Late; // TODO: Get from Game.Instance or crawler property

        // Parallel evaluation of all segment/recipe combinations
        // Note: All evaluation methods are read-only and thread-safe
        var evaluations = candidates
            .AsParallel()
            .WithDegreeOfParallelism(Environment.ProcessorCount)
            .SelectMany(segment => {
                var availableRecipes = IndustryComponent.GetAvailableRecipes(segment, maxTier);
                return availableRecipes
                    .Where(recipe => reservation.CanStart(recipe, crawler.Supplies, segment.BatchSize))
                    .Select(recipe => isSettlement
                        ? EvaluateRecipeForSettlement(recipe, segment, crawler, reservation, location)
                        : EvaluateRecipe(recipe, segment, crawler, stockTargets, reservation, location))
                    .Where(eval => eval != null);
            })
            .ToList();

        if (evaluations.Count == 0) {
            return null;
        }

        // Return the highest scoring evaluation
        return evaluations.MaxBy(e => e!.TotalScore);
    }

    /// <summary>
    /// Calculate deficit ratio for a commodity (0 = fully stocked, 1 = empty).
    /// </summary>
    float CalculateDeficitRatio(Crawler crawler, Commodity c) {
        if (crawler.Stock == null) return 0;
        float baseline = crawler.Stock.Baseline(c);
        if (baseline <= 0) return 0;
        float current = crawler.Supplies[c] + crawler.Cargo[c];
        return Math.Clamp((baseline - current) / baseline, 0, 1);
    }

    /// <summary>
    /// Evaluate a recipe for a settlement using fuzzy priorities.
    /// TotalScore = DeficitUrgency * 0.40 + InputAvailability * 0.25
    ///            + DownstreamDemand * 0.25 + ProfitMargin * 0.10
    /// </summary>
    RecipeEvaluation? EvaluateRecipeForSettlement(
        ProductionRecipe recipe,
        IndustrySegment segment,
        Crawler crawler,
        ResourceReservation reservation,
        Location location) {

        float batchSize = segment.BatchSize;

        // Check basic requirements (but don't reject - score input availability instead)
        if (crawler.PowerBalance < segment.Drain) return null;
        if (crawler.CrewInv < recipe.CrewRequired) return null;

        // 1. Input Availability (0-1): Can we run this recipe?
        float inputAvailability = CalculateInputAvailability(recipe, crawler, reservation, batchSize);
        if (inputAvailability <= 0) return null; // Can't run at all

        // 2. Deficit Urgency (0-1): How badly do we need the outputs?
        float deficitUrgency = CalculateDeficitUrgency(recipe, crawler, batchSize);

        // 3. Downstream Demand (0-1): Is there demand from production chain?
        float downstreamDemand = 0;
        if (_hasDownstreamDemand) {
            downstreamDemand = ProductionChain.GetRecipeDownstreamDemand(recipe, _downstreamDemand);
        }

        // 4. Profit Margin (normalized 0-1): Tiebreaker
        float profitMargin = CalculateNormalizedProfit(recipe, segment, crawler, location, batchSize);

        // Weighted combination
        float totalScore =
            deficitUrgency * Tuning.SettlementProduction.DeficitUrgencyWeight +
            inputAvailability * Tuning.SettlementProduction.InputAvailabilityWeight +
            downstreamDemand * Tuning.SettlementProduction.DownstreamDemandWeight +
            profitMargin * Tuning.SettlementProduction.ProfitMarginWeight;

        // Minimum threshold
        if (totalScore < Tuning.SettlementProduction.MinRecipeScore) {
            return null;
        }

        return new RecipeEvaluation(recipe, segment, 0, 0, deficitUrgency, totalScore);
    }

    /// <summary>
    /// Calculate input availability score (0-1).
    /// 1.0 = all inputs fully available, 0 = missing critical inputs.
    /// </summary>
    float CalculateInputAvailability(ProductionRecipe recipe, Crawler crawler, ResourceReservation reservation, float batchSize) {
        if (!reservation.CanStart(recipe, crawler.Supplies, batchSize)) return 0;

        float minRatio = 1.0f;

        // Check inputs (scaled by batch size)
        foreach (var (commodity, required) in recipe.Inputs) {
            float available = crawler.Supplies[commodity] + crawler.Cargo[commodity];
            float scaledRequired = required * batchSize;
            float ratio = scaledRequired > 0 ? Math.Min(available / scaledRequired, 2.0f) / 2.0f : 1.0f;
            minRatio = Math.Min(minRatio, ratio);
        }

        // Check consumables (less strict - can run with partial, scaled by batch size)
        foreach (var (commodity, required) in recipe.Consumables) {
            float available = crawler.Supplies[commodity];
            float scaledRequired = required * batchSize;
            if (scaledRequired > 0) {
                float ratio = Math.Clamp(available / (scaledRequired * 5), 0, 1); // 5 cycles buffer
                minRatio = Math.Min(minRatio, 0.5f + ratio * 0.5f); // Floor at 0.5
            }
        }

        return minRatio;
    }

    /// <summary>
    /// Calculate deficit urgency for recipe outputs (0-1).
    /// Higher when outputs are below baseline stock levels.
    /// Essential goods get boosted urgency.
    /// </summary>
    float CalculateDeficitUrgency(ProductionRecipe recipe, Crawler crawler, float batchSize) {
        if (crawler.Stock == null) return 0;

        float totalUrgency = 0;
        float totalWeight = 0;

        foreach (var (commodity, amount) in recipe.Outputs) {
            float baseline = crawler.Stock.Baseline(commodity);
            if (baseline <= 0) continue;

            float current = crawler.Supplies[commodity] + crawler.Cargo[commodity];
            float deficitRatio = Math.Clamp((baseline - current) / baseline, 0, 1);

            // Essential goods get boosted urgency (output scaled by batch size)
            float boost = commodity.IsEssential() ? 1.5f : 1.0f;
            float weight = commodity.BaseCost() * amount * batchSize * boost;

            totalUrgency += deficitRatio * weight;
            totalWeight += weight;
        }

        return totalWeight > 0 ? Math.Clamp(totalUrgency / totalWeight, 0, 1) : 0;
    }

    /// <summary>
    /// Calculate normalized profit margin (0-1).
    /// </summary>
    float CalculateNormalizedProfit(ProductionRecipe recipe, IndustrySegment segment, Crawler crawler, Location location, float batchSize) {
        float inputCost = recipe.Inputs.Sum(kv => kv.Key.CostAt(location) * kv.Value * batchSize);
        float consumableCost = recipe.Consumables.Sum(kv => kv.Key.CostAt(location) * kv.Value * batchSize);
        float maintenanceCost = recipe.Maintenance.Sum(kv => kv.Key.CostAt(location) * kv.Value * batchSize);
        float chargeCost = (segment.ActivateCharge + recipe.ActivateCharge) * Tuning.Production.ChargeValue;
        float totalCost = inputCost + consumableCost + maintenanceCost + chargeCost;

        float outputValue = recipe.Outputs.Sum(kv => kv.Key.CostAt(location) * kv.Value * segment.Efficiency * batchSize);

        if (totalCost <= 0) return 1.0f;

        // Profit ratio: 1.0 = break even, 2.0 = 100% profit
        float profitRatio = outputValue / totalCost;

        // Normalize to 0-1 range (0.5 = break even, 1.0 = 100%+ profit)
        return Math.Clamp((profitRatio - 0.5f) / 1.5f, 0, 1);
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

        float batchSize = segment.BatchSize;

        // Check basic requirements
        if (!reservation.CanStart(recipe, crawler.Supplies, batchSize)) return null;
        if (!recipe.HasConsumables(crawler.Supplies, batchSize)) return null;
        if (crawler.PowerBalance < segment.Drain) return null;
        if (crawler.CrewInv < recipe.CrewRequired) return null;

        // Calculate costs (scaled by batch size)
        float inputCost = recipe.Inputs.Sum(kv => kv.Key.CostAt(location) * kv.Value * batchSize);
        float consumableCost = recipe.Consumables.Sum(kv => kv.Key.CostAt(location) * kv.Value * batchSize);
        float maintenanceCost = recipe.Maintenance.Sum(kv => kv.Key.CostAt(location) * kv.Value * batchSize);
        float chargeCost = (segment.ActivateCharge + recipe.ActivateCharge) * Tuning.Production.ChargeValue;

        float totalCost = inputCost + consumableCost + maintenanceCost + chargeCost;

        // Calculate output value (scaled by batch size and efficiency)
        float outputValue = recipe.Outputs.Sum(kv => kv.Key.CostAt(location) * kv.Value * segment.Efficiency * batchSize);

        // Calculate profit
        float profitPerCycle = outputValue - totalCost;
        float cycleHours = (float)recipe.CycleTime.TotalHours / segment.Throughput;
        float profitPerHour = profitPerCycle / cycleHours;

        // Calculate stock priority bonus (output amounts scaled by batch size)
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
