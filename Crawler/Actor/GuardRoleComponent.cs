namespace Crawler;

using Convoy;

/// <summary>
/// AI component for NPC guard crawlers when not hired.
/// Travels between settlements seeking work opportunities.
/// Defers to GuardComponent when guard is under contract.
/// </summary>
public class GuardRoleComponent : ActorComponentBase {
    XorShift _rng;
    Location? _destination;
    GuardJobAction _currentAction;

    enum GuardJobAction {
        Idle,
        TravelingToSettlement,
        WaitingForWork,
        Exploring
    }

    public GuardRoleComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 300; // Below GuardComponent (550)

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler guard) return null;
        if (guard.IsDepowered) return null;

        // Check if hired - if so, let GuardComponent handle behavior
        var guardComponent = guard.Components.OfType<GuardComponent>().FirstOrDefault();
        if (guardComponent?.IsHired == true) return null;

        var time = guard.Time;

        // If we have a destination, travel there
        if (_destination != null && _destination != guard.Location) {
            return CreateTravelEvent(guard, _destination, time);
        }

        // Decide what to do based on current action
        switch (_currentAction) {
            case GuardJobAction.TravelingToSettlement:
                // We've arrived at a settlement
                _currentAction = GuardJobAction.WaitingForWork;
                _destination = null;
                return GetNextEvent(); // Re-evaluate

            case GuardJobAction.WaitingForWork:
                // Wait for someone to hire us
                _currentAction = GuardJobAction.Idle;
                // Wait at settlement for work
                int waitPriority = EventPriority.ForJobSeeking(guard);
                return guard.NewEventFor("Looking for work", waitPriority,
                    Tuning.Guard.JobWaitDuration, Post: () => { });

            case GuardJobAction.Exploring:
                _destination = null;
                _currentAction = GuardJobAction.Idle;
                break;
        }

        // Plan next movement
        return PlanNextMove(guard, time);
    }

    ActorEvent? PlanNextMove(Crawler guard, TimePoint time) {
        // Find a settlement with potential for hiring
        var settlement = FindSettlementWithHiring(guard);
        if (settlement != null) {
            _destination = settlement;
            _currentAction = GuardJobAction.TravelingToSettlement;
            return CreateTravelEvent(guard, settlement, time);
        }

        // No good settlements found, explore or wait
        if (_rng.NextSingle() < 0.5f) {
            var exploreLoc = PickExploreDestination(guard);
            if (exploreLoc != null) {
                _destination = exploreLoc;
                _currentAction = GuardJobAction.Exploring;
                return CreateTravelEvent(guard, exploreLoc, time);
            }
        }

        // Wait and retry later
        int priority = EventPriority.ForJobSeeking(guard);
        return guard.NewEventFor("Waiting for work", priority,
            Tuning.Guard.IdleWaitDuration, Post: () => { });
    }

    Location? FindSettlementWithHiring(Crawler guard) {
        var map = guard.Location.Map;
        if (map.TradeNetwork == null) return null;

        // Find settlements within search radius
        var candidates = map.FindLocationsInRadius(guard.Location.Position, Tuning.Guard.MaxSearchRadius)
            .Where(loc => loc.Type == EncounterType.Settlement)
            .Where(loc => loc != guard.Location)
            .OrderBy(loc => guard.Location.Distance(loc))
            .ToList();

        // Prefer settlements with larger populations (more likely to have hiring)
        if (candidates.Count > 0) {
            // Weight by population
            var weighted = candidates
                .OrderByDescending(loc => loc.Population)
                .Take(3)
                .ToList();

            return _rng.ChooseRandom(weighted) ?? candidates.First();
        }

        return null;
    }

    Location? PickExploreDestination(Crawler guard) {
        var network = guard.Location.Map?.TradeNetwork;
        if (network == null) return null;

        // Random nearby location
        var candidates = network.RoadsFrom(guard.Location)
            .Select(r => r.To)
            .Where(loc => loc.Type is EncounterType.Settlement or EncounterType.Crossroads)
            .ToList();

        if (candidates.Count == 0) return null;
        return _rng.ChooseRandom(candidates);
    }

    ActorEvent? CreateTravelEvent(Crawler crawler, Location destination, TimePoint time) {
        var network = crawler.Location.Map?.TradeNetwork;
        if (network == null) return null;

        // Find path to destination
        var path = network.FindPath(crawler.Location, destination);
        if (path == null || path.Count < 2) {
            // Direct travel if no network path
            var (_, travelHours) = crawler.FuelTimeTo(destination);
            if (travelHours < 0) return null; // Cannot reach destination

            var arrivalTime = time + TimeDuration.FromHours(travelHours);
            return new ActorEvent.TravelEvent(crawler, arrivalTime, destination);
        }

        // Travel to next hop in path
        var nextHop = path[1];
        var (_, hopHours) = crawler.FuelTimeTo(nextHop);
        if (hopHours < 0) return null; // Cannot reach next hop

        var hopArrivalTime = time + TimeDuration.FromHours(hopHours);
        _destination = path.Count > 2 ? destination : null; // Clear if last hop

        return new ActorEvent.TravelEvent(crawler, hopArrivalTime, nextHop);
    }
}
