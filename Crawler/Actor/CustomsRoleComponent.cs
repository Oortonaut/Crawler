namespace Crawler;

/// <summary>
/// AI component for NPC customs officer crawlers.
/// Patrols settlements and crossroads within faction territory.
/// CustomsComponent handles the actual contraband scanning when targets are present.
/// </summary>
public class CustomsRoleComponent : ActorComponentBase {
    XorShift _rng;
    Location? _destination;
    CustomsPatrolAction _currentAction;

    enum CustomsPatrolAction {
        Idle,
        TravelingToCheckpoint,
        PatrollingCheckpoint,
        TravelingToSettlement
    }

    public CustomsRoleComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 350; // Below combat, above trade

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }
    public override void Tick() { }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler customs) return null;
        if (customs.IsDepowered) return null;

        var time = customs.Time;

        // If we have a destination, travel there
        if (_destination != null && _destination != customs.Location) {
            return CreateTravelEvent(customs, _destination, time);
        }

        // Decide what to do based on current action
        switch (_currentAction) {
            case CustomsPatrolAction.TravelingToCheckpoint:
            case CustomsPatrolAction.TravelingToSettlement:
                // We've arrived
                _currentAction = CustomsPatrolAction.PatrollingCheckpoint;
                _destination = null;
                return GetNextEvent(); // Re-evaluate

            case CustomsPatrolAction.PatrollingCheckpoint:
                // Done patrolling this location, pick next
                _currentAction = CustomsPatrolAction.Idle;
                break;
        }

        // Plan next patrol destination
        return PlanNextPatrol(customs, time);
    }

    ActorEvent? PlanNextPatrol(Crawler customs, TimePoint time) {
        // Find next patrol destination within faction territory
        var destination = FindPatrolDestination(customs);
        if (destination != null && destination != customs.Location) {
            _destination = destination;
            _currentAction = destination.Type == EncounterType.Settlement
                ? CustomsPatrolAction.TravelingToSettlement
                : CustomsPatrolAction.TravelingToCheckpoint;
            return CreateTravelEvent(customs, destination, time);
        }

        // At current location, patrol it
        if (IsPatrolLocation(customs.Location, customs.Faction)) {
            _currentAction = CustomsPatrolAction.PatrollingCheckpoint;
            int patrolPriority = EventPriority.ForCustomsPatrol(customs);
            return customs.NewEventFor("Patrolling checkpoint", patrolPriority,
                Tuning.CustomsPatrol.CheckpointDuration, Post: () => { });
        }

        // No good destinations, wait
        int priority = EventPriority.ForCustomsPatrol(customs);
        return customs.NewEventFor("Waiting for orders", priority,
            Tuning.CustomsPatrol.IdleWaitDuration, Post: () => { });
    }

    Location? FindPatrolDestination(Crawler customs) {
        var network = customs.Location.Map?.TradeNetwork;
        if (network == null) return null;

        var faction = customs.Faction;

        // Find settlements and crossroads in faction territory
        var candidates = network.Nodes
            .Where(loc => loc.Type is EncounterType.Settlement or EncounterType.Crossroads)
            .Where(loc => loc != customs.Location)
            .Where(loc => IsInFactionTerritory(loc, faction))
            .ToList();

        if (candidates.Count == 0) {
            // No faction territory, just patrol nearby locations
            candidates = network.RoadsFrom(customs.Location)
                .Select(r => r.To)
                .Where(loc => loc.Type is EncounterType.Settlement or EncounterType.Crossroads)
                .ToList();
        }

        if (candidates.Count == 0) return null;

        // Prefer locations we haven't visited recently (approximated by random selection)
        // Weight settlements higher than crossroads
        var weighted = candidates
            .Select(loc => new {
                Location = loc,
                Weight = loc.Type == EncounterType.Settlement ? 2.0f : 1.0f
            })
            .ToList();

        // Weighted random selection
        float totalWeight = weighted.Sum(x => x.Weight);
        float roll = _rng.NextSingle() * totalWeight;
        float cumulative = 0;
        foreach (var item in weighted) {
            cumulative += item.Weight;
            if (roll <= cumulative) {
                return item.Location;
            }
        }

        return candidates.First();
    }

    bool IsInFactionTerritory(Location location, Factions faction) {
        // Check if location is controlled by the faction
        return location.ControllingFaction == faction;
    }

    bool IsPatrolLocation(Location location, Factions faction) {
        // Settlements and crossroads in faction territory are patrol locations
        if (location.Type is not (EncounterType.Settlement or EncounterType.Crossroads)) {
            return false;
        }

        return IsInFactionTerritory(location, faction);
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
