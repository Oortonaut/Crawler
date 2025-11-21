namespace Crawler;

/// <summary>
/// Event types that can occur in an encounter
/// </summary>
public enum EncounterEventType {
    /// <summary>A new actor has joined the encounter</summary>
    ActorArrived,

    /// <summary>An actor is about to leave the encounter</summary>
    ActorLeaving,

    /// <summary>An actor has left the encounter</summary>
    ActorLeft,

    /// <summary>Time has advanced in the encounter</summary>
    EncounterTick,
}

/// <summary>
/// Represents an event that occurred in an encounter
/// </summary>
public record EncounterEvent(
    EncounterEventType Type,
    IActor? Actor,
    long Time,
    Encounter Encounter
);

/// <summary>
/// Handler for encounter events
/// </summary>
public interface IEncounterEventHandler {
    /// <summary>Handle an encounter event</summary>
    void HandleEvent(EncounterEvent evt);
}

/// <summary>
/// Component-based behavior system for actors.
/// Components can subscribe to encounter events and generate proposals dynamically.
/// </summary>
public interface IActorComponent : IEncounterEventHandler {
    /// <summary>The actor that owns this component</summary>
    IActor Owner { get; }

    /// <summary>Initialize the component with its owner</summary>
    void Initialize(IActor owner);

    /// <summary>Generate proposals this component provides</summary>
    IEnumerable<IProposal> GenerateProposals(IActor owner);

    /// <summary>Called when this component is added to an actor</summary>
    void OnComponentAdded();

    /// <summary>Called when this component is removed from an actor</summary>
    void OnComponentRemoved();

    /// <summary>Event types this component wants to subscribe to</summary>
    IEnumerable<EncounterEventType> SubscribedEvents { get; }
}

/// <summary>
/// Base class for actor components providing common functionality
/// </summary>
public abstract class ActorComponentBase : IActorComponent {
    public IActor Owner { get; private set; } = null!;

    public virtual void Initialize(IActor owner) {
        Owner = owner;
    }

    public abstract IEnumerable<IProposal> GenerateProposals(IActor owner);

    public virtual void OnComponentAdded() { }

    public virtual void OnComponentRemoved() { }

    public abstract void HandleEvent(EncounterEvent evt);

    public abstract IEnumerable<EncounterEventType> SubscribedEvents { get; }
}
