using System.Diagnostics;
using System.Numerics;
using Crawler.Convoy;
using Crawler.Logging;
using Crawler.Network;
using Microsoft.Extensions.Logging;
using Crawler.Production;

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
    Initialized = 1ul << 61,
    Destroyed = 1ul << 62,
    Loading = 1ul << 63,
}
public delegate void HostilityChangedHandler(IActor other, bool hostile);
public delegate void ReceivingFireHandler(IActor source, List<HitRecord> fire);
public delegate void InitializedHandler();
public delegate void DestroyedHandler();
/// <summary>
/// Common interface for all interactive entities (Crawlers, Settlements, Resources).
/// See: docs/DATA-MODEL.md#iactor-interface
/// </summary>
public interface IActor: IComparable, IComparable<IActor> {
    // ===== Identity =====
    /// <summary>Display name (not unique)</summary>
    string Name { get; }

    /// <summary>
    /// Random number seed - unique
    /// </summary>
    ulong Seed { get; }

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

    void Destroy();

    // ===== Simulation ====
    TimePoint LastTime { get; }
    TimePoint Time { get; }
    TimeDuration Elapsed { get; }

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
    /// <param name="evt"></param>
    /// <returns>Elapsed time</returns>
    void HandleEvent(ActorEvent evt);

    /// <summary>
    /// Advances actor's internal time without processing events or thinking.
    /// Used for initializing actor time when entering simulation.
    ///
    /// Parallel to PassTimeUntil (scheduling) as TickTo is to ConsumeTime:
    /// - ConsumeTime(duration) schedules relative to current time → TickTo() processes it
    /// - PassTimeUntil(time) schedules at absolute time → TickTo() processes it
    /// - SimulateTo(time) directly advances time without scheduling or processing
    /// </summary>
    void SimulateTo(TimePoint time);

    void ConsumeTime(string tag, int priority, TimeDuration duration, Action? pre = null, Action? post = null);

    void IdleUntil(string tag, TimePoint time);

    /// <summary>Update this actor with awareness of other actors (AI behavior)</summary>
    void Think();

    /// <summary>Receive combat damage from another actor</summary>
    void ReceiveFire(IActor from, List<HitRecord> fire);

    /// <summary>End this actor's existence (game over, destruction)</summary>
    void SetEndState(EEndState state, string message = "");

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
    /// <summary>Stock baseline tracking for settlements (for dynamic pricing)</summary>
    Economy.SettlementStock? Stock { get; }

    event InitializedHandler? ActorInitialized;
    event DestroyedHandler? ActorDestroyed;
    event HostilityChangedHandler? HostilityChanged;
    /// <summary>
    /// Event fired when this crawler receives fire from another actor.
    /// Invoked before damage is processed, allowing components to react to incoming attacks.
    /// </summary>
    event ReceivingFireHandler? ReceivingFire;

    void Arrived(Encounter encounter);
    void Left(Encounter encounter);
}

// Scheduler event wrappers for use with generic Scheduler<TContext, TEvent, TElement, TTime>

public record ActorEvent(IActor Tag, TimePoint Time, int Priority = ActorEvent.DefaultPriority): ISchedulerEvent<IActor, TimePoint> {
    public const int DefaultPriority = 100;
    public Crawler? AsCrawler => Tag as Crawler;
    public ActorBase? AsActorBase => Tag as ActorBase;
    public virtual void OnStart() { }
    public virtual void OnEnd() { }
    public override string ToString() => $"{Game.TimeString(Time)}.{Priority}: {Tag} {Tag.Location.Description}";
    public record EncounterEvent(IActor Tag, TimePoint Time, string Description, Action? Start = null, Action? End = null, int Priority = DefaultPriority): ActorEvent(Tag, Time, Priority) {
        public override string ToString() => base.ToString() + $" \"{Description}\"";
        public override void OnStart() => Start?.Invoke();
        public override void OnEnd() => End?.Invoke();
    }

    public record TravelEvent(IActor Tag, TimePoint Time, Location Destination): ActorEvent(Tag, Time, 0) {
        public override string ToString() => base.ToString() + $" to {Destination}";
        public override void OnStart() {
            Tag.Location.GetEncounter().TryRemoveActor(Tag);
        }
        public override void OnEnd() {
            Tag.Location = Destination;
            Destination.GetEncounter().AddActorAt(Tag, Time);
        }
    }

