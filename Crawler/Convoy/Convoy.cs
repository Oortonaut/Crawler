using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// Role within a convoy.
/// </summary>
public enum ConvoyRole {
    None,       // Not in a convoy
    Leader,     // Controls route and pace
    Member,     // Following the leader
    Guard,      // Hired escort (special member)
}

/// <summary>
/// Represents a group of crawlers traveling together along a trade route.
/// Convoys provide safety in numbers against bandits and share route risk information.
/// </summary>
public class Convoy {
    static ulong _nextId = 1;

    public Convoy(IActor leader, List<Location> route) {
        Id = _nextId++;
        Leader = leader;
        Route = route;
        CurrentWaypointIndex = 0;
        FormationTime = leader.Time;
    }

    /// <summary>Unique identifier for this convoy.</summary>
    public ulong Id { get; }

    /// <summary>The crawler leading this convoy (controls route and pace).</summary>
    public IActor Leader { get; private set; }

    /// <summary>Members of the convoy (excluding leader).</summary>
    public List<IActor> Members { get; } = [];

    /// <summary>Guards hired for this convoy (subset of members with Guard role).</summary>
    public IEnumerable<IActor> Guards => Members.Where(m =>
        ConvoyRegistry.GetRole(m) == ConvoyRole.Guard);

    /// <summary>All participants (leader + members).</summary>
    public IEnumerable<IActor> AllParticipants => Members.Prepend(Leader);

    /// <summary>The planned route as a list of locations.</summary>
    public List<Location> Route { get; set; }

    /// <summary>Index of current waypoint in the route (0 = origin).</summary>
    public int CurrentWaypointIndex { get; set; }

    /// <summary>The road currently being traveled on (null if at a location).</summary>
    public Road? CurrentRoad { get; private set; }

    /// <summary>Progress along current road (0.0 = at From, 1.0 = at To).</summary>
    public float Progress { get; set; }

    /// <summary>Time when the convoy was formed.</summary>
    public TimePoint FormationTime { get; }

    /// <summary>Time when the convoy departed current waypoint (for progress calculation).</summary>
    public TimePoint DepartureTime { get; set; }

    // Computed properties
    public Location? Origin => Route.Count > 0 ? Route[0] : null;
    public Location? Destination => Route.Count > 0 ? Route[^1] : null;
    public Location? CurrentWaypoint => CurrentWaypointIndex >= 0 && CurrentWaypointIndex < Route.Count
        ? Route[CurrentWaypointIndex] : null;
    public Location? NextWaypoint => CurrentWaypointIndex >= 0 && CurrentWaypointIndex < Route.Count - 1
        ? Route[CurrentWaypointIndex + 1] : null;

    /// <summary>Whether the convoy is currently traveling between locations.</summary>
    public bool IsInTransit => CurrentRoad != null;

    /// <summary>Whether the convoy has reached its final destination.</summary>
    public bool HasArrived => CurrentWaypointIndex >= Route.Count - 1 && !IsInTransit;

    /// <summary>Combined firepower of all convoy members.</summary>
    public float CombinedFirepower => AllParticipants
        .OfType<Crawler>()
        .Sum(c => c.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage));

    /// <summary>Combined defense of all convoy members.</summary>
    public float CombinedDefense => AllParticipants
        .OfType<Crawler>()
        .Sum(c => c.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction));

    /// <summary>Total number of participants.</summary>
    public int Size => Members.Count + 1;

    /// <summary>Add a member to the convoy.</summary>
    public void AddMember(IActor actor, ConvoyRole role = ConvoyRole.Member) {
        if (actor == Leader) return;
        if (Members.Contains(actor)) return;

        // Remove from any existing convoy first
        var existingConvoy = ConvoyRegistry.GetConvoy(actor);
        existingConvoy?.RemoveMember(actor);

        Members.Add(actor);
        ConvoyRegistry.SetActorConvoy(actor, this, role);
    }

    /// <summary>Remove a member from the convoy.</summary>
    public void RemoveMember(IActor actor) {
        if (actor == Leader) {
            // If leader leaves, transfer leadership or dissolve
            if (Members.Count > 0) {
                TransferLeadership(Members[0]);
            } else {
                Dissolve();
            }
            return;
        }

        Members.Remove(actor);
        ConvoyRegistry.ClearActorConvoy(actor);

        // Dissolve if convoy becomes empty
        if (Members.Count == 0) {
            Dissolve();
        }
    }

    /// <summary>Transfer leadership to another member.</summary>
    public void TransferLeadership(IActor newLeader) {
        if (!Members.Contains(newLeader) && newLeader != Leader) return;

        var oldLeader = Leader;
        Members.Remove(newLeader);

        if (oldLeader != newLeader) {
            Members.Add(oldLeader);
            ConvoyRegistry.SetActorConvoy(oldLeader, this, ConvoyRole.Member);
        }

        Leader = newLeader;
        ConvoyRegistry.SetActorConvoy(newLeader, this, ConvoyRole.Leader);
    }

    /// <summary>Begin traveling to the next waypoint.</summary>
    public void BeginTransit(Road road, TimePoint departureTime) {
        CurrentRoad = road;
        Progress = 0;
        DepartureTime = departureTime;
        ConvoyRegistry.UpdateConvoyLocation(this);
    }

    /// <summary>Arrive at the next waypoint.</summary>
    public void ArriveAtWaypoint() {
        CurrentRoad = null;
        Progress = 0;
        CurrentWaypointIndex++;
        ConvoyRegistry.UpdateConvoyLocation(this);
    }

    /// <summary>Dissolve the convoy, removing all members.</summary>
    public void Dissolve() {
        foreach (var member in Members.ToList()) {
            ConvoyRegistry.ClearActorConvoy(member);
        }
        Members.Clear();

        ConvoyRegistry.ClearActorConvoy(Leader);
        ConvoyRegistry.Unregister(this);
    }

    /// <summary>Check if all members are present at the current waypoint.</summary>
    public bool AreAllMembersPresent() {
        var waypoint = CurrentWaypoint;
        if (waypoint == null) return false;

        return AllParticipants.All(p => p.Location == waypoint);
    }

    public override string ToString() =>
        $"Convoy #{Id}: {Leader.Name} + {Members.Count} members, {Origin?.Description} -> {Destination?.Description}";
}
