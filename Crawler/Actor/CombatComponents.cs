using System.Diagnostics;
using Crawler.Convoy;
using Crawler.Economy;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

namespace Crawler;

/// <summary>
/// Component that provides attack interactions for player.
/// </summary>
// TODO: Change this so the player can "Engage" or "Disengage" from a certain target.
// The player can only be engaged with ont target at once. Probably should change to inherit
// from combat component base.
public class AttackComponent : ActorComponentBase {
    string _optionCode;

    public AttackComponent(string optionCode = "A") {
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: only player can attack, target must be alive crawler
        LogCat.Log.LogInformation($"AttackComponent.EnumerateInteractions: Owner={Owner?.Name}, Subject={subject?.Name}");

        if (Owner != Game.Instance?.Player) {
            LogCat.Log.LogInformation($"AttackComponent: Owner is not player (Owner={Owner?.Name}, Player={Game.Instance?.Player?.Name})");
            yield break;
        }
        if (subject is not Crawler _) {
            LogCat.Log.LogInformation($"AttackComponent: Subject {subject?.Name} is not a Crawler");
            yield break;
        }
        if (!subject.Lives()) {
            LogCat.Log.LogInformation($"AttackComponent: Subject {subject?.Name} is not alive (EndState={subject?.EndState})");
            yield break;
        }

        LogCat.Log.LogInformation($"AttackComponent: Yielding AttackInteraction for {Owner?.Name} -> {subject?.Name}");
        yield return new AttackInteraction(Owner!, subject!, _optionCode);
    }

    /// <summary>
    /// Attack interaction - initiates combat
    /// </summary>
    public record AttackInteraction(IActor Attacker, IActor Defender, string MenuOption)
        : Interaction(Attacker, Defender, MenuOption) {

        public override string Description => $"Attack {Subject.Name}";

        public override bool Perform(string args = "") {
            if (Mechanic is Crawler attacker) {
                attacker.Attack(Subject);
                return true;
            }
            return false;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Mechanic.Ended()) return Immediacy.Failed;
            if (Subject.Ended()) return Immediacy.Failed;
            if (Mechanic is not Crawler attacker) return Immediacy.Failed;
            if (attacker.IsDisarmed) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => TimeDuration.Zero; // Duration determined by weapon delays
    }
}

/// <summary>
/// Component that allows player to accept surrender from vulnerable enemies
/// </summary>
public class SurrenderComponent : ActorComponentBase {
    XorShift _rng;
    string _optionCode;

    public SurrenderComponent(ulong seed, string optionCode = "S") {
        _rng = new XorShift(seed);
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: subject must be vulnerable crawler, hostile to owner, not already surrendered
        if (subject is not Crawler loser) yield break;
        if (!loser.IsVulnerable || !loser.Lives()) yield break;
        if (Owner == subject) yield break;
        if (!Owner.To(subject).Hostile) yield break;
        if (subject.To(Owner).Surrendered) yield break;

        yield return new SurrenderInteraction(Owner, subject, _rng, _optionCode);
    }

