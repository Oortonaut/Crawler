using Crawler.Economy;
using Crawler.Network;

namespace Crawler.Convoy;

/// <summary>
/// AI component that decides whether to join, form, or leave convoys.
/// Used by Trader and Traveler roles for risk-based convoy decisions.
/// </summary>
public class ConvoyDecisionComponent : ActorComponentBase {
    readonly XorShift _rng;

    // Personality factors (set at creation for AI variety)
    readonly float _riskAversion;      // 0-1: higher = more likely to join convoys
    readonly float _independenceValue; // 0-1: higher = prefers traveling alone
    readonly float _socialTrust;       // 0-1: higher = trusts convoy members more

    // Current destination (set by TraderAI or other components)
    Location? _intendedDestination;

    public ConvoyDecisionComponent(ulong seed) {
        _rng = new XorShift(seed);
        _riskAversion = _rng.NextSingle();
        _independenceValue = _rng.NextSingle();
        _socialTrust = _rng.NextSingle();
    }

    public override int Priority => 450; // Below convoy coordination, above trade

    /// <summary>Set the intended destination for convoy decisions.</summary>
    public void SetDestination(Location destination) {
        _intendedDestination = destination;
    }

    /// <summary>Evaluate whether to join an existing convoy heading our direction.</summary>
    public bool ShouldJoinConvoy(Convoy convoy) {
        if (Owner is not Crawler crawler) return false;
        if (_intendedDestination == null) return false;

        // Check route compatibility - our destination must be on their route
        if (!convoy.Route.Contains(_intendedDestination)) return false;

        // Check relation with leader
        var leaderRelation = crawler.To(convoy.Leader);
        if (leaderRelation.Hostile) return false;

        // Get route risk
        var network = crawler.Location.Map.TradeNetwork;
        if (network == null) return false;

        var soloPath = network.FindPath(crawler.Location, _intendedDestination);
        if (soloPath == null) return false;

        // Get risk from faction network
        var factionNetwork = FactionRiskNetworks.GetNetwork(crawler.Faction);
        float soloRisk = factionNetwork.RiskTracker.GetRouteRisk(soloPath, crawler.Time, network);

        // Evaluate convoy strength
        float convoyStrength = convoy.CombinedFirepower + convoy.CombinedDefense;
        float myStrength = crawler.OffenseSegments.OfType<WeaponSegment>().Sum(s => s.Damage) +
                          crawler.DefenseSegments.OfType<ArmorSegment>().Sum(s => s.Reduction);

        // Decision factors:
        // - Higher solo risk = more likely to join
        // - Higher convoy strength = more attractive
        // - Higher independence = less likely to join
        // - Trust affects how much we value convoy protection

        float riskReduction = Math.Min(1.0f, convoyStrength / (soloRisk * 100 + 1));
        float joinScore = (soloRisk * _riskAversion * 2.0f) +
                         (riskReduction * _socialTrust * 0.5f) -
                         (_independenceValue * 0.3f);

        // Add randomness for variety
        joinScore += (_rng.NextSingle() - 0.5f) * 0.2f;

        return joinScore > 0.5f;
    }

    /// <summary>Evaluate whether to form a new convoy when none suitable exists.</summary>
    public bool ShouldFormConvoy(Location destination) {
        if (Owner is not Crawler crawler) return false;

        var network = crawler.Location.Map.TradeNetwork;
        if (network == null) return false;

        var path = network.FindPath(crawler.Location, destination);
        if (path == null) return false;

        // Get route risk
        var factionNetwork = FactionRiskNetworks.GetNetwork(crawler.Faction);
        float routeRisk = factionNetwork.RiskTracker.GetRouteRisk(path, crawler.Time, network);

        // Form convoy if route is risky enough
        float formThreshold = Tuning.Convoy.ConvoyFormationRiskThreshold +
                             (1 - _riskAversion) * 0.3f;

        // Reduce threshold if we have valuable cargo
        float cargoValue = crawler.Supplies.ValueAt(crawler.Location);
        if (cargoValue > 1000) {
            formThreshold *= 0.8f;
        }

        return routeRisk > formThreshold;
    }

