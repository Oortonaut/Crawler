namespace Crawler;

public enum HitType {
    Misses, // no hit
    Hits, // hits on armor
    Pierces, // Improved change to miss armor
}
public record struct HitRecord(int Damage, int Aim) {
    const double spread = 0.4;
    double PenChance => 0.15 + spread * Aim;
    double HitChance => 0.75 + spread * Aim;
    public HitType Hit => _Hit();
    HitType _Hit() {
        var test = Random.Shared.NextDouble();
        if (test < PenChance) {
            return HitType.Pierces;
        } else if (test < HitChance) {
            return HitType.Hits;
        } else {
            return HitType.Misses;
        }
    }
}
public interface IActor {
    string Name { get; }
    Faction Faction { get; }
    Inventory Inv { get; }

    Location Location { get; set; }

    string Brief(int detail);
    string Report();

    // Called on an actor (the player) to get their reaction to another actor
    IEnumerable<MenuItem> MenuItems(IActor other);
    IEnumerable<TradeOffer> TradeOffers(IActor other);

    void Tick(IEnumerable<IActor> other);
    void ReceiveFire(IActor from, List<HitRecord> fire);

    string? FailState { get; }
}
