namespace Crawler;

/// <summary>
/// Event handler for when an actor arrives at an encounter
/// </summary>
public delegate void ActorArrivedEventHandler(IActor actor, long time);

/// <summary>
/// Event handler for when an actor is about to leave an encounter
/// </summary>
public delegate void ActorLeavingEventHandler(IActor actor, long time);

/// <summary>
/// Event handler for when an actor has left an encounter
/// </summary>
public delegate void ActorLeftEventHandler(IActor actor, long time);

/// <summary>
/// Event handler for when time advances in an encounter
/// </summary>
public delegate void EncounterTickEventHandler(long time);

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

    void Detach();

    /// <summary>Enumerate interactions this component provides between owner and subject</summary>
    IEnumerable<Interaction> EnumerateInteractions(IActor subject);

    /// <summary>Called when the actors component list changes. During construction and loading,
    /// called once all components are installed.</summary>
    void OnComponentsDirty();

    /// <summary>Subscribe this component's event handlers to an encounter</summary>
    void Enter(Encounter encounter);

    /// <summary>Unsubscribe this component's event handlers from an encounter</summary>
    void Leave(Encounter encounter);

    /// <summary>
    /// Called during Think() to allow proactive component behaviors.
    /// Components are evaluated in priority order (highest first).
    /// Returns AP cost if action was scheduled, null otherwise.
    /// </summary>
    int ThinkAction();
}

/// <summary>
/// Base class for actor components providing common functionality
/// </summary>
public abstract class ActorComponentBase : IActorComponent {
    public IActor Owner { get; private set; } = null!;
    public Crawler Crawler => Owner as Crawler ?? throw new NullReferenceException();

    /// <summary>Default priority for components. Override to set specific priority.</summary>
    public virtual int Priority => 500;

    public virtual void Attach(IActor owner) {
        Owner = owner;
    }
    public virtual void Detach() {
        Owner = null!;
    }

    public virtual IEnumerable<Interaction> EnumerateInteractions(IActor subject) => [];

    public virtual void OnComponentsDirty() { }

    public virtual void Enter(Encounter encounter) { }

    public virtual void Leave(Encounter encounter) { }

    public virtual int ThinkAction() => 0;
}
