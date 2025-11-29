using System.Diagnostics;
using System.Numerics;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

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
public delegate void HostilityChangedHandler(IActor other, bool hostile);
public delegate void ReceivingFireHandler(IActor source, List<HitRecord> fire);
public delegate void InitializedHandler();
/// <summary>
/// Common interface for all interactive entities (Crawlers, Settlements, Resources).
/// See: docs/DATA-MODEL.md#iactor-interface
/// </summary>
public interface IActor {
    // ===== Identity =====
    /// <summary>Display name (not unique)</summary>
    string Name { get; }

    /// <summary>Political allegiance</summary>
    Factions Faction { get; }

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

    // ===== Lifetime State =====
    void Begin();

    /// <summary>Game over message if actor is destroyed/ended</summary>
    string EndMessage { get; }

    /// <summary>End condition (Destroyed, Revolt, etc.) or null if still active</summary>
    EEndState? EndState { get; }

    // ===== Simulation ====
    long LastTime { get; }
    long Time { get; }
    int Elapsed { get; }

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

    void SetHostileTo(IActor other, bool hostile);

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
    void SimulateTo(long time);

    /// <summary>Update this actor with awareness of other actors (AI behavior)</summary>
    void Think();

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

    event InitializedHandler? ActorInitialized;
    event HostilityChangedHandler? HostilityChanged;
    /// <summary>
    /// Event fired when this crawler receives fire from another actor.
    /// Invoked before damage is processed, allowing components to react to incoming attacks.
    /// </summary>
    event ReceivingFireHandler? ReceivingFire;

    void Arrived(Encounter encounter);
    void Left(Encounter encounter);
}

public class ActorBase(ulong seed, string name, string brief, Factions faction, Inventory supplies, Inventory cargo, Location Location): IActor {
    public record class Init {
        public required ulong Seed { get; set; }
        public string Name { get; set; } = "";
        public string Brief { get; set; } = "";
        public Factions Faction { get; set; }
        public Location Location { get; set; } = null!;
        public Inventory Supplies { get; set; } = new();
        public Inventory Cargo { get; set; } = new();
    }

    public record class Data {
        public Init Init { get; set; } = null!;
        public XorShift.Data Rng { get; set; } = null!;
        public GaussianSampler.Data Gaussian { get; set; } = null!;
        public long Time { get; set; }
        public long LastTime { get; set; }
        public EEndState? EndState { get; set; }
        public string EndMessage { get; set; } = "";
        public Dictionary<string, ActorToActor.Data> ActorRelations { get; set; } = new();
        public Dictionary<string, LocationActor.Data> LocationRelations { get; set; } = new();
    }

    public class Builder {
        protected ulong _seed = (ulong)Random.Shared.NextInt64();
        protected string _name = "";
        protected string _brief = "";
        protected Factions _faction = Factions.Independent;
        protected Location _location = null!;
        protected Inventory _supplies = new();
        protected Inventory _cargo = new();

        public Builder() { }

        public Builder WithSeed(ulong seed) {
            _seed = seed;
            return this;
        }

        public Builder WithName(string name) {
            _name = name;
            return this;
        }

        public Builder WithBrief(string brief) {
            _brief = brief;
            return this;
        }

        public Builder WithFaction(Factions faction) {
            _faction = faction;
            return this;
        }

        public Builder WithLocation(Location location) {
            _location = location;
            return this;
        }

        public Builder WithSupplies(Inventory supplies) {
            _supplies = supplies;
            return this;
        }

        public Builder WithCargo(Inventory cargo) {
            _cargo = cargo;
            return this;
        }

        public Builder AddSupplies(Commodity commodity, float amount) {
            _supplies.Add(commodity, amount);
            return this;
        }

        public Builder AddCargo(Commodity commodity, float amount) {
            _cargo.Add(commodity, amount);
            return this;
        }

        public Init BuildInit() {
            return new Init {
                Seed = _seed,
                Name = _name,
                Brief = _brief,
                Faction = _faction,
                Location = _location,
                Supplies = _supplies,
                Cargo = _cargo
            };
        }

        public static Builder Load(Init init) {
            return new Builder()
                .WithSeed(init.Seed)
                .WithName(init.Name)
                .WithBrief(init.Brief)
                .WithFaction(init.Faction)
                .WithLocation(init.Location)
                .WithSupplies(init.Supplies)
                .WithCargo(init.Cargo);
        }
    }

    // Init-based constructor
    public ActorBase(Init init) : this(init.Seed, init.Name, init.Brief, init.Faction, init.Supplies, init.Cargo, init.Location) { }

