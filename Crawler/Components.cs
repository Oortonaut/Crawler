namespace Crawler;

/// <summary>
/// Event arguments for when an actor arrives at an encounter
/// </summary>
public class ActorArrivedEventArgs : EventArgs {
    public IActor Actor { get; }
    public long Time { get; }
    public Encounter Encounter { get; }

    public ActorArrivedEventArgs(IActor actor, long time, Encounter encounter) {
        Actor = actor;
        Time = time;
        Encounter = encounter;
    }
}

/// <summary>
/// Event arguments for when an actor is about to leave an encounter
/// </summary>
public class ActorLeavingEventArgs : EventArgs {
    public IActor Actor { get; }
    public long Time { get; }
    public Encounter Encounter { get; }

    public ActorLeavingEventArgs(IActor actor, long time, Encounter encounter) {
        Actor = actor;
        Time = time;
        Encounter = encounter;
    }
}

/// <summary>
/// Event arguments for when an actor has left an encounter
/// </summary>
public class ActorLeftEventArgs : EventArgs {
    public IActor Actor { get; }
    public long Time { get; }
    public Encounter Encounter { get; }

    public ActorLeftEventArgs(IActor actor, long time, Encounter encounter) {
        Actor = actor;
        Time = time;
        Encounter = encounter;
    }
}

/// <summary>
/// Event arguments for when time advances in an encounter
/// </summary>
public class EncounterTickEventArgs : EventArgs {
    public long Time { get; }
    public Encounter Encounter { get; }

    public EncounterTickEventArgs(long time, Encounter encounter) {
        Time = time;
        Encounter = encounter;
    }
}

/// <summary>
/// Component-based behavior system for actors.
/// Components can subscribe to encounter events and generate proposals dynamically.
/// </summary>
public interface IActorComponent {
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

    public abstract IEnumerable<IProposal> GenerateProposals(IActor owner);

    public virtual void OnComponentAdded() { }

    public virtual void OnComponentRemoved() { }

    public virtual void SubscribeToEncounter(Encounter encounter) { }

    public virtual void UnsubscribeFromEncounter(Encounter encounter) { }
}
