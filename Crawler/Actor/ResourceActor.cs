namespace Crawler;

/// <summary>
/// Actor representing an extractable resource deposit.
/// Uses Poisson sampling for extraction yields with natural diminishing returns.
/// </summary>
public class ResourceActor : ActorBase {
    public Commodity ResourceType { get; }

    readonly float _totalAmount;           // Hidden until estimated
    float _remainingAmount;                // What's left to extract
    readonly float _baseYieldPerCycle;     // Base extraction rate (lambda for Poisson)
    readonly float _estimateVariance;      // Variance for noisy estimate display
    readonly List<float> _extractionHistory = [];  // Track each cycle's yield

    // Refill parameters
    readonly bool _refillable;
    readonly float _refillRate;            // Units per hour
    readonly float _maxAmount;             // Cap for refilling

    XorShift _rng;

    public ResourceActor(
        ulong seed,
        Location location,
        Commodity type,
        ResourceParams p,
        string name,
        string description
    ) : base(seed, name, description, Factions.Independent, new Inventory(), new Inventory(), location) {
        _rng = new XorShift(seed);
        ResourceType = type;

        // Initialize total with variance: random in range [-variance, +variance]
        float varianceFactor = (_rng.NextSingle() * 2 - 1) * p.AmountVariance;
        _totalAmount = p.BaseAmount * (1 + varianceFactor);
        _remainingAmount = _totalAmount;
        _maxAmount = _totalAmount;
        _baseYieldPerCycle = p.BaseYield;
        _estimateVariance = p.EstimateVariance;
        _refillable = p.Refillable;
        _refillRate = p.RefillRate;
    }

    /// <summary>
    /// Noisy estimate of remaining amount after enough extraction cycles.
    /// Returns NaN if not enough cycles have been performed yet.
    /// </summary>
    public float EstimatedRemaining => _extractionHistory.Count >= Tuning.Resource.EstimateCycles
        ? _remainingAmount * (1 + (_rng.NextSingle() * 2 - 1) * _estimateVariance)
        : float.NaN;

    /// <summary>
    /// Number of extraction cycles performed on this resource.
    /// </summary>
    public int ExtractionCycles => _extractionHistory.Count;

    /// <summary>
    /// Whether this resource has been fully depleted.
    /// </summary>
    public bool IsExhausted => _remainingAmount <= 0;

    /// <summary>
    /// Whether this resource can regenerate over time.
    /// </summary>
    public bool IsRefillable => _refillable;

    /// <summary>
    /// Extract resources using Poisson sampling.
    /// </summary>
    /// <param name="harvesterYield">Total yield multiplier from harvest segments.</param>
    /// <param name="harvesterCount">Number of compatible harvest segments.</param>
    /// <returns>Actual amount extracted.</returns>
    public float Extract(float harvesterYield, int harvesterCount) {
        if (_remainingAmount <= 0) return 0;

        // 1. Calculate lambda = baseYield * harvesterYield * harvesterCount
        float lambda = _baseYieldPerCycle * harvesterYield * harvesterCount;

        // 2. Apply diminishing returns: scale lambda by remaining fraction
        float remainingFraction = _remainingAmount / _totalAmount;
        lambda *= remainingFraction;  // Natural depletion curve

        // 3. Sample from Poisson distribution for actual yield
        int yield = CrawlerEx.PoissonQuantile(lambda, ref _rng);
        float actualYield = Math.Min(yield, _remainingAmount);

        // 4. Update remaining
        _remainingAmount -= actualYield;
        _extractionHistory.Add(actualYield);

        return actualYield;
    }

    /// <summary>
    /// Simulate resource refill over elapsed time.
    /// </summary>
    public override void SimulateTo(TimePoint time) {
        base.SimulateTo(time);

        if (_refillable && _remainingAmount < _maxAmount && Elapsed.TotalHours > 0) {
            float refillAmount = _refillRate * (float)Elapsed.TotalHours;
            _remainingAmount = Math.Min(_maxAmount, _remainingAmount + refillAmount);
        }
    }
}