    // Init + Data constructor (for loading from save)
    public ActorBase(Init init, Data data) : this(init) {
        FromData(data);
    }

    public ulong Seed { get; set; } = seed;
    public string Name { get; set; } = name;
    public Factions Faction { get; set; } = faction;
    public Inventory Supplies { get; } = supplies;
    public Inventory Cargo { get; } = cargo;
    public ActorFlags Flags { get; set; } = ActorFlags.Loading;
    public Location Location { get; set; } = Location;
    public bool HasEncounter => Location.HasEncounter;
    public Encounter Encounter => Location.GetEncounter();
    public Roles Role { get; set; } = Roles.None;
    public bool Harvested => EndState == EEndState.Looted;
    public virtual string Brief(IActor viewer) => brief;
    public override string ToString() => $"{Name} ({Faction}/{Role})";
    public virtual string Report() {
        return $"{Name}\n{Brief(this)}\n{Supplies}";
    }
    public ILogger Log => LogCat.Log;

    // RNG state - initialized by derived classes or builders
    protected XorShift Rng = new(seed);
    protected GaussianSampler Gaussian = new GaussianSampler(seed * 3 + 7);

    public bool IsDestroyed => EndState is not null;
    public bool IsSettlement => Flags.HasFlag(ActorFlags.Settlement);
    public bool IsLoading => Flags.HasFlag(ActorFlags.Loading);

    // Component system
    List<IActorComponent> _components = new();
    List<IActorComponent> _newComponents = new();
    bool _componentsDirty = false;

    public IEnumerable<IActorComponent> Components => _components;

    protected void CleanComponents(bool notify) {
        if (_componentsDirty) {
            _components.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _componentsDirty = false;
            if (notify) {
                foreach (var component in _components) {
                    component.OnComponentsDirty();
                }
            }
        }
    }

    public void AddComponent(IActorComponent component) {
        component.Attach(this);
        _newComponents.Add(component);
        _componentsDirty = true; // Mark for re-sort
    }

    public void RemoveComponent(IActorComponent component) {
        if (_components.Remove(component)) {
            _componentsDirty = true; // Mark for re-sort (though not strictly necessary)
        } else if (_newComponents.Remove(component)) {
            Debug.Assert(_componentsDirty);
        }
    }

    /// <summary>
    /// Get an existing component of the specified type, or create and add a new one if it doesn't exist.
    /// </summary>
    public TComponent GetOrAddComponent<TComponent>(Func<TComponent>? factory = null) where TComponent : IActorComponent, new() {
        var existing = Components.OfType<TComponent>().FirstOrDefault();
        if (existing != null) {
            return existing;
        }

        var newComponent = factory != null ? factory() : new TComponent();
        AddComponent(newComponent);
        return newComponent;
    }

    public virtual void Begin() {
        if (!Flags.HasFlag(ActorFlags.Loading))
            throw new InvalidOperationException($"Double Initialize");
        Flags &= ~ActorFlags.Loading;
        ActorInitialized?.Invoke();
        foreach (var component in _newComponents) {
            component.Enter(Encounter);
            _components.Add(component);
        }
        _newComponents.Clear();
        CleanComponents(false);
    }

    // Returns elapsed, >= 0
    public long LastTime { get; protected set; }
    public long Time { get; protected set; } = 0;
    public int Elapsed => (int)(LastTime > 0 ? Time - LastTime : 0);
    public virtual void SimulateTo(long time) {
        if (Flags.HasFlag(ActorFlags.Loading))
            throw new InvalidOperationException($"Tried to simulate {Name} during loading.");
        (LastTime, Time) = (Time, time);
    }
    public virtual void Think() { }
    public virtual void ReceiveFire(IActor from, List<HitRecord> fire) {
        // Notify components that we're receiving fire
        ReceivingFire?.Invoke(from, fire);
    }
    public virtual void Message(string message) {}
    public int Domes { get; set; } = 0;

    public void Arrived(Encounter encounter) {
        foreach (var component in _newComponents) {
            component.Enter(encounter);
        }
        CleanComponents(true);
    }

    public void Left(Encounter encounter) {
        foreach (var component in Components) {
            component.Leave(encounter);
        }
    }

    public void End(EEndState state, string message = "") {
        EndMessage = $"{state}: {message}";
        EndState = state;
        Message($"Game Over: {message} ({state})");
        GetOrAddComponent<LeaveEncounterComponent>().ExitAfter(36000);
    }
    public string EndMessage { get; set; } = string.Empty;
    public EEndState? EndState { get; set; }

