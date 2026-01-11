using System.Numerics;
using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// Factory and registry for transit encounters - ephemeral encounters that occur
/// when actors meet on a road between locations.
/// </summary>
public static class TransitEncounterFactory {
    static readonly Dictionary<(Road, float), Encounter> _activeEncounters = new();

    /// <summary>
    /// Create or get an existing transit encounter at a position on a road.
    /// </summary>
    public static Encounter GetOrCreate(ulong seed, Road road, float progress, TimePoint time) {
        // Round progress to avoid floating point issues
        float roundedProgress = MathF.Round(progress, 3);
        var key = (road, roundedProgress);

        if (_activeEncounters.TryGetValue(key, out var existing)) {
            return existing;
        }

        var transitLocation = CreateTransitLocation(seed, road, progress);
        var encounter = new Encounter(seed, transitLocation);
        encounter.Name = $"Road ({road.From.PosString} - {road.To.PosString})";

        _activeEncounters[key] = encounter;
        return encounter;
    }

    /// <summary>
    /// Create a location that represents a position along a road.
    /// </summary>
    static Location CreateTransitLocation(ulong seed, Road road, float progress) {
        // Interpolate position along the road
        var from = road.From.Position;
        var to = road.To.Position;
        var position = Vector2.Lerp(from, to, progress);

        // Use same map as From location
        var map = road.From.Map;

        // Interpolate wealth between endpoints
        float wealth = road.From.Wealth * (1 - progress) + road.To.Wealth * progress;
        wealth = Math.Max(1, wealth);

        // Interpolate faction control
        var controllingFaction = road.From.ControllingFaction;

        // Create location that cannot spawn encounters
        return new Location(
            Seed: seed,
            Map: map,
            Position: position,
            Type: EncounterType.None,
            Wealth: wealth,
            NewEncounter: _ => throw new InvalidOperationException("Transit location cannot spawn encounters")
        ) {
            TransitRoad = road,
            TransitProgress = progress,
            ControllingFaction = controllingFaction
        };
    }

    /// <summary>
    /// Remove a transit encounter when all actors have left.
    /// </summary>
    public static void TryRemove(Encounter encounter) {
        var toRemove = _activeEncounters
            .Where(kvp => kvp.Value == encounter && kvp.Value.Actors.Count == 0)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove) {
            _activeEncounters.Remove(key);
        }
    }

    /// <summary>
    /// Check if a location is a transit location.
    /// </summary>
    public static bool IsTransitLocation(Location location) =>
        location.TransitRoad != null;

    /// <summary>
    /// Get the road and progress for a transit location.
    /// </summary>
    public static (Road Road, float Progress)? GetTransitInfo(Location location) {
        if (location.TransitRoad is Road road && location.TransitProgress is float progress) {
            return (road, progress);
        }
        return null;
    }

    /// <summary>
    /// Clear all transit encounters (for save/load).
    /// </summary>
    public static void Clear() {
        _activeEncounters.Clear();
    }

    /// <summary>
    /// Get count of active transit encounters.
    /// </summary>
    public static int ActiveCount => _activeEncounters.Count;
}
