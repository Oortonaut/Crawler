using Crawler.Network;

namespace Crawler.Economy;

/// <summary>
/// Types of dangerous events that can occur on roads.
/// </summary>
public enum RiskEventType {
    BanditAttack,      // Direct bandit assault
    BanditExtortion,   // Bandit demand/shakedown
    CustomsSeizure,    // Contraband confiscated
    CrawlerCombat,     // General combat between crawlers
    HazardDamage,      // Environmental hazard damage
    ConvoyAmbush,      // Coordinated attack on convoy
}

/// <summary>
/// Record of a dangerous event that occurred on a road.
/// </summary>
public record RiskEvent(
    Road Road,
    TimePoint Timestamp,
    RiskEventType Type,
    Factions? AttackerFaction,
    float Severity  // 0-1 scale: 0 = minor incident, 1 = catastrophic
) {
    /// <summary>Whether the victim survived the event.</summary>
    public bool Survived { get; init; } = true;

    /// <summary>Damage dealt during the event.</summary>
    public float DamageDealt { get; init; } = 0;

    /// <summary>Value lost (cargo, scrap paid in extortion, etc.).</summary>
    public float ValueLost { get; init; } = 0;

    /// <summary>Base risk contribution of this event type.</summary>
    public float BaseRisk => Type switch {
        RiskEventType.BanditAttack => 0.5f,
        RiskEventType.BanditExtortion => 0.3f,
        RiskEventType.CustomsSeizure => 0.1f,
        RiskEventType.CrawlerCombat => 0.4f,
        RiskEventType.HazardDamage => 0.2f,
        RiskEventType.ConvoyAmbush => 0.6f,
        _ => 0.1f
    };
}

/// <summary>
/// Tracks danger information about roads and routes.
/// Similar to PriceKnowledge, this can be shared between actors and factions.
/// </summary>
public class RouteRiskTracker {
    readonly Dictionary<Road, List<RiskEvent>> _events = new();
    readonly Dictionary<Road, (float Risk, TimePoint Calculated)> _cachedRisk = new();

    /// <summary>Maximum age of risk events to consider (default 7 days).</summary>
    public static TimeDuration MaxEventAge => TimeDuration.FromDays(7);

    /// <summary>Cache invalidation time (recalculate risk after this duration).</summary>
    public static TimeDuration CacheExpiry => TimeDuration.FromHours(1);

    /// <summary>All roads with recorded events.</summary>
    public IEnumerable<Road> KnownRoads => _events.Keys;

    /// <summary>Total number of recorded events.</summary>
    public int EventCount => _events.Values.Sum(list => list.Count);

    /// <summary>Record a risk event on a road.</summary>
    public void RecordEvent(RiskEvent evt) {
        if (!_events.ContainsKey(evt.Road)) {
            _events[evt.Road] = [];
        }
        _events[evt.Road].Add(evt);

        // Invalidate cached risk for this road
        _cachedRisk.Remove(evt.Road);
    }

    /// <summary>Get the risk score for a road (0 = safe, higher = more dangerous).</summary>
    public float GetRoadRisk(Road road, TimePoint currentTime) {
        // Check cache
        if (_cachedRisk.TryGetValue(road, out var cached)) {
            if (currentTime - cached.Calculated < CacheExpiry) {
                return cached.Risk;
            }
        }

        // Base risk from terrain difficulty
        float risk = road.Difficulty * 0.1f;

        // Add risk from recorded events
        if (_events.TryGetValue(road, out var events)) {
            foreach (var evt in events) {
                var age = currentTime - evt.Timestamp;
                if (age > MaxEventAge) continue;

                // Decay risk over time (recent events matter more)
                float ageFactor = 1.0f - (float)(age.TotalHours / MaxEventAge.TotalHours);

                // Severity amplifies the base risk
                float eventRisk = evt.BaseRisk * (0.5f + evt.Severity * 0.5f);

                // Fatal events are remembered longer
                if (!evt.Survived) {
                    eventRisk *= 1.5f;
                }

                risk += eventRisk * ageFactor;
            }
        }

        // Cache the result
        _cachedRisk[road] = (risk, currentTime);
        return risk;
    }

    /// <summary>Get the total risk for a route (sum of road risks).</summary>
    public float GetRouteRisk(List<Location> route, TimePoint currentTime, TradeNetwork network) {
        float totalRisk = 0;

        for (int i = 0; i < route.Count - 1; i++) {
            var road = network.RoadsFrom(route[i]).FirstOrDefault(r => r.To == route[i + 1]);
            if (road != null) {
                totalRisk += GetRoadRisk(road, currentTime);
            } else {
                // No road data - assume moderate risk
                totalRisk += 0.3f;
            }
        }

        return totalRisk;
    }

    /// <summary>Get the average risk per road segment for a route.</summary>
    public float GetAverageRouteRisk(List<Location> route, TimePoint currentTime, TradeNetwork network) {
        if (route.Count < 2) return 0;
        return GetRouteRisk(route, currentTime, network) / (route.Count - 1);
    }

