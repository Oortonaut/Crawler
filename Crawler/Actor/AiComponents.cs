namespace Crawler;

/// <summary>
/// High-priority survival component that makes crawlers flee when vulnerable.
/// Overrides other AI behaviors to prioritize survival.
/// </summary>
public class RetreatComponent : ActorComponentBase {
    public override int Priority => 1000; // Highest priority - survival first

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;

        // Check if depowered first
        if (crawler.IsDepowered) {
            crawler.Message($"{crawler.Name} has no power.");
            return null;
        }

        float hits = crawler.Segments.Sum(s  => s.Hits);
        float maxHits = crawler.Segments.Sum(s  => s.MaxHealth);
        float damageRatio =  hits / maxHits;

        // Flee if vulnerable and not pinned
        if (crawler.IsVulnerable && damageRatio > 0.75f) {
            float escapeChance = crawler.EscapeChance(); // Slow, O(N) in encounter crawlers
            if (escapeChance > 0) {
                return crawler.NewEventFor("Flee", Priority, Tuning.Crawler.FleeTime, Post: () => {
                    if (crawler.GetRng().NextSingle() < escapeChance) {
                        crawler.Message($"{crawler.Name} fled.");
                        crawler.Location.GetEncounter().RemoveActor(crawler);
                    } else {
                        crawler.Message($"{crawler.Name} couldn't escape.");

                    }
                }); // Consumed time to flee
            }
        }

        return null; // Not vulnerable, let lower priority components handle it
    }
}

/// <summary>
/// Bandit AI component that handles extortion and ultimatum management.
/// Does not handle combat - use with CombatComponentAdvanced for that.
/// </summary>
public class BanditComponent : ActorComponentBase {
    XorShift _rng;
    float _demandFraction;

    public BanditComponent(ulong seed, float demandFraction = 0.5f) {
        _rng = new XorShift(seed);
        _demandFraction = demandFraction;
    }

    public override int Priority => 600; // Faction-specific behavior

    public override void Enter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    void OnActorArrived(IActor actor, TimePoint time) {
        if (Owner == actor) return;
        SetupExtortion(actor, time);
    }

    void OnActorLeft(IActor actor, TimePoint time) {
        if (Owner == actor) return;
        // Clear ultimatum when target leaves
        Owner.To(actor).Ultimatum = null;
    }

    void SetupExtortion(IActor target, TimePoint time) {
        if (Owner == target) return;
        if (Owner is not Crawler bandit) return;
        if (bandit.Role != Roles.Bandit) return;

        // Skip fellow bandits and settlements
        if (target is Crawler { Role: Roles.Bandit or Roles.Settlement }) return;

        // Calculate combined standing: faction-to-actor + actor-to-faction + actor-to-actor
        // Total range is +-1000. Negative = hostile, positive = friendly
        // bandit.To(target.Faction).FactionStanding = how target's faction feels about bandit
        // target.To(bandit.Faction).FactionStanding = how bandit's faction feels about target
        // bandit.To(target).Standing = direct actor-to-actor standing
        var targetActor = target as ActorBase;
        int factionToTarget = targetActor?.To(bandit.Faction).FactionStanding ?? 0;
        int standing = bandit.To(target.Faction).FactionStanding
                     + factionToTarget
                     + bandit.To(target).Standing;

        // Don't extort targets we're friendly with
        if (standing >= 0) return;

        float cargoValue = target.Supplies.ValueAt(bandit.Location);
        if (cargoValue >= Tuning.Bandit.minValueThreshold &&
            _rng.NextSingle() < Tuning.Bandit.demandChance &&
            !bandit.To(target).Hostile &&
            !bandit.To(target).Surrendered &&
            !bandit.IsDisarmed) {

            // Set ultimatum with timeout
            bandit.To(target).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = time + Tuning.Crawler.UltimatumTimeout,
                Type = "BanditExtortion",
                Data = _demandFraction
            };
        }
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        var relation = Owner.To(subject);
        if (relation.Ultimatum?.Type != "BanditExtortion") yield break;

        var expirationTime = relation.Ultimatum.ExpirationTime;
        float demandFraction = relation.Ultimatum.Data as float? ?? _demandFraction;
        bool expired = expirationTime.IsValid && Owner.Time > expirationTime;

        if (!expired) {
            // Use shared extortion interactions
            IOffer MakeDemand() => subject.SupplyOffer(_rng/1, demandFraction);

            yield return new ExtortionInteractions.AcceptExtortionInteraction(
                Owner,
                subject,
                MakeDemand,
                "Accept extortion: hand over cargo",
                "DY"
            );
            yield return new ExtortionInteractions.RefuseExtortionInteraction(Owner, subject, "");
        } else {
            // Time expired - attack automatically
            yield return new ExtortionInteractions.ExtortionExpiredInteraction(Owner, subject, "");
        }
    }
}
