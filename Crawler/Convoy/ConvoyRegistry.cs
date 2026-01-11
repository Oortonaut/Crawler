using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// Global registry for active convoys with O(1) lookups by actor, location, and road.
/// Settlements use this to efficiently find convoys forming at their location.
/// </summary>
public static class ConvoyRegistry {
    // Global convoy storage
    static readonly Dictionary<ulong, Convoy> _convoys = new();

    // Actor -> Convoy mapping for O(1) lookup
    static readonly Dictionary<IActor, Convoy> _actorToConvoy = new();
    static readonly Dictionary<IActor, ConvoyRole> _actorToRole = new();

    // Location-based clearing for settlement convoy boards
    static readonly Dictionary<Location, HashSet<Convoy>> _convoysByLocation = new();

    // Road-based lookup for in-transit convoys
    static readonly Dictionary<Road, HashSet<Convoy>> _convoysByRoad = new();

    /// <summary>Get all registered convoys.</summary>
    public static IEnumerable<Convoy> AllConvoys => _convoys.Values;

    /// <summary>Get convoy count.</summary>
    public static int Count => _convoys.Count;

    /// <summary>Get the convoy an actor belongs to (if any).</summary>
    public static Convoy? GetConvoy(IActor actor) =>
        _actorToConvoy.TryGetValue(actor, out var convoy) ? convoy : null;

    /// <summary>Get an actor's role in their convoy.</summary>
    public static ConvoyRole GetRole(IActor actor) =>
        _actorToRole.TryGetValue(actor, out var role) ? role : ConvoyRole.None;

    /// <summary>Check if an actor is in a convoy.</summary>
    public static bool IsInConvoy(IActor actor) => _actorToConvoy.ContainsKey(actor);

    /// <summary>Check if an actor is leading a convoy.</summary>
    public static bool IsLeader(IActor actor) => GetRole(actor) == ConvoyRole.Leader;

    /// <summary>Get all convoys currently at a location (forming or waiting).</summary>
    public static IEnumerable<Convoy> ConvoysAt(Location location) =>
        _convoysByLocation.TryGetValue(location, out var set) ? set : [];

    /// <summary>Get all convoys currently traveling on a road.</summary>
    public static IEnumerable<Convoy> ConvoysOnRoad(Road road) =>
        _convoysByRoad.TryGetValue(road, out var set) ? set : [];

    /// <summary>Get all convoys heading to a destination.</summary>
    public static IEnumerable<Convoy> ConvoysToDestination(Location destination) =>
        _convoys.Values.Where(c => c.Destination == destination);

    /// <summary>Get all convoys whose route passes through a location.</summary>
    public static IEnumerable<Convoy> ConvoysPassingThrough(Location location) =>
        _convoys.Values.Where(c => c.Route.Contains(location));

    /// <summary>
    /// Create a new convoy with the given leader and route.
    /// </summary>
    public static Convoy Create(IActor leader, List<Location> route) {
        // Remove leader from any existing convoy
        var existing = GetConvoy(leader);
        existing?.RemoveMember(leader);

        var convoy = new Convoy(leader, route);
        _convoys[convoy.Id] = convoy;
        SetActorConvoy(leader, convoy, ConvoyRole.Leader);
        UpdateConvoyLocation(convoy);

        return convoy;
    }

    /// <summary>
    /// Unregister a convoy from all lookups.
    /// </summary>
    public static void Unregister(Convoy convoy) {
        _convoys.Remove(convoy.Id);

        // Remove from location index
        foreach (var (_, set) in _convoysByLocation) {
            set.Remove(convoy);
        }

        // Remove from road index
        foreach (var (_, set) in _convoysByRoad) {
            set.Remove(convoy);
        }
    }

    /// <summary>
    /// Set an actor's convoy membership.
    /// </summary>
    internal static void SetActorConvoy(IActor actor, Convoy convoy, ConvoyRole role) {
        _actorToConvoy[actor] = convoy;
        _actorToRole[actor] = role;
    }

    /// <summary>
    /// Clear an actor's convoy membership.
    /// </summary>
    internal static void ClearActorConvoy(IActor actor) {
        _actorToConvoy.Remove(actor);
        _actorToRole.Remove(actor);
    }

    /// <summary>
    /// Update convoy location indices when convoy moves.
    /// </summary>
    internal static void UpdateConvoyLocation(Convoy convoy) {
        // Remove from all location sets
        foreach (var (_, set) in _convoysByLocation) {
            set.Remove(convoy);
        }

        // Remove from all road sets
        foreach (var (_, set) in _convoysByRoad) {
            set.Remove(convoy);
        }

        if (convoy.IsInTransit && convoy.CurrentRoad != null) {
            // Add to road index
            if (!_convoysByRoad.ContainsKey(convoy.CurrentRoad)) {
                _convoysByRoad[convoy.CurrentRoad] = [];
            }
            _convoysByRoad[convoy.CurrentRoad].Add(convoy);
        } else if (convoy.CurrentWaypoint != null) {
            // Add to location index
            if (!_convoysByLocation.ContainsKey(convoy.CurrentWaypoint)) {
                _convoysByLocation[convoy.CurrentWaypoint] = [];
            }
            _convoysByLocation[convoy.CurrentWaypoint].Add(convoy);
        }
    }

    /// <summary>
    /// Clear all convoy data. Used for new game or testing.
    /// </summary>
    public static void Clear() {
        _convoys.Clear();
        _actorToConvoy.Clear();
        _actorToRole.Clear();
        _convoysByLocation.Clear();
        _convoysByRoad.Clear();
    }

    /// <summary>
    /// Get summary of convoy registry state for debugging.
    /// </summary>
    public static string GetDebugInfo() =>
        $"Convoys: {_convoys.Count}, Actors in convoys: {_actorToConvoy.Count}, " +
        $"Locations with convoys: {_convoysByLocation.Count(kv => kv.Value.Count > 0)}, " +
        $"Roads with convoys: {_convoysByRoad.Count(kv => kv.Value.Count > 0)}";
}
