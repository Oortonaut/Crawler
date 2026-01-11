using Crawler.Economy;

namespace Crawler.Convoy;

/// <summary>
/// Settlement component that manages available guards for hire.
/// Guards are pre-generated based on settlement population and refresh periodically.
/// </summary>
public class GuardHireComponent : ActorComponentBase {
    readonly XorShift _rng;
    readonly List<Crawler> _availableGuards = [];
    TimePoint _lastRefresh;

    /// <summary>How often guards refresh at settlement.</summary>
    public static TimeDuration RefreshInterval => TimeDuration.FromDays(1);

    /// <summary>Maximum guards per settlement based on population.</summary>
    public static int MaxGuardsForPopulation(int population) =>
        Math.Min(3, Math.Max(0, population / 100));

    public GuardHireComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 450;

    /// <summary>Guards currently available for hire.</summary>
    public IReadOnlyList<Crawler> AvailableGuards => _availableGuards;

    public override void Enter(Encounter encounter) {
        // Refresh guard pool when encounter is entered
        RefreshGuardPool();
    }

    /// <summary>Generate or refresh available guards.</summary>
    void RefreshGuardPool() {
        if (Owner is not Crawler settlement) return;
        if (!settlement.Flags.HasFlag(ActorFlags.Settlement)) return;

        // Check if refresh is needed
        if (_lastRefresh.IsValid && Owner.Time - _lastRefresh < RefreshInterval) {
            return;
        }

        _availableGuards.Clear();

        int maxGuards = MaxGuardsForPopulation(settlement.Location.Population);
        int numGuards = _rng.NextInt(0, maxGuards + 1);

        for (int i = 0; i < numGuards; i++) {
            var guard = GenerateGuard(_rng.Seed());
            _availableGuards.Add(guard);
        }

        _lastRefresh = Owner.Time;
    }

    /// <summary>Generate a guard crawler.</summary>
    Crawler GenerateGuard(ulong seed) {
        var guardRng = new XorShift(seed);
        var location = Owner.Location;

        // Guards scale with location wealth but are combat-focused
        float wealth = location.Wealth * 0.4f;
        int crew = Math.Max(3, (int)(wealth / 100));
        crew = Math.Min(crew, 15);

        // Segment wealth allocation: prioritize offense and defense
        float segmentWealth = wealth * 0.7f;

        // Create guard with combat-focused loadout
        // supplyDays, goodsWealth, segmentWealth, segmentClassWeights
        float supplyDays = 10;
        float goodsWealth = 0; // Guards don't carry trade goods
        EArray<SegmentKind, float> weights = [0.6f, 0.6f, 1.5f, 1.3f, 0.2f, 0.3f, 0.1f];
        var guard = Crawler.NewRandom(
            guardRng.Seed(),
            Factions.Independent,
            location,
            crew,
            supplyDays,
            goodsWealth,
            segmentWealth,
            weights
        );

        guard.Name = Names.HumanName(guardRng.Seed());
        guard.Role = Roles.Guard;

        // Initialize components but don't add to encounter yet
        guard.InitializeComponents(guardRng.Seed());

        return guard;
    }