/// <summary>
/// Component for extracting resources from a ResourceActor.
/// Requires compatible harvest segments on the crawler.
/// </summary>
public class ExtractionComponent : ActorComponentBase {
    ResourceActor Resource => (ResourceActor)Owner!;

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        if (Resource.IsExhausted) yield break;
        if (subject is not Crawler crawler) yield break;

        // Get compatible harvest segments
        var harvestSegments = GetCompatibleHarvestSegments(crawler);
        if (harvestSegments.Count == 0) {
            // Show disabled interaction explaining requirement
            yield return new MissingHarvestInteraction(Resource, subject);
            yield break;
        }

        float totalYield = harvestSegments.Sum(h => h.Yield);
        yield return new ExtractInteraction(Resource, subject, harvestSegments, totalYield);
    }

    List<HarvestSegment> GetCompatibleHarvestSegments(Crawler crawler) {
        return crawler.Segments
            .OfType<HarvestSegment>()
            .Where(h => h.IsActive && h.ExtractableCommodities.Contains(Resource.ResourceType))
            .ToList();
    }
}

/// <summary>
/// Interaction for extracting resources from a deposit.
/// </summary>
public record ExtractInteraction(
    ResourceActor Resource,
    IActor Harvester,
    List<HarvestSegment> HarvestSegments,
    float TotalYield
) : Interaction(Resource, Harvester, "E") {

    public override string Description {
        get {
            var estimate = Resource.EstimatedRemaining;
            string remaining = float.IsNaN(estimate)
                ? "unknown amount"
                : $"~{estimate:F0} remaining";
            return $"Extract {Resource.ResourceType} ({HarvestSegments.Count} harvester(s), {remaining})";
        }
    }

    public override bool Perform(string args = "") {
        float extracted = Resource.Extract(TotalYield, HarvestSegments.Count);

        if (Harvester is Crawler c) {
            c.Supplies.Add(Resource.ResourceType, extracted);
        }

        // Show extraction result
        Harvester.Message($"Extracted {extracted:F1} {Resource.ResourceType}");

        // Show estimate if available
        var estimate = Resource.EstimatedRemaining;
        if (!float.IsNaN(estimate)) {
            Harvester.Message($"Estimated remaining: ~{estimate:F0} {Resource.ResourceType}");
        }

        if (Resource.IsExhausted) {
            Resource.SetEndState(EEndState.Looted, "depleted");
            Harvester.Message($"{Resource.Name} is exhausted");
        }

        // Time consumption for extraction
        Harvester.ConsumeTime("Extracting", 300, ExpectedDuration);

        return true;
    }

    public override Immediacy GetImmediacy(string args = "") {
        if (Resource.IsExhausted) return Immediacy.Failed;
        if (HarvestSegments.Count == 0) return Immediacy.Failed;
        return Immediacy.Menu;
    }

    public override TimeDuration ExpectedDuration => Tuning.Resource.ExtractionTime;
}

/// <summary>
/// Disabled interaction shown when player lacks required harvest equipment.
/// </summary>
public record MissingHarvestInteraction(
    ResourceActor Resource,
    IActor Subject
) : Interaction(Resource, Subject, "E") {

    public override string Description =>
        $"Extract {Resource.ResourceType} (requires {RequiredHarvester})";

    string RequiredHarvester => Resource.ResourceType switch {
        Commodity.Ore => "Mining Drill",
        Commodity.Silicates => "Mining Drill or Crystal Extractor",
        Commodity.Biomass => "Biomass Harvester",
        Commodity.Gems => "Crystal Extractor",
        Commodity.Isotopes => "Isotope Collector",
        _ => "harvester segment"
    };

    public override Immediacy GetImmediacy(string args = "") => Immediacy.Failed;
    public override bool Perform(string args = "") => false;
}
