namespace Crawler.Network;

using System.Numerics;

/// <summary>
/// Represents a road connection between two locations.
/// </summary>
public record Road(
    Location From,
    Location To,
    float Distance,
    float Difficulty,
    TerrainType WorstTerrain
) {
    /// <summary>Travel time in hours based on distance and difficulty.</summary>
    public float TravelTime => Distance * Difficulty / 100f; // Base 100 km/hr adjusted by difficulty
}

/// <summary>
/// Manages the road network connecting settlements and crossroads.
/// Enables pathfinding and trade route planning.
/// </summary>
public class TradeNetwork {
    readonly Dictionary<Location, List<Road>> _adjacency = new();
    readonly List<Road> _allRoads = [];

    /// <summary>All roads in the network.</summary>
    public IReadOnlyList<Road> AllRoads => _allRoads;

    /// <summary>All locations that are part of the road network.</summary>
    public IEnumerable<Location> Nodes => _adjacency.Keys;

    /// <summary>
    /// Add a bidirectional road between two locations.
    /// </summary>
    public void AddRoad(Location from, Location to, float difficulty, TerrainType worstTerrain) {
        float distance = from.Distance(to);

        var roadForward = new Road(from, to, distance, difficulty, worstTerrain);
        var roadBackward = new Road(to, from, distance, difficulty, worstTerrain);

        if (!_adjacency.ContainsKey(from)) _adjacency[from] = [];
        if (!_adjacency.ContainsKey(to)) _adjacency[to] = [];

        _adjacency[from].Add(roadForward);
        _adjacency[to].Add(roadBackward);

        _allRoads.Add(roadForward);
    }

    /// <summary>
    /// Get all roads from a location.
    /// </summary>
    public IEnumerable<Road> RoadsFrom(Location loc) {
        return _adjacency.TryGetValue(loc, out var roads) ? roads : [];
    }

    /// <summary>
    /// Check if two locations are directly connected.
    /// </summary>
    public bool AreConnected(Location a, Location b) {
        return RoadsFrom(a).Any(r => r.To == b);
    }

    /// <summary>
    /// Find the shortest path between two locations using Dijkstra's algorithm.
    /// Returns null if no path exists.
    /// </summary>
    /// <param name="from">Starting location</param>
    /// <param name="to">Destination location</param>
    /// <param name="maxTerrain">Maximum terrain difficulty the traveler can handle</param>
    public List<Location>? FindPath(Location from, Location to, TerrainType maxTerrain = TerrainType.Ruined) {
        if (!_adjacency.ContainsKey(from) || !_adjacency.ContainsKey(to)) {
            return null;
        }

        var distances = new Dictionary<Location, float>();
        var previous = new Dictionary<Location, Location>();
        var unvisited = new PriorityQueue<Location, float>();

        foreach (var node in _adjacency.Keys) {
            distances[node] = float.MaxValue;
        }
        distances[from] = 0;
        unvisited.Enqueue(from, 0);

        while (unvisited.Count > 0) {
            var current = unvisited.Dequeue();

            if (current == to) {
                // Reconstruct path
                var path = new List<Location>();
                var node = to;
                while (node != from) {
                    path.Add(node);
                    node = previous[node];
                }
                path.Add(from);
                path.Reverse();
                return path;
            }

            foreach (var road in RoadsFrom(current)) {
                // Skip roads with terrain too difficult
                if (road.WorstTerrain > maxTerrain) continue;

                var alt = distances[current] + road.TravelTime;
                if (alt < distances[road.To]) {
                    distances[road.To] = alt;
                    previous[road.To] = current;
                    unvisited.Enqueue(road.To, alt);
                }
            }
        }

        return null; // No path found
    }

    /// <summary>
    /// Calculate total travel time along a path.
    /// </summary>
    public float PathTravelTime(List<Location> path) {
        float total = 0;
        for (int i = 0; i < path.Count - 1; i++) {
            var road = RoadsFrom(path[i]).FirstOrDefault(r => r.To == path[i + 1]);
            if (road != null) {
                total += road.TravelTime;
            }
        }
        return total;
    }

    /// <summary>
    /// Get all locations reachable from a starting point within a given terrain limit.
    /// </summary>
    public IEnumerable<Location> ReachableFrom(Location start, TerrainType maxTerrain = TerrainType.Ruined) {
        var visited = new HashSet<Location>();
        var queue = new Queue<Location>();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0) {
            var current = queue.Dequeue();
            yield return current;

            foreach (var road in RoadsFrom(current)) {
                if (road.WorstTerrain <= maxTerrain && !visited.Contains(road.To)) {
                    visited.Add(road.To);
                    queue.Enqueue(road.To);
                }
            }
        }
    }

    /// <summary>
    /// Generate the trade network by connecting settlements and crossroads.
    /// </summary>
    public static TradeNetwork Generate(Map map, XorShift rng) {
        var network = new TradeNetwork();

        // Collect all trade-relevant locations (settlements and crossroads)
        var tradeLocations = new List<Location>();
        foreach (var sector in map.AllSectors) {
            tradeLocations.AddRange(sector.Locations
                .Where(loc => loc.Type is EncounterType.Settlement or EncounterType.Crossroads));
        }

        if (tradeLocations.Count < 2) return network;

        // Use Delaunay-like approach: connect each location to nearest neighbors
        // then add some random long-distance connections
        foreach (var loc in tradeLocations) {
            // Find nearest neighbors (3-5 connections)
            var neighbors = tradeLocations
                .Where(other => other != loc)
                .OrderBy(other => loc.Distance(other))
                .Take(rng.NextInt(3, 6))
                .ToList();

            foreach (var neighbor in neighbors) {
                if (!network.AreConnected(loc, neighbor)) {
                    var (difficulty, worstTerrain) = CalculateRoadDifficulty(loc, neighbor, map);
                    network.AddRoad(loc, neighbor, difficulty, worstTerrain);
                }
            }
        }

        // Add some random long-distance connections for network resilience
        int extraConnections = tradeLocations.Count / 5;
        for (int i = 0; i < extraConnections; i++) {
            var a = rng.ChooseRandom(tradeLocations);
            var b = rng.ChooseRandom(tradeLocations);
            if (a != b && !network.AreConnected(a, b)) {
                var (difficulty, worstTerrain) = CalculateRoadDifficulty(a, b, map);
                network.AddRoad(a, b, difficulty, worstTerrain);
            }
        }

        return network;
    }

    /// <summary>
    /// Calculate road difficulty based on terrain along the path.
    /// </summary>
    static (float difficulty, TerrainType worstTerrain) CalculateRoadDifficulty(Location from, Location to, Map map) {
        // Sample terrain along the path
        var fromTerrain = from.Terrain;
        var toTerrain = to.Terrain;
        var worstTerrain = (TerrainType)Math.Max((int)fromTerrain, (int)toTerrain);

        // Difficulty multiplier based on worst terrain
        float difficulty = worstTerrain switch {
            TerrainType.Flat => 1.0f,
            TerrainType.Rough => 1.3f,
            TerrainType.Broken => 1.7f,
            TerrainType.Shattered => 2.2f,
            TerrainType.Ruined => 3.0f,
            _ => 1.0f
        };

        return (difficulty, worstTerrain);
    }
}
