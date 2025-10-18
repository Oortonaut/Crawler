using System.Numerics;

namespace Crawler;

public enum HitType {
    Misses, // no hit
    Hits, // hits on armor
    Pierces, // Improved change to miss armor
}

public record struct HitRecord(WeaponSegment Weapon, float Damage, float Aim) {
    float t = Random.Shared.NextSingle();
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
    Revolt,
    Killed,
    Starved,

    Won,
}

[Flags]
public enum EActorFlags {
    None = 0,
    Player = 1 << 0,
    Mobile = 1 << 1,
    Settlement = 1 << 2,
    Creature = 1 << 3,

    Looted = 1 << 16,
}

public interface IActor {
    string Name { get; } // A user-specified or generated name for the actor. Not unique.
    Faction Faction { get; } // The faction this actor belongs to.
    Inventory Inv { get; } // The inventory of this actor; commodities like fuel and crew as well as crawler segments.
    EActorFlags Flags { get; set; }

    Location Location { get; set; } // Where the actor is located.

    string Brief(IActor viewer); // A brief writing on the actor; the viewer affects what info shows
    string Report(); // A detailed report on the actor

    IEnumerable<IProposal> Proposals();

    void Tick();
    void Tick(IEnumerable<IActor> other);
    void ReceiveFire(IActor from, List<HitRecord> fire);

    void End(EEndState state, string message = "");
    string EndMessage { get; }
    EEndState? EndState { get; }

    bool Knows(IActor other);
    bool Knows(Location loc);
    ActorToActor To(IActor other);
    ActorLocation To(Location loc);

    void Message(string message);

    int Domes { get; }
}

public class StaticActor(string name, string brief, Faction faction, Inventory inv, Location location): IActor {
    public string Name => name;
    public Faction Faction => faction;
    public Inventory Inv { get; } = inv;
    public EActorFlags Flags { get; set; } = EActorFlags.None;
    public Location Location { get; set; } = location;
    public bool Harvested => (Flags & EActorFlags.Looted) != 0;
    public string Brief(IActor viewer) => brief + (Harvested ? " (Harvested)" : "") + "\n";
    public string Report() {
        return $"{Name}\n{Brief(this)}\n{Inv}";
    }
    public List<IProposal> StoredProposals { get; private set; } = new();
    public IEnumerable<IProposal> Proposals() => StoredProposals;
    public void Tick() {
    }
    public void Tick(IEnumerable<IActor> other) { }
    public void ReceiveFire(IActor from, List<HitRecord> fire) {
        Message($"{from.Name} fired uselessly at you");
        from.Message($"You fired uselessly at {Name}");
    }
    public void Message(string message) {}
    public int Domes => 0;
    public bool Knows(Location loc) => false;
    public ActorLocation To(Location loc) => new();

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
}