    /// <summary>
    /// Event for a single step of transit (5 minutes by default).
    /// Actors travel in discrete steps to enable contact detection with other travelers.
    /// </summary>
    public record TransitStepEvent(
        IActor Tag,
        TimePoint Time,
        Road Road,
        float StartProgress,   // Parametric position at step start (0-1)
        float EndProgress,     // Parametric position at step end (0-1)
        Location Destination   // Final destination location
    ) : ActorEvent(Tag, Time, Priority: 50) {

        public override string ToString() =>
            base.ToString() + $" transit {Road.From.PosString}->{Road.To.PosString} ({StartProgress:P0}->{EndProgress:P0})";

        public override void OnStart() {
            // Update transit registry with start position
            var transit = TransitRegistry.GetTransit(Tag);
            if (transit != null) {
                transit.Progress = StartProgress;
            }
        }

        public override void OnEnd() {
            // Update transit registry with end position
            TransitRegistry.UpdateProgress(Tag, EndProgress);

            if (EndProgress >= 1.0f) {
                // Arrived at destination
                TransitRegistry.EndTransit(Tag);
                Tag.Location = Destination;
                Destination.GetEncounter().AddActorAt(Tag, Time);
            } else {
                // Schedule next step
                ScheduleNextStep();
            }
        }

        void ScheduleNextStep() {
            var transit = TransitRegistry.GetTransit(Tag);
            if (transit == null) return;

            // Calculate next step
            float stepHours = (float)Tuning.Convoy.TransitStepDuration.TotalHours;
            float stepKm = transit.Speed * stepHours;
            float stepProgress = stepKm / Road.Distance;
            float newProgress = Math.Min(EndProgress + stepProgress, 1.0f);

            var nextStep = new TransitStepEvent(
                Tag,
                Time + Tuning.Convoy.TransitStepDuration,
                Road,
                EndProgress,
                newProgress,
                Destination
            );
            Game.Instance!.Schedule(nextStep);
        }
    }

    /// <summary>
    /// Event for a single production cycle on an industry segment.
    /// OnStart consumes all inputs, OnEnd produces all outputs.
    /// </summary>
    public record ProductionCycleEvent(
        Crawler Crawler,
        TimePoint Time,
        IndustrySegment Segment,
        ProductionRecipe Recipe
    ) : ActorEvent(Crawler, Time, Priority: 150) {

        public override string ToString() =>
            base.ToString() + $" producing {Recipe.Name} on {Segment.IndustryType}";

        public override void OnStart() {
            float batchSize = Segment.BatchSize;

            // Verify and consume inputs
            if (!Recipe.HasInputs(Crawler.Supplies, batchSize)) {
                Segment.IsStalled = true;
                return;
            }

            // Consume burst power for activation
            float chargeRequired = Segment.ActivateCharge + Recipe.ActivateCharge;
            if (!Crawler.ConsumeBurstPower(chargeRequired)) {
                Segment.IsStalled = true;
                return;
            }

            // Consume all inputs upfront
            Recipe.ConsumeInputs(Crawler.Supplies, 1.0f, batchSize);
            Recipe.ConsumeConsumables(Crawler.Supplies, 1.0f, batchSize);

            Segment.IsStalled = false;

            // Track production timing for progress calculation
            Segment.ProductionStartTime = Crawler.Time;
            Segment.CycleDuration = TimeDuration.FromHours(Recipe.CycleTime.TotalHours / Segment.Throughput);
        }

        public override void OnEnd() {
            float batchSize = Segment.BatchSize;

            // Produce outputs with efficiency bonus
            Recipe.ProduceOutputs(Crawler.Cargo, Segment.Efficiency, batchSize);
            Recipe.ProduceWaste(Crawler.Cargo, batchSize);

            // Handle maintenance - if not satisfied, accumulate wear
            float maintenanceSatisfied = Recipe.ConsumeMaintenance(Crawler.Supplies, 1.0f, batchSize);
            if (maintenanceSatisfied < 1.0f) {
                Segment.WearAccumulator += Recipe.Wear;
                while (Segment.WearAccumulator >= 1.0f) {
                    Segment.Hits++;
                    Segment.WearAccumulator -= 1.0f;
                }
            }

            // Clear timing info (ProductionProgress becomes 0)
            Segment.ProductionStartTime = TimePoint.Zero;
            Segment.CycleDuration = TimeDuration.Zero;

            // Clear recipe so ProductionAIComponent can schedule next cycle
            // (it will re-evaluate and potentially assign a different recipe)
            Segment.CurrentRecipe = null;
        }
    }
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
    public int CompareTo(IActor? other) {
        if (other == null) return 1;

        return Seed.CompareTo(other.Seed);
    }
    public int CompareTo(object? obj) {
        if (obj == null) return 1;
        if (obj is IActor actor) return CompareTo(actor);
        throw new ArgumentException("Object is not an IActor");
    }
    public override string ToString() => $"{Name} ({Faction}/{Role})" + EndState switch {
        null => "",
        _ => $" [{EndState}]",
    };
    public virtual string Report() {
        return $"{Name}\n{Brief(this)}\n{Supplies}";
    }
    public ILogger Log => LogCat.Log;

    // RNG state - initialized by derived classes or builders
    protected XorShift Rng = new(seed);
    protected GaussianSampler Gaussian = new GaussianSampler(seed * 3 + 7);

