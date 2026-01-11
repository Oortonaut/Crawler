namespace Crawler;

using Convoy;

/// <summary>
/// AI component for NPC traveler crawlers.
/// Wanders between settlements randomly, may join convoys for safety.
/// </summary>
public class TravelerRoleComponent : ActorComponentBase {
    XorShift _rng;
    Location? _destination;
    TravelerAction _currentAction;

    enum TravelerAction {
        Idle,
        TravelingToDestination,
        Visiting,
        Exploring
    }

    public TravelerRoleComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 200; // Same as TradeRoleComponent

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;
        if (crawler.IsDepowered) return null;

        var time = crawler.Time;

        // If we have a destination, travel there
        if (_destination != null && _destination != crawler.Location) {
            return CreateTravelEvent(crawler, _destination, time);
        }

        // Decide what to do based on current action
        switch (_currentAction) {
            case TravelerAction.TravelingToDestination:
                // We've arrived
                _currentAction = TravelerAction.Visiting;
                _destination = null;
                return GetNextEvent(); // Re-evaluate

            case TravelerAction.Visiting:
                // Done visiting, pick next destination
                _currentAction = TravelerAction.Idle;
                break;

            case TravelerAction.Exploring:
                _destination = null;
                _currentAction = TravelerAction.Idle;
                break;
        }

        // Plan next trip
        return PlanNextTrip(crawler, time);
    }

    ActorEvent? PlanNextTrip(Crawler crawler, TimePoint time) {
        // Pick a random destination
        var destination = PickDestination(crawler);
        if (destination != null) {
            _destination = destination;
            _currentAction = TravelerAction.TravelingToDestination;
            return CreateTravelEvent(crawler, destination, time);
        }

        // No destination found, wait and retry
        int priority = EventPriority.ForWander(crawler);
        return crawler.NewEventFor("Waiting to travel", priority,
            Tuning.TravelerWander.IdleWaitDuration, Post: () => { });
    }

    Location? PickDestination(Crawler crawler) {
        var network = crawler.Location.Map?.TradeNetwork;
        if (network == null) return null;

        // Find settlements and crossroads to visit
        var candidates = network.Nodes
            .Where(loc => loc.Type is EncounterType.Settlement or EncounterType.Crossroads)
            .Where(loc => loc != crawler.Location)
            .ToList();

        if (candidates.Count == 0) return null;
        return _rng.ChooseRandom(candidates);
    }

    ActorEvent? CreateTravelEvent(Crawler crawler, Location destination, TimePoint time) {
        // Check if we should form/join a convoy instead of traveling solo
        var convoyDecision = crawler.Components
            .OfType<ConvoyDecisionComponent>().FirstOrDefault();

        if (convoyDecision != null) {
            convoyDecision.SetDestination(destination);

            // Check if we're already in a convoy going our way
            var existingConvoy = ConvoyRegistry.GetConvoy(crawler);
            if (existingConvoy != null && existingConvoy.Route.Contains(destination)) {
                // Let ConvoyComponent handle travel
                return null;
            }

            // Try to join an existing convoy at this location
            foreach (var convoy in ConvoyRegistry.ConvoysAt(crawler.Location)) {
                if (convoyDecision.ShouldJoinConvoy(convoy)) {
                    convoy.AddMember(crawler);
                    crawler.Message($"{crawler.Name} joined convoy to {convoy.Destination?.Description}.");
                    return null; // ConvoyComponent will handle travel
                }
            }

            // Try to form a new convoy if route is risky
            if (convoyDecision.ShouldFormConvoy(destination)) {
                var tradeNet = crawler.Location.Map?.TradeNetwork;
                if (tradeNet != null) {
                    var route = tradeNet.FindPath(crawler.Location, destination);
                    if (route != null && route.Count >= 2) {
                        ConvoyRegistry.Create(crawler, route);
                        crawler.Message($"{crawler.Name} formed convoy to {destination.Description}.");
                        return null; // ConvoyComponent will handle travel
                    }
                }
            }
        }

        // No convoy - travel solo
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
