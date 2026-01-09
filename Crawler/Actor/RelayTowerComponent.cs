namespace Crawler;

using Economy;

/// <summary>
/// Component that manages price information relay at crossroads.
/// When actors arrive, their price knowledge is merged with the relay's knowledge,
/// and they receive updated information in return.
/// </summary>
public class RelayTowerComponent : ActorComponentBase {
    /// <summary>Accumulated price knowledge from all visitors.</summary>
    public PriceKnowledge RelayKnowledge { get; } = new();

    /// <summary>Time of last knowledge propagation to connected relays.</summary>
    TimePoint _lastPropagation = TimePoint.Zero;

    /// <summary>Interval between propagation attempts.</summary>
    static readonly TimeDuration PropagationInterval = TimeDuration.FromHours(6);

    public override void Enter(Encounter encounter) {
        // Update local prices when we enter an encounter
        if (Owner?.Location != null) {
            RelayKnowledge.UpdatePrice(Owner.Location, encounter.EncounterTime);
        }
    }

    public override void Leave(Encounter encounter) { }

    public override void Tick() {
        if (Owner?.Location == null) return;
        var encounter = Owner.Location.GetEncounter();
        var currentTime = encounter.EncounterTime;

        // Periodically propagate to connected relays via trade network
        if (currentTime - _lastPropagation >= PropagationInterval) {
            PropagateToConnectedRelays(currentTime);
            _lastPropagation = currentTime;
        }
    }

    /// <summary>
    /// Called when another actor arrives at this location.
    /// Exchanges price knowledge with the visitor.
    /// </summary>
    public void OnVisitorArrived(IActor visitor, TimePoint time) {
        // Get visitor's price knowledge if they have one
        var visitorKnowledge = GetActorKnowledge(visitor);
        if (visitorKnowledge != null) {
            // Merge visitor's knowledge into relay
            RelayKnowledge.MergeKnowledge(visitorKnowledge);

            // Share relay's knowledge with visitor
            visitorKnowledge.MergeKnowledge(RelayKnowledge);
        }

        // Update local prices
        if (Owner?.Location != null) {
            RelayKnowledge.UpdatePrice(Owner.Location, time);
        }
    }

    /// <summary>
    /// Propagate knowledge to other relay towers via the trade network.
    /// </summary>
    void PropagateToConnectedRelays(TimePoint time) {
        var map = Owner?.Location?.Map;
        var network = map?.TradeNetwork;
        if (network == null || Owner?.Location == null) return;

        // Find connected crossroads locations
        foreach (var road in network.RoadsFrom(Owner.Location)) {
            if (road.To.Type != EncounterType.Crossroads) continue;
            if (!road.To.HasEncounter) continue;

            var encounter = road.To.GetEncounter();
            var relayTower = encounter.Actors
                .OfType<ActorBase>()
                .SelectMany(a => a.Components.OfType<RelayTowerComponent>())
                .FirstOrDefault();

            if (relayTower != null) {
                // Share knowledge (simulating radio/signal propagation with delay)
                // The propagation delay is based on distance
                var delay = TimeDuration.FromHours(road.Distance / 500); // 500 km/hr signal speed

                // Only share if our info is newer
                foreach (var (location, snapshot) in RelayKnowledge.Snapshots) {
                    var theirSnapshot = relayTower.RelayKnowledge.GetSnapshot(location);
                    if (theirSnapshot == null || snapshot.Timestamp > theirSnapshot.Timestamp + delay) {
                        // Create delayed snapshot
                        var delayedSnapshot = snapshot with { Timestamp = snapshot.Timestamp - delay };
                        relayTower.RelayKnowledge.UpdatePrice(delayedSnapshot);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get the price knowledge from an actor, if it has one.
    /// </summary>
    static PriceKnowledge? GetActorKnowledge(IActor actor) {
        // Check for TraderAIComponent or player crawler
        if (actor is Crawler crawler) {
            var trader = crawler.Components.OfType<TraderKnowledgeComponent>().FirstOrDefault();
            return trader?.Knowledge;
        }
        return null;
    }
}

/// <summary>
/// Component that provides price knowledge storage for crawlers.
/// Used by both player and NPC traders to track market information.
/// </summary>
public class TraderKnowledgeComponent : ActorComponentBase {
    /// <summary>This crawler's price knowledge.</summary>
    public PriceKnowledge Knowledge { get; } = new();

    public override void Enter(Encounter encounter) {
        // Update prices for this location
        if (Owner?.Location != null) {
            Knowledge.UpdatePrice(Owner.Location, encounter.EncounterTime);
        }

        // Exchange with relay tower if at crossroads
        if (Owner?.Location?.Type == EncounterType.Crossroads) {
            var relayComponent = encounter.Actors
                .OfType<ActorBase>()
                .SelectMany(a => a.Components.OfType<RelayTowerComponent>())
                .FirstOrDefault();

            relayComponent?.OnVisitorArrived(Owner, encounter.EncounterTime);
        }
    }

    public override void Leave(Encounter encounter) { }

    public override void Tick() { }
}