    /// <summary>
    /// Accept surrender interaction - loser gives up cargo/supplies based on damage
    /// </summary>
    public record SurrenderInteraction(IActor Winner, IActor Loser, XorShift Rng, string MenuOption)
        : Interaction(Winner, Loser, MenuOption) {

        public override string Description => $"Accept {Loser.Name}'s surrender";

        Inventory MakeSurrenderInv() {
            Inventory surrenderInv = new();
            float ratio = 0;
            if (Loser is Crawler loser) {
                float totalHits = loser.Segments.Sum(s => s.Hits);
                float totalMaxHits = loser.Segments.Sum(s => s.MaxHealth);
                ratio = totalHits / (totalMaxHits + 1e-8f);
            }
            surrenderInv.Add(Loser.Supplies.Loot(Rng/1, ratio));
            if (Loser.Supplies != Loser.Cargo) {
                surrenderInv.Add(Loser.Cargo.Loot(Rng/2, ratio));
            }
            return surrenderInv;
        }

        public override bool Perform(string args = "") {
            var surrenderInv = MakeSurrenderInv();

            Winner.Message($"{Loser.Name} has surrendered to you . {Tuning.Crawler.MoraleSurrenderedTo} Morale");
            Loser.Message($"You have surrendered to {Winner.Name}. {Tuning.Crawler.MoraleSurrendered} Morale");
            Loser.To(Winner).Surrendered = true;
            Winner.To(Loser).Spared = true;
            Loser.SetHostileTo(Winner, false);
            Winner.SetHostileTo(Loser, false);
            Winner.Supplies[Commodity.Morale] += Tuning.Crawler.MoraleSurrenderedTo;
            Loser.Supplies[Commodity.Morale] += Tuning.Crawler.MoraleSurrendered;

            Winner.Message($"{Loser.Name} surrenders and gives you {surrenderInv}");
            Loser.Message($"You surrender to {Winner.Name} and give {surrenderInv}");

            // Time consumption for processing surrender
            Winner.ConsumeTime("AcceptSurrender", 300, ExpectedDuration);
            Loser.ConsumeTime("Surrendered", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            if (Loser is not Crawler loser) return Immediacy.Failed;
            if (!loser.IsVulnerable || !loser.Lives()) return Immediacy.Failed;
            if (Loser.To(Winner).Surrendered) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Tuning.Crawler.SurrenderTime;
    }
}

/// <summary>
/// Shared extortion interaction records used by both player demands and bandit extortion.
/// </summary>
public static class ExtortionInteractions {
    /// <summary>
    /// Interaction where extortioner demands cargo from target.
    /// Target complies and hands over the demanded items.
    /// </summary>
    public record AcceptExtortionInteraction(
        IActor Extortioner,
        IActor Target,
        Func<IOffer> MakeDemand,
        string DescriptionText,
        string MenuOption
    ) : Interaction(Extortioner, Target, MenuOption) {

        public override string Description => DescriptionText;

        public override string? MessageFor(IActor viewer) {
            if (viewer != Target) return null;
            var ultimatum = Extortioner.To(Target).Ultimatum;
            if (ultimatum?.ExpirationTime.IsValid == true) {
                return $"You have until {Game.TimeString(ultimatum.ExpirationTime)} to surrender goods";
            }
            return null;
        }

        public override bool Perform(string args = "") {
            var demand = MakeDemand();

            Extortioner.Message($"{Target.Name} hands over {demand.Description}");
            Target.Message($"You hand over {demand.Description} to {Extortioner.Name}");

            demand.PerformOn(Target, Extortioner);

            // Clear ultimatum if it exists
            if (Extortioner.To(Target).Ultimatum != null) {
                Extortioner.To(Target).Ultimatum = null;
            }

            // Mark as surrendered
            Target.To(Extortioner).Surrendered = true;

            // Record risk event for intelligence network
            RecordExtortionRisk(Target, Extortioner);

            // Time consumption for handing over extorted goods
            Extortioner.ConsumeTime("Extorting", 300, ExpectedDuration);
            Target.ConsumeTime("Extorted", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") {
            var demand = MakeDemand();
            if (demand.DisabledFor(Target, Extortioner) != null) return Immediacy.Failed;
            if (Target.To(Extortioner).Surrendered) return Immediacy.Failed;
            return Immediacy.Menu;
        }

        public override TimeDuration ExpectedDuration => Tuning.Crawler.ExtortionTime;
    }

    /// <summary>
    /// Interaction where target refuses extortion demand.
    /// Results in hostility between both parties.
    /// </summary>
    public record RefuseExtortionInteraction(
        IActor Extortioner,
        IActor Target,
        string MenuOption
    ) : Interaction(Extortioner, Target, MenuOption) {

        public override string Description => $"Refuse extortion from {Extortioner.Name}";

        public override string? MessageFor(IActor viewer) {
            if (viewer != Target) return null;
            var ultimatum = Extortioner.To(Target).Ultimatum;
            if (ultimatum?.ExpirationTime.IsValid == true) {
                return $"You have until {Game.TimeString(ultimatum.ExpirationTime)} to surrender goods";
            }
            return null;
        }

        public override bool Perform(string args = "") {
            Extortioner.SetHostileTo(Target, true);
            Target.SetHostileTo(Extortioner, true);

            Extortioner.Message($"{Target.Name} refuses your demand!");
            Target.Message($"You refuse {Extortioner.Name}'s demand - now hostile!");

            // Clear ultimatum if it exists
            if (Extortioner.To(Target).Ultimatum != null) {
                Extortioner.To(Target).Ultimatum = null;
            }

            // Time consumption for refusing extortion
            Extortioner.ConsumeTime("RefusedBy", 300, ExpectedDuration);
            Target.ConsumeTime("Refusing", 300, ExpectedDuration);

            return true;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Menu;

        public override TimeDuration ExpectedDuration => Tuning.Crawler.RefuseTime;
    }

    /// <summary>
    /// Automatic interaction when extortion ultimatum expires.
    /// Extortioner attacks the target.
    /// </summary>
    public record ExtortionExpiredInteraction(
        IActor Extortioner,
        IActor Target,
        string MenuOption
    ) : Interaction(Extortioner, Target, MenuOption) {

        public override string Description => $"Extortion expired - {Extortioner.Name} attacks!";

        public override bool Perform(string args = "") {
            Extortioner.To(Target).Ultimatum = null;
            if (Extortioner is Crawler extortioner) {
                extortioner.SetHostileTo(Target, true);
            }


            return false;
        }

        public override Immediacy GetImmediacy(string args = "") => Immediacy.Immediate;

        public override TimeDuration ExpectedDuration => TimeDuration.Zero; // Auto-trigger, no time consumed
    }

    /// <summary>
    /// Record an extortion event to the victim's risk tracker and faction network.
    /// </summary>
    public static void RecordExtortionRisk(IActor victim, IActor extortioner) {
        if (victim is not Crawler crawler) return;

        // Find the victim's route knowledge component
        var routeKnowledge = crawler.Components
            .OfType<RouteKnowledgeComponent>().FirstOrDefault();
        if (routeKnowledge == null) return;

        // Find current road (if traveling) - for now just record at location
        // TODO: Integrate with TransitRegistry to get actual road when in transit
        var location = crawler.Location;
        if (location == null) return;

        var network = location.Map?.TradeNetwork;
        if (network == null) return;

        // Try to find a nearby road to associate the event with
        var roads = network.RoadsFrom(location).ToList();
        if (roads.Count == 0) return;

        // Record on the first available road (the one we arrived on or are leaving from)
        var road = roads[0];
        routeKnowledge.ReportDanger(
            road,
            RiskEventType.BanditExtortion,
            extortioner.Faction,
            severity: Tuning.Combat.ExtortionRiskSeverity,
            survived: true
        );
    }
}

/// <summary>
/// Component that allows player to threaten vulnerable NPCs for cargo
/// </summary>
public class PlayerDemandComponent : ActorComponentBase {
    XorShift _rng;
    float _demandFraction;
    string _optionCode;

    public PlayerDemandComponent(ulong seed, float demandFraction = 0.5f, string optionCode = "X") {
        _rng = new XorShift(seed);
        _demandFraction = demandFraction;
        _optionCode = optionCode;
    }


    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Feasibility: player only, subject is vulnerable non-player, not hostile, not surrendered
        if (Owner.Faction != Factions.Player) yield break;
        if (Owner is not Crawler { IsDisarmed: false }) yield break;
        if (subject is not Crawler { IsVulnerable: true }) yield break;
        if (subject.Faction == Factions.Player) yield break;
        if (Owner.To(subject).Hostile) yield break;
        if (subject.To(Owner).Surrendered) yield break;

        // Use shared extortion interaction
        IOffer MakeDemand() => new CompoundOffer(
            subject.SupplyOffer(_rng/1, _demandFraction),
            subject.CargoOffer(_rng/2, (_demandFraction + 1) / 2)
        );

        yield return new ExtortionInteractions.AcceptExtortionInteraction(
            Owner,
            subject,
            MakeDemand,
            $"Threaten {subject.Name} for cargo",
            _optionCode
        );
    }
}

/// <summary>
/// Base class for combat components that provides shared combat functionality.
/// Handles target tracking and provides helper methods for combat behavior.
/// </summary>
public abstract class CombatComponentBase : ActorComponentBase {
    protected XorShift _rng;

    public CombatComponentBase(ulong seed) {
        _rng = new XorShift(seed);
    }

    /// <summary>
    /// Current target this component is tracking.
    /// </summary>
    protected IActor? CurrentTarget { get; set; }

    /// <summary>
    /// Select a hostile target to attack. Returns null if no valid target found.
    /// Can be overridden to implement custom target selection logic.
    /// </summary>
    protected virtual IActor? SelectTarget() {
        // TODO: Rank based on faction scores, after we have faction scores
        var actors = PotentialTargets();
        return _rng.ChooseRandom(actors.Where(a => Owner.To(a).Hostile));
    }
    protected IEnumerable<IActor> PotentialTargets() {
        return Owner.Location.GetEncounter().ActorsExcept(Owner).Where(IsValidTarget);
    }

    protected virtual bool IsValidTarget(IActor? target) {
        return target is not null &&
               target.Lives() &&
               Owner.Location == target.Location &&
               Owner.HostileTo(target);
    }

    /// <summary>
    /// Attempt to attack the current or selected target.
    /// Returns AP cost if attack was performed, null otherwise.
    /// </summary>
    protected ActorEvent? AttackTarget(IActor target) {
        if (Owner is not Crawler attacker) return null;
        if (attacker.IsDisarmed) return null;

        var (ready, waiting) = attacker.CycleDelays();
        bool preFire = ready.Length > 0;
        bool postFire = waiting.Length > 0;
        TimeDuration duration = TimeDuration.Zero;
        if (preFire) {
            duration = ready[0].CycleLength;
            if (postFire && waiting[0].Cycle < duration) {
                duration = waiting[0].Cycle;
            }
        } else if (postFire) {
            // Just wait
            duration = waiting[0].Cycle;
        } else {
            // No weapons
            return null;
        }
        Debug.Assert(duration.IsPositive);
        int priority = EventPriority.ForAttack(attacker, target);
        return Owner.NewEventFor($"Volley at {target}", priority, duration,
            preFire ? () => attacker.Attack(target) : null);
    }

    public override void Attach(IActor owner) {
        base.Attach(owner);
        Owner.ReceivingFire += OnReceivingFire;
    }

    public override void Detach() {
        Owner.ReceivingFire -= OnReceivingFire;
        base.Detach();
    }

    void OnReceivingFire(IActor actor, List<HitRecord> fire) {
        Owner.SetHostileTo(actor, true);
        if (actor is ActorScheduled scheduled &&
            Owner is ActorScheduled ownerScheduled) {
            ownerScheduled.IdleUntil("ReceivingFire", scheduled.Time);
        }

        // Record combat risk event
        RecordCombatRisk(Owner, actor, fire);
    }

    /// <summary>
    /// Record a combat event to the victim's risk tracker and faction network.
    /// </summary>
    static void RecordCombatRisk(IActor victim, IActor attacker, List<HitRecord> fire) {
        if (victim is not Crawler crawler) return;
        if (fire.Count == 0) return;

        // Find the victim's route knowledge component
        var routeKnowledge = crawler.Components
            .OfType<RouteKnowledgeComponent>().FirstOrDefault();
        if (routeKnowledge == null) return;

        var location = crawler.Location;
        if (location == null) return;

        var network = location.Map?.TradeNetwork;
        if (network == null) return;

        var roads = network.RoadsFrom(location).ToList();
        if (roads.Count == 0) return;

        // Calculate severity based on damage taken
        float totalDamage = fire.Sum(h => h.Damage);
        float severity = Math.Clamp(totalDamage / Tuning.Combat.DamageSeverityCap, 0.1f, 1.0f);

        // Determine risk event type
        var riskType = attacker is Crawler { Role: Roles.Bandit }
            ? RiskEventType.BanditAttack
            : RiskEventType.CrawlerCombat;

        var road = roads[0];
        routeKnowledge.ReportDanger(
            road,
            riskType,
            attacker.Faction,
            severity: severity,
            survived: victim.Lives()
        );
    }

    public override void Enter(Encounter encounter) {
        encounter.ActorArrived += OnActorArrived;
        encounter.ActorLeft += OnActorLeft;
    }

    public override void Leave(Encounter encounter) {
        encounter.ActorArrived -= OnActorArrived;
        encounter.ActorLeft -= OnActorLeft;
    }

    protected virtual void OnActorArrived(IActor actor, TimePoint time) {
        if (Owner is not Crawler bandit) return;
        if (actor == Owner) return;
    }

    protected virtual void OnActorLeft(IActor actor, TimePoint time) {
        if (Owner is not Crawler bandit) return;
        if (actor == Owner) return;

        // Clear ultimatum when target leaves
        bandit.To(actor).Ultimatum = null;
    }

    protected IActor? ChooseTarget() {
        if (!IsValidTarget(CurrentTarget)) {
            CurrentTarget = SelectTarget();
            if (CurrentTarget != null && !IsValidTarget(CurrentTarget)) {
                throw new InvalidOperationException($"Chose an invalid target: {CurrentTarget?.Name}");
            }
        }
        return CurrentTarget;
    }

    public override ActorEvent? GetNextEvent() {
        var target = ChooseTarget();
        if (target is not null) {
            return AttackTarget(target);
        }
        return null;
    }
}

/// <summary>
/// Advanced combat AI component with smart targeting that prioritizes vulnerable enemies.
/// Subscribes to OnHostilityChanged events to react to new hostilities.
/// </summary>
public class CombatComponentAdvanced : CombatComponentBase {
    public CombatComponentAdvanced(ulong seed) : base(seed) {
    }

    public override int Priority => 600; // Advanced combat behavior

    public override void Attach(IActor actor) {
        base.Attach(actor);
        if (actor is Crawler crawler) {
            crawler.HostilityChanged += HostilityChanged;
        }
    }

    void HostilityChanged(IActor other, bool hostile) {
        // React to hostility changes - could retarget or take immediate action
        if (hostile && CurrentTarget == null) {
            CurrentTarget = other;
        }
    }

    /// <summary>
    /// Advanced targeting: prioritize vulnerable targets over healthy ones.
    /// </summary>
    protected override IActor? SelectTarget() {
        if (Owner is not Crawler crawler) return null;
        var actorList = Owner.Location.GetEncounter().CrawlersExcept(Owner).ToList();

        // Priority 1: Attack vulnerable hostiles first (easy targets)
        var vulnerableHostile = _rng.ChooseRandom(actorList
            .Where(a => crawler.To(a).Hostile && a.IsVulnerable && a.Lives()));

        if (vulnerableHostile != null) {
            return vulnerableHostile;
        }

        // Priority 2: Attack any hostile
        return base.SelectTarget();
    }
}

/// <summary>
/// Generic hostile AI component that attacks enemies.
/// Used as fallback behavior for NPCs that should fight but have no specialized AI.
/// </summary>
public class CombatComponentDefense : CombatComponentBase {
    public CombatComponentDefense(ulong seed) : base(seed) {
    }

    public override int Priority => 400; // Generic combat behavior
}