    /// <summary>Calculate cost to hire a guard for a route.</summary>
    public float CalculateHireCost(Crawler guard, Location destination) {
        var network = Owner.Location.Map.TradeNetwork;
        if (network == null) return -1;

        var path = network.FindPath(Owner.Location, destination);
        if (path == null) return -1;

        // Base cost factors:
        // - Distance
        // - Route risk
        // - Guard quality (firepower + defense)

        float distance = 0;
        for (int i = 0; i < path.Count - 1; i++) {
            distance += path[i].Distance(path[i + 1]);
        }

        // Get risk if available
        float riskMultiplier = 1.0f;
        var factionNetwork = FactionRiskNetworks.GetNetwork(Owner.Location.Sector.ControllingFaction);
        float routeRisk = factionNetwork.RiskTracker.GetRouteRisk(path, Owner.Time, network);
        riskMultiplier = 1.0f + routeRisk * 0.5f;

        // Guard quality
        float guardQuality = guard.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage) +
                            guard.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction);
        float qualityMultiplier = 1.0f + guardQuality * 0.01f;

        // Base rate per km
        float baseRate = Tuning.Convoy.GuardBaseCostPerKm;

        return distance * baseRate * riskMultiplier * qualityMultiplier;
    }

    /// <summary>Hire a guard for a convoy.</summary>
    public bool HireGuard(Crawler guard, IActor employer, Convoy convoy, Location destination) {
        if (!_availableGuards.Contains(guard)) return false;

        float cost = CalculateHireCost(guard, destination);
        if (cost < 0) return false;

        // Check employer can afford deposit (half upfront)
        float deposit = cost / 2;
        if (employer.Supplies[Commodity.Scrap] < deposit) {
            employer.Message("Not enough scrap to hire this guard.");
            return false;
        }

        // Pay deposit
        employer.Supplies[Commodity.Scrap] -= deposit;
        guard.Supplies[Commodity.Scrap] += deposit;

        // Get guard component and accept contract
        var guardComponent = guard.Components.OfType<GuardComponent>().FirstOrDefault();
        if (guardComponent == null) {
            // Refund if guard doesn't have component
            employer.Supplies[Commodity.Scrap] += deposit;
            guard.Supplies[Commodity.Scrap] -= deposit;
            return false;
        }

        guardComponent.AcceptContract(convoy, employer, destination, cost);

        // Add guard to encounter
        var encounter = Owner.Location.GetEncounter();
        if (!encounter.Actors.Contains(guard)) {
            encounter.AddActorAt(guard, Owner.Time);
        }

        // Remove from available pool
        _availableGuards.Remove(guard);

        employer.Message($"Hired {guard.Name} as escort for {cost:F0} scrap ({deposit:F0} paid upfront).");
        return true;
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Only show hiring options if subject is in a convoy or about to form one
        var convoy = ConvoyRegistry.GetConvoy(subject);

        // Refresh guards if needed
        RefreshGuardPool();

        if (_availableGuards.Count == 0) yield break;

        // Show available guards
        for (int i = 0; i < _availableGuards.Count; i++) {
            var guard = _availableGuards[i];
            yield return new HireGuardInteraction(subject, Owner, guard, $"HG{i + 1}");
        }
    }
}

/// <summary>
/// Interaction to hire a guard from a settlement.
/// </summary>
public record HireGuardInteraction(
    IActor Mechanic,  // The hirer (player)
    IActor Subject,   // The settlement
    Crawler Guard,
    string MenuOption
) : Interaction(Mechanic, Subject, MenuOption) {

    public override string Description {
        get {
            var firepower = Guard.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage);
            var defense = Guard.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction);
            return $"Hire {Guard.Name} (ATK:{firepower:F0} DEF:{defense:F0} Crew:{Guard.CrewInv})";
        }
    }

    public override Immediacy GetImmediacy(string args = "") {
        // Must be in a convoy to hire guards
        var convoy = ConvoyRegistry.GetConvoy(Mechanic);
        if (convoy == null) {
            return Immediacy.Failed; // Need to form convoy first
        }

        return Immediacy.Menu;
    }

    public override bool Perform(string args = "") {
        var convoy = ConvoyRegistry.GetConvoy(Mechanic);
        if (convoy == null) {
            Mechanic.Message("You must form a convoy before hiring guards.");
            return false;
        }

        var hireComponent = (Subject as Crawler)?.Components
            .OfType<GuardHireComponent>().FirstOrDefault();
        if (hireComponent == null) return false;

        var destination = convoy.Destination;
        if (destination == null) {
            Mechanic.Message("Convoy has no destination set.");
            return false;
        }

        return hireComponent.HireGuard(Guard, Mechanic, convoy, destination);
    }

    public override TimeDuration ExpectedDuration => TimeDuration.FromMinutes(5);
}

/// <summary>
/// Interaction to view guard details before hiring.
/// </summary>
public record ViewGuardInteraction(
    IActor Mechanic,
    IActor Subject,
    Crawler Guard,
    string MenuOption
) : Interaction(Mechanic, Subject, MenuOption) {

    public override string Description => $"Inspect {Guard.Name}";

    public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;

    public override bool Perform(string args = "") {
        // Display guard stats
        var firepower = Guard.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage);
        var defense = Guard.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction);
        var speed = Guard.Speed;

        Mechanic.Message($"=== {Guard.Name} ===");
        Mechanic.Message($"Crew: {Guard.CrewInv}  Domes: {Guard.Domes}");
        Mechanic.Message($"Firepower: {firepower:F0}  Defense: {defense:F0}");
        Mechanic.Message($"Speed: {speed:F0} km/h");
        Mechanic.Message($"Segments: {Guard.Segments.Count}");

        return true;
    }
}