    /// <summary>Get detailed risk breakdown for a route.</summary>
    public IEnumerable<(Road Road, float Risk, List<RiskEvent> RecentEvents)> GetRouteRiskDetails(
        List<Location> route, TimePoint currentTime, TradeNetwork network) {

        for (int i = 0; i < route.Count - 1; i++) {
            var road = network.RoadsFrom(route[i]).FirstOrDefault(r => r.To == route[i + 1]);
            if (road != null) {
                var risk = GetRoadRisk(road, currentTime);
                var recentEvents = GetRecentEvents(road, currentTime);
                yield return (road, risk, recentEvents);
            }
        }
    }

    /// <summary>Get recent events on a road.</summary>
    public List<RiskEvent> GetRecentEvents(Road road, TimePoint currentTime) {
        if (!_events.TryGetValue(road, out var events)) {
            return [];
        }

        return events
            .Where(e => currentTime - e.Timestamp <= MaxEventAge)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>Merge knowledge from another tracker, keeping newer information.</summary>
    public void MergeKnowledge(RouteRiskTracker other, TimePoint currentTime) {
        foreach (var (road, events) in other._events) {
            foreach (var evt in events) {
                // Only merge events that are still relevant
                if (currentTime - evt.Timestamp <= MaxEventAge) {
                    // Check if we already have this event (by timestamp and type)
                    if (!HasEvent(road, evt)) {
                        RecordEvent(evt);
                    }
                }
            }
        }
    }

    bool HasEvent(Road road, RiskEvent evt) {
        if (!_events.TryGetValue(road, out var events)) return false;
        return events.Any(e =>
            e.Timestamp == evt.Timestamp &&
            e.Type == evt.Type &&
            e.AttackerFaction == evt.AttackerFaction);
    }

    /// <summary>Prune old events to save memory.</summary>
    public void PruneOldEvents(TimePoint currentTime) {
        foreach (var (road, events) in _events.ToList()) {
            events.RemoveAll(e => currentTime - e.Timestamp > MaxEventAge);
            if (events.Count == 0) {
                _events.Remove(road);
            }
        }
        _cachedRisk.Clear();
    }

    /// <summary>Clone the risk tracker.</summary>
    public RouteRiskTracker Clone() {
        var clone = new RouteRiskTracker();
        foreach (var (road, events) in _events) {
            clone._events[road] = [..events];
        }
        return clone;
    }

    /// <summary>Get the most dangerous roads known.</summary>
    public IEnumerable<(Road Road, float Risk)> GetDangerousRoads(TimePoint currentTime, int limit = 10) {
        return _events.Keys
            .Select(road => (road, GetRoadRisk(road, currentTime)))
            .Where(x => x.Item2 > 0.2f)
            .OrderByDescending(x => x.Item2)
            .Take(limit);
    }

    /// <summary>Get statistics about the risk tracker.</summary>
    public string GetDebugInfo(TimePoint currentTime) =>
        $"Roads tracked: {_events.Count}, Events: {EventCount}, " +
        $"Dangerous roads: {_events.Keys.Count(r => GetRoadRisk(r, currentTime) > 0.5f)}";
}

/// <summary>
/// Faction-level route risk network that propagates information within territory.
/// Similar to how prices propagate, risk info spreads through faction settlements.
/// </summary>
public class FactionRiskNetwork {
    public Factions Faction { get; }
    public RouteRiskTracker RiskTracker { get; } = new();

    public FactionRiskNetwork(Factions faction) {
        Faction = faction;
    }

    /// <summary>
    /// Propagate a risk event through the faction network.
    /// Events are shared with all settlements in faction territory.
    /// </summary>
    public void PropagateEvent(RiskEvent evt, Map map) {
        RiskTracker.RecordEvent(evt);

        // In a full implementation, this would queue the event to be
        // distributed to settlements over time (simulating communication delay)
    }

    /// <summary>Get known risk for a road within this faction's knowledge.</summary>
    public float GetKnownRisk(Road road, TimePoint currentTime) {
        return RiskTracker.GetRoadRisk(road, currentTime);
    }

    /// <summary>Prune old events from the network.</summary>
    public void Prune(TimePoint currentTime) {
        RiskTracker.PruneOldEvents(currentTime);
    }
}

/// <summary>
/// Global registry of faction risk networks.
/// </summary>
public static class FactionRiskNetworks {
    static readonly Dictionary<Factions, FactionRiskNetwork> _networks = new();

    public static FactionRiskNetwork GetNetwork(Factions faction) {
        if (!_networks.TryGetValue(faction, out var network)) {
            network = new FactionRiskNetwork(faction);
            _networks[faction] = network;
        }
        return network;
    }

    public static void PropagateEvent(RiskEvent evt, Factions reportingFaction, Map map) {
        var network = GetNetwork(reportingFaction);
        network.PropagateEvent(evt, map);
    }

    public static void Clear() {
        _networks.Clear();
    }

    public static void PruneAll(TimePoint currentTime) {
        foreach (var network in _networks.Values) {
            network.Prune(currentTime);
        }
    }
}
