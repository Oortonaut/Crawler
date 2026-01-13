namespace Crawler;

/// <summary>
/// Event handler for when an actor arrives at an encounter
/// </summary>
public delegate void ActorArrivedEventHandler(IActor actor, TimePoint now);

/// <summary>
/// Event handler for when another actor has left an encounter
/// </summary>
public delegate void ActorLeftEventHandler(IActor actor, TimePoint now);

/// <summary>
/// Event handler for when time advances in an encounter
/// </summary>
public delegate void EncounterTickEventHandler(TimePoint then, TimePoint now);

/// <summary>
/// Component-based behavior system for actors.
/// Components can subscribe to encounter events and enumerate interactions directly.
/// </summary>
public interface IActorComponent {
    /// <summary>The actor that owns this component</summary>
    IActor Owner { get; }

    /// <summary>
    /// Priority for ThinkAction evaluation. Higher priority = evaluated first.
    /// Priority ranges:
    /// - 1000+: Critical survival (retreat, emergency)
    /// - 500-999: Faction-specific AI (bandit, civilian, police)
    /// - 100-499: Opportunistic behaviors (scavenge, patrol)
    /// - 0-99: Default/fallback behaviors
    /// </summary>
    int Priority { get; }

    /// <summary>Initialize the component with its owner</summary>
    void Attach(IActor owner);

    /// <summary>Subscribe this component's event handlers to an encounter</summary>
    void Enter(Encounter encounter);

    void Tick();
    /// <summary>Unsubscribe this component's event handlers from an encounter</summary>
    void Leave(Encounter encounter);

    void Detach();

    /// <summary>Enumerate interactions this component provides between owner and subject</summary>
    IEnumerable<Interaction> EnumerateInteractions(IActor subject);

    /// <summary>Called when the actors component list changes. During construction and loading,
    /// called once all components and segmentsare installed. During runtime, called immediately after the list
    /// changes.</summary>
    void ComponentsChanged();

    /// <summary>Called when the actors segment list changes. During construction and loading,
    /// called once all components and segments are installed. During runtime, called immediately after the segment list
    /// changed.</summary>
    void SegmentsChanged();

    /// <summary>Called when a segment's operation state changes. Called once at initialization and
    /// thereafter when the state changes.
    void SegmentStateChanged(Segment segment);

    /// <summary>
    /// Called during Think() to allow proactive component behaviors.
    /// Components are evaluated in priority order (highest first).
    /// Returns AP cost if action was scheduled, null otherwise.
    /// </summary>
    ActorEvent? GetNextEvent();
}

/// <summary>
/// Base class for actor components providing common functionality
/// </summary>
public abstract class ActorComponentBase : IActorComponent {
    public IActor Owner { get; private set; } = null!;
    public Crawler Crawler => Owner as Crawler ?? throw new NullReferenceException();

    /// <summary>Default priority for components. Override to set specific priority.</summary>
    public virtual int Priority => 200;

    public virtual void Attach(IActor owner) {
        Owner = owner;
    }
    public virtual void Tick() { }
    public virtual void Detach() {
        Owner = null!;
    }

    public virtual IEnumerable<Interaction> EnumerateInteractions(IActor subject) => [];

    public virtual void ComponentsChanged() { }
    public virtual void SegmentsChanged() { }
    public virtual void SegmentStateChanged(Segment segment) { }

    public virtual void Enter(Encounter encounter) { }

    public virtual void Leave(Encounter encounter) { }

    public virtual ActorEvent? GetNextEvent() => null;
    public override string ToString() => $"{GetType().Name} on {Owner?.Name}";

    protected Encounter GetEncounter() => Owner.Location.GetEncounter();
}
