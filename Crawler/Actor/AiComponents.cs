namespace Crawler;

/// <summary>
/// High-priority survival component that makes crawlers flee when vulnerable.
/// Overrides other AI behaviors to prioritize survival.
/// </summary>
public class RetreatComponent : ActorComponentBase {
    public override int Priority => 1000; // Highest priority - survival first

    public override int ThinkAction() {
        if (Owner is not Crawler crawler) return 0;

        // Check if depowered first
        if (crawler.IsDepowered) {
            crawler.Message($"{crawler.Name} has no power.");
            return 0; // Can't act, but don't let other components try either
        }

        // Flee if vulnerable and not pinned
        if (crawler.IsVulnerable && !crawler.Pinned()) {
            crawler.Message($"{crawler.Name} flees the encounter.");
            crawler.Location.GetEncounter().RemoveActor(crawler);
            return 1; // Consumed time to flee
        }

        return 0; // Not vulnerable, let lower priority components handle it
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

    void OnActorArrived(IActor actor, long time) {
        if (Owner == actor) return;
        SetupExtortion(actor, time);
    }

    void OnActorLeft(IActor actor, long time) {
        if (Owner == actor) return;
        // Clear ultimatum when target leaves
        Owner.To(actor).Ultimatum = null;
    }

    void SetupExtortion(IActor target, long time) {
        if (Owner == target) return;
        if (Owner is not Crawler bandit) return;

        if (bandit.Role != CrawlerRole.Bandit || target.Faction != Faction.Player) return;

        float cargoValue = target.Supplies.ValueAt(bandit.Location);
        if (cargoValue >= Tuning.Bandit.minValueThreshold &&
            _rng.NextSingle() < Tuning.Bandit.demandChance &&
            !bandit.To(target).Hostile &&
            !bandit.To(target).Surrendered &&
            !bandit.IsDisarmed) {

            // Set ultimatum with timeout
            bandit.To(target).Ultimatum = new ActorToActor.UltimatumState {
                ExpirationTime = time + 300,
                Type = "BanditExtortion",
                Data = _demandFraction
            };
        }
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        var relation = Owner.To(subject);
        if (relation.Ultimatum?.Type != "BanditExtortion") yield break;

        long expirationTime = relation.Ultimatum.ExpirationTime;
        float demandFraction = relation.Ultimatum.Data as float? ?? _demandFraction;
        bool expired = expirationTime > 0 && Game.SafeTime > expirationTime;

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
