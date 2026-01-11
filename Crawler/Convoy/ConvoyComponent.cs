using Crawler.Economy;
using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// Component that coordinates convoy movement.
/// Ensures all members travel together and handles waypoint progression.
/// </summary>
public class ConvoyComponent : ActorComponentBase {
    /// <summary>High priority - coordinate before most other AI.</summary>
    public override int Priority => 700;

    public override void Enter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
    }

    void OnActorArrived(IActor actor, TimePoint time) {
        if (actor != Owner) return;

        var convoy = ConvoyRegistry.GetConvoy(Owner);
        if (convoy == null) return;

        // Update convoy state when arriving at waypoint
        if (convoy.Leader == Owner && convoy.IsInTransit) {
            convoy.ArriveAtWaypoint();
        }
    }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;

        var convoy = ConvoyRegistry.GetConvoy(Owner);
        if (convoy == null) return null;

        // Different behavior for leader vs members
        if (convoy.Leader == Owner) {
            return LeaderBehavior(crawler, convoy);
        } else {
            return MemberBehavior(crawler, convoy);
        }
    }

    /// <summary>Leader behavior: wait for members, then advance route.</summary>
    ActorEvent? LeaderBehavior(Crawler leader, Convoy convoy) {
        // Check if route is complete
        if (convoy.HasArrived) {
            convoy.Dissolve();
            leader.Message($"Convoy arrived at destination. Convoy dissolved.");
            return null;
        }

        // If in transit, wait for arrival (handled by travel event)
        if (convoy.IsInTransit) {
            return null;
        }

        // Check if all members are present
        if (!convoy.AreAllMembersPresent()) {
            // Wait for stragglers
            var waited = leader.Time - convoy.DepartureTime;
            if (waited < Tuning.Convoy.MaxWaitForMembers) {
                // Priority based on convoy's combined cargo value
                float convoyCargoValue = convoy.AllParticipants
                    .Where(p => p is Crawler c)
                    .Sum(p => ((Crawler)p).Cargo.ValueAt(leader.Location));
                int priority = EventPriority.ForConvoy(leader, routeRisk: 0, convoyCargoValue);
                return leader.NewEventFor(
                    "Waiting for convoy",
                    priority,
                    Tuning.Convoy.WaypointWaitTime,
                    Post: () => {
                        // Check again after waiting
                        if (!convoy.AreAllMembersPresent()) {
                            leader.Message("Still waiting for convoy members...");
                        }
                    }
                );
            } else {
                // Waited too long - leave without them
                var missing = convoy.AllParticipants
                    .Where(p => p.Location != convoy.CurrentWaypoint)
                    .ToList();
                foreach (var straggler in missing) {
                    convoy.RemoveMember(straggler);
                    leader.Message($"{straggler.Name} left behind by convoy.");
                }
            }
        }

        // All present - advance to next waypoint
        return AdvanceToNextWaypoint(leader, convoy);
    }

    /// <summary>Member behavior: follow leader.</summary>
    ActorEvent? MemberBehavior(Crawler member, Convoy convoy) {
        // If we're not at the convoy's current waypoint, travel there
        if (convoy.CurrentWaypoint != null && member.Location != convoy.CurrentWaypoint) {
            var network = member.Location.Map.TradeNetwork;
            if (network != null) {
                var path = network.FindPath(member.Location, convoy.CurrentWaypoint);
                if (path != null && path.Count > 1) {
                    // Travel to catch up with convoy
                    var nextLoc = path[1];
                    var (fuel, hours) = member.FuelTimeTo(nextLoc);
                    if (fuel >= 0 && member.FuelInv >= fuel) {
                        return CreateTravelEvent(member, nextLoc);
                    }
                }
            }
        }

        // At convoy location - wait for leader to move
        // If convoy is in transit, we should also be in transit (synchronized)
        if (convoy.IsInTransit && convoy.NextWaypoint != null) {
            // Follow leader to next waypoint
            return CreateTravelEvent(member, convoy.NextWaypoint);
        }

        return null;
    }

    ActorEvent? AdvanceToNextWaypoint(Crawler leader, Convoy convoy) {
        var nextWaypoint = convoy.NextWaypoint;
        if (nextWaypoint == null) {
            // No more waypoints - arrived
            convoy.Dissolve();
            return null;
        }

        var network = leader.Location.Map.TradeNetwork;
        if (network == null) return null;

        // Find road to next waypoint
        var road = network.RoadsFrom(leader.Location)
            .FirstOrDefault(r => r.To == nextWaypoint);

        if (road == null) {
            leader.Message($"No road to {nextWaypoint.Description}. Convoy stuck.");
            return null;
        }

        // Check fuel for journey
        var (fuel, hours) = leader.FuelTimeTo(nextWaypoint);
        if (fuel < 0 || leader.FuelInv < fuel) {
            leader.Message("Not enough fuel for convoy to continue.");
            return null;
        }

        // Begin transit
        convoy.BeginTransit(road, leader.Time);
        convoy.DepartureTime = leader.Time;

        // Schedule departure for all convoy members
        leader.Message($"Convoy departing for {nextWaypoint.Description}.");

        return CreateTravelEvent(leader, nextWaypoint);
    }

    ActorEvent? CreateTravelEvent(Crawler crawler, Location destination) {
        // Find road to destination
        if (crawler.Location == null) return null;
        var network = crawler.Location.Map?.TradeNetwork;
        var road = network?.RoadsFrom(crawler.Location)
            .FirstOrDefault(r => r.To == destination);

        if (road == null) return null;

        var (fuel, hours) = crawler.FuelTimeTo(destination);
        if (fuel < 0 || crawler.FuelInv < fuel) {
            return null;
        }

        // Use step-based travel for contact detection
        crawler.TravelViaRoad(road);
        return null; // TravelViaRoad schedules its own events
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        var convoy = ConvoyRegistry.GetConvoy(Owner);
        if (convoy == null) yield break;

        // If owner is leader, provide convoy management interactions
        if (convoy.Leader == Owner) {
            // Allow subject to request joining
            if (!ConvoyRegistry.IsInConvoy(subject)) {
                yield return new RequestJoinConvoyInteraction(subject, Owner, convoy, "JC");
            }
        }
    }
}

