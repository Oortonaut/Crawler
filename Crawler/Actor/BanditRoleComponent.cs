namespace Crawler;

/// <summary>
/// AI component for NPC bandit crawlers.
/// Positions bandits at strategic ambush points (crossroads) along trade routes.
/// BanditComponent handles the actual extortion when targets arrive.
/// </summary>
public class BanditRoleComponent : ActorComponentBase {
    XorShift _rng;
    Location? _destination;
    BanditPatrolAction _currentAction;
    TimePoint _ambushStartTime;

    enum BanditPatrolAction {
        Idle,
        TravelingToAmbushPoint,
        WaitingForTarget,
        Repositioning
    }

    public BanditRoleComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 400; // Below BanditComponent (600)

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler bandit) return null;
        if (bandit.IsDepowered) return null;

        var time = bandit.Time;

        // If we have a destination, travel there
        if (_destination != null && _destination != bandit.Location) {
            return CreateTravelEvent(bandit, _destination, time);
        }

        // Decide what to do based on current action
        switch (_currentAction) {
            case BanditPatrolAction.TravelingToAmbushPoint:
                // We've arrived at ambush point
                _currentAction = BanditPatrolAction.WaitingForTarget;
                _destination = null;
                _ambushStartTime = time;
                return GetNextEvent(); // Re-evaluate

            case BanditPatrolAction.WaitingForTarget:
                // Check if we've waited too long
                if (_ambushStartTime.IsValid &&
                    time - _ambushStartTime > Tuning.BanditPatrol.AmbushWaitDuration) {
                    // Consider relocating
                    if (_rng.NextSingle() < Tuning.BanditPatrol.RelocateChance) {
                        _currentAction = BanditPatrolAction.Repositioning;
                        return GetNextEvent();
                    }
                }
                // Wait for targets (BanditComponent handles extortion)
                int waitPriority = EventPriority.ForBanditPatrol(bandit);
                return bandit.NewEventFor("Waiting for targets", waitPriority,
                    Tuning.BanditPatrol.AmbushWaitDuration, Post: () => { });

            case BanditPatrolAction.Repositioning:
                _destination = null;
                _currentAction = BanditPatrolAction.Idle;
                break;
        }

        // Plan next ambush position
        return PlanNextAmbush(bandit, time);
    }

    ActorEvent? PlanNextAmbush(Crawler bandit, TimePoint time) {
        // Find a good ambush location
        var ambushPoint = FindAmbushLocation(bandit);
        if (ambushPoint != null && ambushPoint != bandit.Location) {
            _destination = ambushPoint;
            _currentAction = BanditPatrolAction.TravelingToAmbushPoint;
            return CreateTravelEvent(bandit, ambushPoint, time);
        }

        // Already at a good spot or no good spots found
        if (IsGoodAmbushSpot(bandit.Location)) {
            _currentAction = BanditPatrolAction.WaitingForTarget;
            _ambushStartTime = time;
            int waitPriority = EventPriority.ForBanditPatrol(bandit);
            return bandit.NewEventFor("Waiting for targets", waitPriority,
                Tuning.BanditPatrol.AmbushWaitDuration, Post: () => { });
        }

        // Wait and try again
        int priority = EventPriority.ForBanditPatrol(bandit);
        return bandit.NewEventFor("Scouting area", priority,
            Tuning.BanditPatrol.IdleWaitDuration, Post: () => { });
    }

    Location? FindAmbushLocation(Crawler bandit) {
        var network = bandit.Location.Map?.TradeNetwork;
        if (network == null) return null;

        // Find crossroads with high traffic potential (more road connections)
        var candidates = network.Nodes
            .Where(loc => loc.Type == EncounterType.Crossroads)
            .Where(loc => loc != bandit.Location)
            .Select(loc => new {
                Location = loc,
                TrafficScore = GetTrafficScore(loc, network)
            })
            .Where(x => x.TrafficScore > 0)
            .OrderByDescending(x => x.TrafficScore)
            .ThenBy(x => bandit.Location.Distance(x.Location))
            .Take(5)
            .Select(x => x.Location)
            .ToList();

        if (candidates.Count == 0) {
            // No crossroads found, try any connected location
            var roads = network.RoadsFrom(bandit.Location).ToList();
            if (roads.Count > 0) {
                return _rng.ChooseRandom(roads)?.To;
            }
            return null;
        }

        // Pick from top candidates with some randomness
        return _rng.ChooseRandom(candidates);
    }

    float GetTrafficScore(Location location, Network.TradeNetwork network) {
        // Score based on:
        // 1. Number of road connections (more = more traffic)
        // 2. Nearby settlement populations (more = more trade traffic)

        var roads = network.RoadsFrom(location).ToList();
        float connectionScore = roads.Count * 10f;

        // Check for nearby settlements
        float populationScore = 0;
        foreach (var road in roads) {
            if (road.To.Type == EncounterType.Settlement) {
                populationScore += road.To.Population * 0.01f;
            }
        }

        return connectionScore + populationScore;
    }

    bool IsGoodAmbushSpot(Location location) {
        // Crossroads are ideal ambush spots
        if (location.Type == EncounterType.Crossroads) return true;

        // Locations on routes between settlements are also decent
        var network = location.Map?.TradeNetwork;
        if (network == null) return false;

        var roads = network.RoadsFrom(location).ToList();
        return roads.Count >= 2;
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