    public virtual void Destroy() {
        Log.LogInformation("Destroy {Name}",  Name);
        Debug.Assert(!Flags.HasFlag(ActorFlags.Destroyed));
        Flags |= ActorFlags.Destroyed;
        ActorDestroyed?.Invoke();
        if (_encounter != null) {
            _encounter.RemoveActor(this);
            _encounter = null;
        }
        Game.Instance!.Unschedule(this);
    }
    public bool IsDestroyed => EndState is not null;
    public bool IsSettlement => Flags.HasFlag(ActorFlags.Settlement);
    public bool IsLoading => Flags.HasFlag(ActorFlags.Loading);

    // Component system
    List<IActorComponent> _components = new();
    List<IActorComponent> _newComponents = new();
    bool _componentsDirty = false;

    public IEnumerable<IActorComponent> Components => _components;

    protected void ComponentsChanged(bool notify) {
        if (_componentsDirty) {
            _components.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            _componentsDirty = false;
            if (notify) {
                foreach (var component in _components) {
                    component.ComponentsChanged();
                }
            }
        }
    }

    public void AddComponent(IActorComponent component) {
        component.Attach(this);

        // If we're not in Loading state, component system is already initialized
        // so add directly to _components instead of _newComponents
        if (!IsLoading) {
            //LogCat.Log.LogInformation($"AddComponent: {Name} not loading, adding {component.GetType().Name} directly to _components");
            component.Enter(Encounter);
            _components.Add(component);
        } else {
            //LogCat.Log.LogInformation($"AddComponent: {Name} is loading, adding {component.GetType().Name} to _newComponents");
            _newComponents.Add(component);
        }
        _componentsDirty = true; // Mark for re-sort
    }

    public void RemoveComponent(IActorComponent component) {
        if (_components.Remove(component)) {
            component.Detach();
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
        ComponentsChanged(false);
    }

    // Returns elapsed, >= 0
    public TimePoint LastTime { get; protected set; }
    public TimePoint Time { get; protected set; } = TimePoint.Zero;
    public TimeDuration Elapsed => LastTime.IsValid ? Time - LastTime : TimeDuration.Zero;
    public void HandleEvent(ActorEvent evt) {
        SimulateTo(evt.Time);
        evt.OnEnd();
        if (!Flags.HasFlag(ActorFlags.Loading | ActorFlags.Destroyed)) {
            Think();
        }
        PostTick();
    }
    protected virtual void PostTick() {}

    /// <summary>
    /// Advances actor's internal time without processing events or thinking.
    /// Used for initializing actor time when entering simulation.
    ///
    /// Parallel to PassTimeUntil (scheduling) as TickTo is to ConsumeTime:
    /// - ConsumeTime(duration) schedules relative to current time → TickTo() processes it
    /// - PassTimeUntil(time) schedules at absolute time → TickTo() processes it
    /// - SimulateTo(time) directly advances time without scheduling or processing
    /// </summary>
    public virtual void SimulateTo(TimePoint time) {
        if (Flags.HasFlag(ActorFlags.Loading))
            throw new InvalidOperationException($"Tried to simulate {Name} during loading.");
        // On first simulation (Time is still at initial Zero), initialize both timestamps
        // to the arrival time so Elapsed = 0 (no time has passed for a newly spawned actor)
        if (Time == TimePoint.Zero) {
            (LastTime, Time) = (time, time);
        } else {
            (LastTime, Time) = (Time, time);
        }
        foreach (var component in _components) {
            component.Tick();
        }
    }
    public virtual void ConsumeTime(string tag, int priority, TimeDuration duration, Action? pre = null, Action? post = null) {}
    public virtual void IdleUntil(string tag, TimePoint time) {}
    public virtual void Think() { }
    public virtual void ReceiveFire(IActor from, List<HitRecord> fire) {
        // Notify components that we're receiving fire
        ReceivingFire?.Invoke(from, fire);
    }
    public virtual void Message(string message) {}
    public virtual int Domes => 0;
    public Economy.SettlementStock? Stock { get; set; }

    Encounter? _encounter;
    public void Arrived(Encounter encounter) {
        _encounter = encounter;
        foreach (var component in _newComponents) {
            component.Enter(encounter);
        }
    }

    public void Left(Encounter encounter) {
        Debug.Assert(_encounter == encounter);
        if (_encounter != null) {
            _encounter = null;
            foreach (var component in Components) {
                component.Leave(encounter);
            }
        }
    }

    public void SetEndState(EEndState state, string message = "") {
        Debug.Assert(EndState == null);
        EndMessage = $"{state}: {message}";
        EndState = state;
        Message($"Game Over: {message} ({state})");
        Game.Instance!.ScheduleEncounter(this, Time + TimeDuration.FromHours(5), "Dissolves", null, Destroy, 1000);
        LogCat.Log.LogInformation("ActorBase.End {message} {state}", message, state);
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
    public event DestroyedHandler? ActorDestroyed;
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
            Time = this.Time.Elapsed,
            LastTime = this.LastTime.Elapsed,
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
        this.Time = new TimePoint(data.Time);
        this.LastTime = new TimePoint(data.LastTime);

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