/// <summary>
/// Interaction for a crawler to request joining another's convoy.
/// </summary>
public record RequestJoinConvoyInteraction(
    IActor Mechanic,  // The one requesting to join
    IActor Subject,   // The convoy leader
    Convoy Convoy,
    string MenuOption
) : Interaction(Mechanic, Subject, MenuOption) {

    public override string Description =>
        $"Request to join {Subject.Name}'s convoy to {Convoy.Destination?.Description ?? "unknown"}";

    public override Immediacy GetImmediacy(string args = "") {
        // Check relation - need positive relation to join
        var relation = Subject.To(Mechanic);
        if (relation.Hostile) return Immediacy.Failed;

        // Check if already in a convoy
        if (ConvoyRegistry.IsInConvoy(Mechanic)) return Immediacy.Failed;

        return Immediacy.Menu;
    }

    public override bool Perform(string args = "") {
        // Check relation
        var relation = Subject.To(Mechanic);
        if (relation.Hostile) {
            Mechanic.Message($"{Subject.Name} refuses - hostile relations.");
            return false;
        }

        // Check minimum relation
        // (For now, just check not hostile - could add reputation check)

        // Add to convoy
        Convoy.AddMember(Mechanic);
        Mechanic.Message($"Joined {Subject.Name}'s convoy to {Convoy.Destination?.Description}.");
        Subject.Message($"{Mechanic.Name} joined the convoy.");

        return true;
    }

    public override TimeDuration ExpectedDuration => Tuning.Convoy.JoinConvoyTime;
}

/// <summary>
/// Interaction for leaving a convoy.
/// </summary>
public record LeaveConvoyInteraction(
    IActor Mechanic,
    string MenuOption
) : Interaction(Mechanic, Mechanic, MenuOption) {

    public override string Description => "Leave convoy";

    public override Immediacy GetImmediacy(string args = "") {
        var convoy = ConvoyRegistry.GetConvoy(Mechanic);
        if (convoy == null) return Immediacy.Failed;

        // Can only leave at a location, not in transit
        if (convoy.IsInTransit) return Immediacy.Failed;

        return Immediacy.Menu;
    }

    public override bool Perform(string args = "") {
        var convoy = ConvoyRegistry.GetConvoy(Mechanic);
        if (convoy == null) return false;

        convoy.RemoveMember(Mechanic);
        Mechanic.Message("Left the convoy.");

        return true;
    }
}