    /// <summary>Evaluate whether to leave current convoy.</summary>
    public bool ShouldLeaveConvoy() {
        var convoy = ConvoyRegistry.GetConvoy(Owner);
        if (convoy == null) return false;

        if (Owner is not Crawler crawler) return true;

        // Leave if we've reached our destination
        if (_intendedDestination != null && crawler.Location == _intendedDestination) {
            return true;
        }

        // Leave if convoy is going somewhere we don't want
        if (_intendedDestination != null && !convoy.Route.Contains(_intendedDestination)) {
            return true;
        }

        // Leave if relation with leader has soured
        var leaderRelation = crawler.To(convoy.Leader);
        if (leaderRelation.Hostile) return true;

        // Random chance to leave based on independence (very small)
        if (_rng.NextSingle() < _independenceValue * 0.02f) return true;

        return false;
    }

    public override ActorEvent? GetNextEvent() {
        if (Owner is not Crawler crawler) return null;

        var convoy = ConvoyRegistry.GetConvoy(crawler);

        if (convoy != null) {
            // We're in a convoy - check if we should leave
            if (ShouldLeaveConvoy()) {
                convoy.RemoveMember(crawler);
                crawler.Message($"{crawler.Name} left the convoy.");
            }
            return null; // Let ConvoyComponent handle travel
        }

        // Not in convoy - check if we should join or form one
        if (_intendedDestination == null) return null;

        // Look for compatible convoys at current location
        foreach (var existingConvoy in ConvoyRegistry.ConvoysAt(crawler.Location)) {
            if (ShouldJoinConvoy(existingConvoy)) {
                existingConvoy.AddMember(crawler);
                crawler.Message($"{crawler.Name} joined convoy to {existingConvoy.Destination?.Description}.");
                return null;
            }
        }

        // No suitable convoy - consider forming one
        if (ShouldFormConvoy(_intendedDestination)) {
            var network = crawler.Location.Map.TradeNetwork;
            if (network != null) {
                var route = network.FindPath(crawler.Location, _intendedDestination);
                if (route != null) {
                    var newConvoy = ConvoyRegistry.Create(crawler, route);
                    crawler.Message($"{crawler.Name} formed convoy to {_intendedDestination.Description}.");
                    return null;
                }
            }
        }

        return null;
    }

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // If subject has a convoy going our direction, offer to join
        var subjectConvoy = ConvoyRegistry.GetConvoy(subject);
        if (subjectConvoy != null && _intendedDestination != null) {
            if (subjectConvoy.Route.Contains(_intendedDestination)) {
                if (!ConvoyRegistry.IsInConvoy(Owner)) {
                    yield return new OfferToJoinConvoyInteraction(Owner, subject, subjectConvoy, "OJ");
                }
            }
        }
    }
}

/// <summary>
/// Interaction where NPC offers to join player's convoy.
/// </summary>
public record OfferToJoinConvoyInteraction(
    IActor Mechanic,  // The NPC offering to join
    IActor Subject,   // The convoy leader (player)
    Convoy Convoy,
    string MenuOption
) : Interaction(Mechanic, Subject, MenuOption) {

    public override string Description =>
        $"{Mechanic.Name} offers to join your convoy";

    public override Immediacy GetImmediacy(string args = "") {
        // Only show if subject is leader
        if (Convoy.Leader != Subject) return Immediacy.Failed;

        // Check relation
        var relation = Subject.To(Mechanic);
        if (relation.Hostile) return Immediacy.Failed;

        return Immediacy.Menu;
    }

    public override bool Perform(string args = "") {
        Convoy.AddMember(Mechanic);
        Subject.Message($"{Mechanic.Name} joined your convoy.");
        Mechanic.Message($"Joined convoy to {Convoy.Destination?.Description}.");
        return true;
    }

    public override TimeDuration ExpectedDuration => TimeDuration.FromMinutes(1);
}

/// <summary>
/// Component that allows actors to purchase route risk intel from factions.
/// Similar to LicenseComponent but for risk information.
/// </summary>
public class RiskIntelComponent : ActorComponentBase {
    readonly XorShift _rng;

