namespace Crawler;

using Production;

/// <summary>
/// Component that handles settlement population growth and dome construction.
/// Settlements grow when population has room, and build domes when overcrowded.
/// Priority scales dynamically with population pressure (1-1000).
/// </summary>
public class SettlementGrowthComponent : ActorComponentBase {
    readonly XorShift _rng;
    TimePoint _lastGrowthCheck;
    TimePoint _lastConstructionCheck;

    /// <summary>Minimum interval between growth checks (1 game day = 10 hours).</summary>
    static readonly TimeDuration GrowthCheckInterval = TimeDuration.FromHours(10);

    /// <summary>Minimum interval between construction evaluations.</summary>
    static readonly TimeDuration ConstructionCheckInterval = TimeDuration.FromHours(1);

    public SettlementGrowthComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    /// <summary>
    /// Priority scales with population pressure:
    /// - Below 80% capacity = 0 (no urgency)
    /// - 80% capacity = weight 1
    /// - 100% capacity = weight ~250
    /// - 120%+ capacity = weight 1000
    /// </summary>
    public override int Priority {
        get {
            if (Owner is not Crawler crawler) return 0;
            if (!crawler.Flags.HasFlag(ActorFlags.Settlement)) return 0;

            float capacity = GetHousingCapacity(crawler);
            if (capacity <= 0) return 0;

            float ratio = crawler.Location.Population / capacity;
            if (ratio < 0.8f) return 0; // No urgency below 80%

            // Linear scale: 0.8 -> 1, 1.2 -> 1000
            float normalized = (ratio - 0.8f) / 0.4f; // 0 at 80%, 1 at 120%
            return (int)Math.Clamp(1 + normalized * 999, 1, 1000);
        }
    }

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }

    public override void Tick() {
        if (Owner is not Crawler crawler) return;
        if (!crawler.Flags.HasFlag(ActorFlags.Settlement)) return;

        var time = crawler.Time;

        // Check for population growth periodically
        if (!_lastGrowthCheck.IsValid || time - _lastGrowthCheck >= GrowthCheckInterval) {
            CheckPopulationGrowth(crawler);
            _lastGrowthCheck = time;
        }
    }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;
        if (!crawler.Flags.HasFlag(ActorFlags.Settlement)) return null;
        if (crawler.IsDepowered) return null;

        var time = crawler.Time;
        if (_lastConstructionCheck.IsValid && time - _lastConstructionCheck < ConstructionCheckInterval) {
            return null;
        }
        _lastConstructionCheck = time;

        // Check if we need more domes
        if (ShouldBuildDome(crawler)) {
            TryQueueDomeConstruction(crawler);
        }

        return null;
    }

    /// <summary>
    /// Calculate total housing capacity from habitat segments.
    /// </summary>
    public static float GetHousingCapacity(Crawler crawler) {
        return crawler.Segments
            .OfType<HabitatSegment>()
            .Where(h => h.IsUsable)
            .Sum(h => h.CrewCapacity);
    }

    /// <summary>
    /// Check if population should grow based on housing capacity and resources.
    /// </summary>
    void CheckPopulationGrowth(Crawler crawler) {
        var location = crawler.Location;
        float housingCapacity = GetHousingCapacity(crawler);
        int currentPop = location.Population;

        // Growth only happens if there's room (below 90% capacity)
        if (currentPop >= housingCapacity * 0.9f) return;

        // Calculate growth rate based on prosperity factors
        float growthRate = CalculateGrowthRate(crawler, currentPop, housingCapacity);

        if (_rng.NextSingle() < growthRate) {
            // Grow population
            int growth = Math.Max(1, (int)(currentPop * 0.01f));
            int newPop = Math.Min(currentPop + growth, (int)(housingCapacity * 0.9f));
            location.Population = newPop;

            if (newPop > currentPop) {
                crawler.Message($"{crawler.Name} population grew to {newPop}");
            }
        }
    }

    /// <summary>
    /// Calculate daily growth probability based on prosperity.
    /// </summary>
    float CalculateGrowthRate(Crawler crawler, int population, float capacity) {
        float baseRate = 0.05f; // 5% base daily growth chance

        // Food availability modifier
        float rationTarget = population * 0.001f * 24; // Daily ration need
        float rationRatio = rationTarget > 0
            ? crawler.Supplies[Commodity.Rations] / rationTarget
            : 1.0f;
        float foodMod = Math.Clamp(rationRatio, 0.2f, 1.5f);

        // Housing pressure (growth slower near capacity)
        float capacityRatio = population / capacity;
        float housingMod = capacityRatio < 0.7f ? 1.0f : Math.Max(0, 1.0f - (capacityRatio - 0.7f) * 3);

        // Wealth modifier
        float wealthMod = Math.Clamp(crawler.Location.Wealth / 100f, 0.5f, 2.0f);

        return baseRate * foodMod * housingMod * wealthMod;
    }

    /// <summary>
    /// Check if settlement needs to build more domes.
    /// </summary>
    bool ShouldBuildDome(Crawler crawler) {
        float housingCapacity = GetHousingCapacity(crawler);
        int population = crawler.Location.Population;

        // Build when at 80%+ capacity
        return population >= housingCapacity * 0.8f;
    }

    /// <summary>
    /// Try to queue dome construction on an available fabricator.
    /// </summary>
    void TryQueueDomeConstruction(Crawler crawler) {
        // Find idle fabricator with sufficient size
        var fabricators = crawler.IndustrySegments
            .Where(i => i.IndustryType == IndustryType.Fabricator)
            .Where(i => i.IsActive && i.CurrentRecipe == null)
            .OrderByDescending(i => i.SegmentDef.Size.Size)
            .ToList();

        if (fabricators.Count == 0) return;

        // Select appropriate dome based on fabricator capability
        var bestFab = fabricators.First();
        int fabSize = (int)bestFab.SegmentDef.Size.Size;

        // Find dome recipe that can be built by this fabricator and we can afford
        var domeRecipe = SelectDomeRecipe(crawler, fabSize);
        if (domeRecipe == null) return;

        // Convert to commodity recipe wrapper for production system
        // For now, just message that we want to build (actual integration needs more work)
        crawler.Message($"{crawler.Name} wants to construct {domeRecipe.OutputDef.NameSize} (needs integration)");

        // TODO: Integrate with segment manufacturing system
        // The ManufacturingComponent handles player-initiated manufacturing,
        // but we need a way for AI to trigger segment production directly
    }

    /// <summary>
    /// Select the best dome recipe we can build and afford.
    /// </summary>
    SegmentRecipe? SelectDomeRecipe(Crawler crawler, int fabricatorSize) {
        // RequiredIndustrySize = FactorySize + 2
        // So fabricatorSize >= FactorySize + 2
        // Max FactorySize we can build = fabricatorSize - 2
        float maxFactorySize = fabricatorSize - 2;

        var domeDefs = SegmentEx.HabitatDefs
            .Where(h => h.Type == HabitatType.Dome)
            .Where(h => h.FactorySize.Size <= maxFactorySize)
            .OrderByDescending(h => h.Size.Size);

        foreach (var domeDef in domeDefs) {
            var recipe = SegmentRecipeEx.CreateDomeRecipe(domeDef);
            if (recipe.HasInputs(crawler.Supplies)) {
                return recipe;
            }
        }
        return null;
    }
}
