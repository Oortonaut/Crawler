using System.Collections.Concurrent;
using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// State of an actor in transit on a road.
/// </summary>
public class TransitState {
    public IActor Actor { get; }
    public Road Road { get; }
    public float Progress { get; set; }      // 0.0 to 1.0 parametric position
    public int Direction { get; }            // +1 forward (From->To), -1 backward
    public TimePoint DepartureTime { get; }
    public float Speed { get; }              // km/h

    public TransitState(IActor actor, Road road, float progress, int direction, TimePoint departureTime, float speed) {
        Actor = actor;
        Road = road;
        Progress = progress;
        Direction = direction;
        DepartureTime = departureTime;
        Speed = speed;
    }

    /// <summary>Calculate distance traveled from departure to current time.</summary>
    public float DistanceTraveled(TimePoint currentTime) {
        var elapsed = currentTime - DepartureTime;
        return Speed * (float)elapsed.TotalHours * Direction;
    }

    /// <summary>Calculate progress from departure to current time.</summary>
    public float ProgressAt(TimePoint currentTime) {
        float distance = DistanceTraveled(currentTime);
        return Math.Clamp(Progress + distance / Road.Distance, 0, 1);
    }
}

/// <summary>
/// Global registry tracking all actors currently traveling on roads.
/// Enables O(1) lookup by actor and efficient lookup by road for contact detection.
/// Thread-safe for parallel simulation processing.
/// </summary>
public static class TransitRegistry {
    static readonly ConcurrentDictionary<IActor, TransitState> _actorTransit = new();
    static readonly ConcurrentDictionary<Road, List<TransitState>> _roadActors = new();
    static readonly object _roadActorsLock = new();

    /// <summary>Get transit state for an actor, or null if not in transit.</summary>
    public static TransitState? GetTransit(IActor actor) =>
        _actorTransit.TryGetValue(actor, out var state) ? state : null;

    /// <summary>Check if an actor is currently in transit.</summary>
    public static bool IsInTransit(IActor actor) => _actorTransit.ContainsKey(actor);

    /// <summary>Begin transit for an actor on a road.</summary>
    public static void BeginTransit(IActor actor, Road road, float speed, TimePoint departureTime, int direction = 1) {
        // Remove from any existing transit first
        EndTransit(actor);

        var state = new TransitState(actor, road, 0, direction, departureTime, speed);
        _actorTransit[actor] = state;

        lock (_roadActorsLock) {
            var list = _roadActors.GetOrAdd(road, _ => []);
            list.Add(state);
        }
    }

    /// <summary>End transit for an actor.</summary>
    public static void EndTransit(IActor actor) {
        if (_actorTransit.TryRemove(actor, out var state)) {
            lock (_roadActorsLock) {
                if (_roadActors.TryGetValue(state.Road, out var list)) {
                    list.Remove(state);
                    if (list.Count == 0) {
                        _roadActors.TryRemove(state.Road, out _);
                    }
                }
            }
        }
    }

    /// <summary>Update progress for an actor in transit.</summary>
    public static void UpdateProgress(IActor actor, float progress) {
        if (_actorTransit.TryGetValue(actor, out var state)) {
            state.Progress = Math.Clamp(progress, 0, 1);
        }
    }

    /// <summary>Get all actors currently on a road (snapshot for iteration).</summary>
    public static IEnumerable<TransitState> ActorsOnRoad(Road road) {
        lock (_roadActorsLock) {
            return _roadActors.TryGetValue(road, out var list) ? list.ToList() : [];
        }
    }

    /// <summary>Get all roads with actors in transit (snapshot for iteration).</summary>
    public static IEnumerable<Road> ActiveRoads {
        get {
            lock (_roadActorsLock) {
                return _roadActors.Keys.ToList();
            }
        }
    }

    /// <summary>Get count of actors in transit.</summary>
    public static int TransitCount => _actorTransit.Count;

    /// <summary>Clear all transit state (for save/load).</summary>
    public static void Clear() {
        lock (_roadActorsLock) {
            _actorTransit.Clear();
            _roadActors.Clear();
        }
    }
}