    public RiskIntelComponent(ulong seed) {
        _rng = new XorShift(seed);
    }

    public override int Priority => 400;

    public override IEnumerable<Interaction> EnumerateInteractions(IActor subject) {
        // Only settlements sell risk intel
        if (Owner is not Crawler settlement) yield break;
        if (!settlement.Flags.HasFlag(ActorFlags.Settlement)) yield break;

        var faction = settlement.Faction;
        if (!faction.IsCivilian()) yield break;

        // Offer to sell risk intel license
        yield return new BuyRiskIntelInteraction(subject, Owner, faction, "RI");
    }
}

/// <summary>
/// Interaction to purchase access to a faction's risk intelligence network.
/// </summary>
public record BuyRiskIntelInteraction(
    IActor Mechanic,  // The buyer
    IActor Subject,   // The settlement
    Factions Faction,
    string MenuOption
) : Interaction(Mechanic, Subject, MenuOption) {

    public override string Description =>
        $"Buy risk intel license ({Faction.Name()}) - {Tuning.Convoy.RiskIntelLicenseCost:F0} scrap";

    public override Immediacy GetImmediacy(string args = "") {
        // Check if can afford
        if (Mechanic.Supplies[Commodity.Scrap] < Tuning.Convoy.RiskIntelLicenseCost) {
            return Immediacy.Failed;
        }

        return Immediacy.Menu;
    }

    public override bool Perform(string args = "") {
        float cost = Tuning.Convoy.RiskIntelLicenseCost;

        if (Mechanic.Supplies[Commodity.Scrap] < cost) {
            Mechanic.Message("Not enough scrap.");
            return false;
        }

        Mechanic.Supplies[Commodity.Scrap] -= cost;
        Subject.Supplies[Commodity.Scrap] += cost;

        // Transfer faction's risk knowledge to buyer
        var factionNetwork = FactionRiskNetworks.GetNetwork(Faction);

        // Get or create buyer's risk knowledge
        if (Mechanic is Crawler crawler) {
            var knowledgeComponent = crawler.Components
                .OfType<RouteKnowledgeComponent>().FirstOrDefault();
            if (knowledgeComponent != null) {
                knowledgeComponent.RiskTracker.MergeKnowledge(
                    factionNetwork.RiskTracker, crawler.Time);
                Mechanic.Message($"Acquired risk intel from {Faction.Name()}.");
            } else {
                Mechanic.Message($"Unable to process risk intel.");
            }
        }

        return true;
    }

    public override TimeDuration ExpectedDuration => TimeDuration.FromMinutes(5);
}

/// <summary>
/// Component that tracks and shares route risk information for an actor.
/// </summary>
public class RouteKnowledgeComponent : ActorComponentBase {
    public RouteRiskTracker RiskTracker { get; } = new();

    public override int Priority => 100;

    public override void Enter(Encounter encounter) {
        // Share knowledge with others at location
        foreach (var other in encounter.Actors) {
            if (other == Owner) continue;

            var otherKnowledge = (other as Crawler)?.Components
                .OfType<RouteKnowledgeComponent>().FirstOrDefault();

            if (otherKnowledge != null) {
                // Exchange risk information (mutual)
                RiskTracker.MergeKnowledge(otherKnowledge.RiskTracker, Owner.Time);
                otherKnowledge.RiskTracker.MergeKnowledge(RiskTracker, Owner.Time);
            }
        }
    }

    /// <summary>Report a dangerous event that occurred to this actor.</summary>
    public void ReportDanger(Road road, RiskEventType type,
        Factions? attackerFaction = null, float severity = 0.5f, bool survived = true) {
        var evt = new RiskEvent(road, Owner.Time, type, attackerFaction, severity) {
            Survived = survived
        };
        RiskTracker.RecordEvent(evt);

        // Also report to faction network if we're in civilized space
        if (Owner is Crawler crawler && crawler.Faction.IsCivilian()) {
            FactionRiskNetworks.PropagateEvent(evt, crawler.Faction, crawler.Location.Map);
        }
    }
}
