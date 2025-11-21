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

    /// <summary>Initialize the component with its owner</summary>
    void Initialize(IActor owner);

    /// <summary>Enumerate interactions this component provides between owner and subject</summary>
    IEnumerable<Interaction> EnumerateInteractions(IActor subject);

    /// <summary>Called when this component is added to an actor</summary>
    void OnComponentAdded();

    /// <summary>Called when this component is removed from an actor</summary>
    void OnComponentRemoved();

    /// <summary>Subscribe this component's event handlers to an encounter</summary>
    void SubscribeToEncounter(Encounter encounter);

    /// <summary>Unsubscribe this component's event handlers from an encounter</summary>
    void UnsubscribeFromEncounter(Encounter encounter);
}

/// <summary>
/// Base class for actor components providing common functionality
/// </summary>
public abstract class ActorComponentBase : IActorComponent {
    public IActor Owner { get; private set; } = null!;

    public virtual void Initialize(IActor owner) {
        Owner = owner;
    }

    public virtual IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        yield break;
    }

    public virtual void OnComponentAdded() { }

    public virtual void OnComponentRemoved() { }

    public virtual void SubscribeToEncounter(Encounter encounter) { }

    public virtual void UnsubscribeFromEncounter(Encounter encounter) { }
}
