using System.Numerics;

namespace Crawler;

public enum HitType {
    Misses, // no hit
    Hits, // hits on armor
    Pierces, // Improved change to miss armor
}

public record struct HitRecord(ulong seed, WeaponSegment Weapon, float Damage, float Aim) {
    XorShift Rng = new(seed);
    float t => Rng.NextSingle();
    public HitType Hit => _Hit();
    HitType _Hit() {
        var test = t + Aim;
        if (test < 0.5f) {
            return HitType.Misses;
        } else if (test < 1.5f) {
            return HitType.Hits;
        } else {
            return HitType.Pierces;
        }
    }
}

public enum EEndState {
    Destroyed,
    Looted,
    Revolt,
    Killed,
    Starved,

    Won,
}

[Flags]
public enum ActorFlags: ulong {
    None = 0,
    Player = 1 << 0,
    Mobile = 1 << 1,
    Settlement = 1 << 2,
    Creature = 1 << 3,
    Capital = 1 << 4,
    Loading = 1ul << 63,
}

/// <summary>
/// Common interface for all interactive entities (Crawlers, Settlements, Resources).
/// See: docs/DATA-MODEL.md#iactor-interface
/// </summary>
public interface IActor {
    // ===== Identity =====
    /// <summary>Display name (not unique)</summary>
    string Name { get; }

    /// <summary>Political allegiance</summary>
    Faction Faction { get; }

    /// <summary>Type flags (Mobile, Settlement, Creature)</summary>
    ActorFlags Flags { get; set; }

    /// <summary>Current location in the world</summary>
    Location Location { get; set; }

    // ===== Construction =====
    void AddComponent(IActorComponent component);
    void RemoveComponent(IActorComponent component);

    // ===== Resources =====
    /// <summary>Commodities and segments this actor is willing to trade</summary>
    Inventory Cargo { get; }

    /// <summary>Commodities and segments owned by this actor</summary>
    Inventory Supplies { get; }

    // ===== State =====
    /// <summary>Game over message if actor is destroyed/ended</summary>
    string EndMessage { get; }

    /// <summary>End condition (Destroyed, Revolt, etc.) or null if still active</summary>
    EEndState? EndState { get; }

    // ===== Knowledge & Relations =====
    /// <summary>Has met this actor before?</summary>
    bool Knows(IActor other);

    /// <summary>Has visited this location before?</summary>
    bool Knows(Location loc);

    /// <summary>
    /// Get relationship state with another actor (hostile, damage history, ultimatum timer).
    /// See: docs/DATA-MODEL.md#actortoactor-relationship-state
    /// </summary>
    ActorToActor To(IActor other);

    /// <summary>Get visit tracking for a location</summary>
    LocationActor To(Location loc);

    /// <summary>
    /// Initialize relationship when actors first meet (called once per side per actor pair).
    /// Creates ActorToActor relation and sets initial faction-based state and proposals.
    /// </summary>
    ActorToActor NewRelation(IActor other);

    /// <summary>
    /// Initialize actor/location relationship on first visit. The Encounter will
    /// have been created.
    /// </summary>
    LocationActor NewRelation(Location other);

    // ===== Interactions =====
    // ===== Simulation =====
    /// <summary>Update this actor (called every game second)</summary>
    /// <param name="time"></param>
    /// <returns>Elapsed time</returns>
    int SimulateTo(long time);

    /// <summary>Update this actor with awareness of other actors (AI behavior)</summary>
    void ThinkFor(int elapsed);

    /// <summary>Receive combat damage from another actor</summary>
    void ReceiveFire(IActor from, List<HitRecord> fire);

    /// <summary>End this actor's existence (game over, destruction)</summary>
    void End(EEndState state, string message = "");

    // ===== Display =====
    /// <summary>One-line summary (context-aware based on viewer)</summary>
    string Brief(IActor viewer);

    /// <summary>Detailed report (inventory, segments, stats)</summary>
    string Report();

    /// <summary>Send notification message to this actor</summary>
    void Message(string message);

    // ===== Misc =====
    /// <summary>Number of domes (for settlements)</summary>
    int Domes { get; }

    void Arrived(Encounter encounter);
    void Left(Encounter encounter);
}

public class ActorBase(string name, string brief, Faction faction, Inventory supplies, Inventory cargo, Location location): IActor {
    public string Name => name;
    public Faction Faction => faction;
    public Inventory Supplies { get; } = supplies;
    public Inventory Cargo { get; } = cargo;
    public ActorFlags Flags { get; set; } = ActorFlags.None;
    public Location Location { get; set; } = location;
    public bool Harvested => EndState == EEndState.Looted;
    public string Brief(IActor viewer) => brief + (Harvested ? " (Harvested)" : "") + "\n";
    public string Report() {
        return $"{Name}\n{Brief(this)}\n{Supplies}";
    }

    // Component system
    List<IActorComponent> _components = new();
    bool _componentsDirty = false;

    public IEnumerable<IActorComponent> Components => _components;

    /// <summary>
    /// Get components sorted by priority (highest first), using lazy sorting.
    /// </summary>
    protected List<IActorComponent> ComponentsByPriority() {
        if (_componentsDirty) {
            _components.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _componentsDirty = false;
            foreach (var component in _components) {
                component.OnComponentsDirty();
            }
        }
        return _components;
    }

    public void AddComponent(IActorComponent component) {
        component.Attach(this);
        _components.Add(component);
        _componentsDirty = true; // Mark for re-sort
    }

    public void RemoveComponent(IActorComponent component) {
        if (_components.Remove(component)) {
            _componentsDirty = true; // Mark for re-sort (though not strictly necessary)
        }
    }

    public int SimulateTo(long time) => 0;
    public virtual void ThinkFor(int elapsed) { }
    public void ReceiveFire(IActor from, List<HitRecord> fire) {
        Message($"{from.Name} fired uselessly at you");
        from.Message($"You fired uselessly at {Name}");
    }
    public void Message(string message) {}
    public int Domes => 0;

    public void Arrived(Encounter encounter) {
        // Subscribe all actor components to encounter events
        ComponentsByPriority();
        foreach (var component in Components) {
            component.SubscribeToEncounter(encounter);
        }
    }

    public void Left(Encounter encounter) {
        foreach (var component in Components) {
            component.UnsubscribeFromEncounter(encounter);
        }
    }
    public bool Knows(Location loc) => false;
    public LocationActor To(Location loc) => new();

    EEndState? _endState;
    string _endMessage = string.Empty;

    public void End(EEndState state, string message = "") {
        _endState = state;
        _endMessage = message;
    }
    public string EndMessage => _endMessage;
    public EEndState? EndState => _endState;
    public bool Knows(IActor other) => false;
    public ActorToActor To(IActor other) => new();
    public ActorToActor NewRelation(IActor other) => new();
    public LocationActor NewRelation(Location other) => new();
}