    public virtual void Travel(Location loc) {
        Encounter.RemoveActor(this);
        Location = loc;
        Encounter.AddActor(this);
    }

    public EArraySparse<Factions, ActorFaction> FactionRelations { get; } = new();
    public ActorFaction To(Factions faction) => FactionRelations.GetOrAddNew(faction, () => new ActorFaction(this, faction));

    public event InitializedHandler? ActorInitialized;
    public event HostilityChangedHandler? HostilityChanged;
    public event ReceivingFireHandler? ReceivingFire;

    Dictionary<IActor, ActorToActor> _relations = new();
    public bool Knows(IActor other) => _relations.ContainsKey(other);
    public ActorToActor To(IActor other) => _relations.GetOrAddNew(other, () => NewRelation(other));
    public ActorToActor NewRelation(IActor other) {
        var relation = new ActorToActor();
        var actorFaction = To(other.Faction);

        if (actorFaction.ActorStanding < 0) {
            bool hostile = true;
            relation.Hostile = hostile;
            HostilityChanged?.Invoke(other, hostile);
        }
        return relation;
    }
    public Dictionary<IActor, ActorToActor> GetRelations() => _relations;
    public void SetRelations(Dictionary<IActor, ActorToActor> relations) => _relations = relations;

    Dictionary<Location, LocationActor> _locations = new();
    public bool Knows(Location location) => _locations.ContainsKey(location);
    public LocationActor To(Location location) => _locations.GetOrAddNew(location, () => NewRelation(location));
    public LocationActor NewRelation(Location to) => new();
    public Dictionary<Location, LocationActor> GetVisitedLocations() => _locations;
    public void SetVisitedLocations(Dictionary<Location, LocationActor> locations) => _locations = locations;
    public void SetHostileTo(IActor other, bool hostile) {
        var relation = To(other);
        if (relation.Hostile != hostile) {
            relation.Hostile = hostile;
            HostilityChanged?.Invoke(other, hostile);
        }
    }

    // Serialization methods
    public virtual Data ToData() {
        // Create Init from current state
        var init = new Init {
            Seed = this.Seed,
            Name = this.Name,
            Brief = this.Brief(this),
            Faction = this.Faction,
            Location = this.Location,
            Supplies = this.Supplies,
            Cargo = this.Cargo
        };

        // Serialize actor relations by name (will need to be resolved on load)
        var actorRelations = new Dictionary<string, ActorToActor.Data>();
        foreach (var (actor, relation) in _relations) {
            actorRelations[actor.Name] = relation.ToData();
        }

        // Serialize location relations by position key
        var locationRelations = new Dictionary<string, LocationActor.Data>();
        foreach (var (location, relation) in _locations) {
            locationRelations[$"{location.Position.X},{location.Position.Y}"] = relation.ToData();
        }

        return new Data {
            Init = init,
            Rng = this.Rng.ToData(),
            Gaussian = this.Gaussian.ToData(),
            Time = this.Time,
            LastTime = this.LastTime,
            EndState = this.EndState,
            EndMessage = this.EndMessage,
            ActorRelations = actorRelations,
            LocationRelations = locationRelations
        };
    }

    public virtual void FromData(Data data) {
        // Restore RNG state
        this.Rng.FromData(data.Rng);
        this.Gaussian.FromData(data.Gaussian);

        // Restore time tracking
        this.Time = data.Time;
        this.LastTime = data.LastTime;

        // Restore end state
        this.EndState = data.EndState;
        this.EndMessage = data.EndMessage;

        // Note: Actor and Location relations are restored in a second pass
        // after all actors are loaded (see SaveLoad.RestoreRelationsTo)
    }

    // Called after all actors are loaded to restore relations
    public void RestoreActorRelations(Dictionary<string, ActorToActor.Data> actorRelations, Dictionary<string, IActor> actorLookup) {
        _relations.Clear();
        foreach (var (actorName, relationData) in actorRelations) {
            if (actorLookup.TryGetValue(actorName, out var actor)) {
                var relation = new ActorToActor();
                relation.FromData(relationData);
                _relations[actor] = relation;
            }
        }
    }

    public void RestoreLocationRelations(Dictionary<string, LocationActor.Data> locationRelations, Map map) {
        _locations.Clear();
        foreach (var (posKey, relationData) in locationRelations) {
            var parts = posKey.Split(',');
            if (parts.Length == 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float y)) {
                var location = map.FindLocationByPosition(new System.Numerics.Vector2(x, y));
                var relation = new LocationActor();
                relation.FromData(relationData);
                _locations[location] = relation;
            }
        }
    }
}
