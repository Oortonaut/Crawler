using System.Data;
using Crawler.Logging;

namespace Crawler;

/// <summary>
/// Cached commodity enumeration to avoid repeated Enum.GetValues calls in hot paths
/// </summary>
static file class CommodityCache {
    public static readonly Commodity[] AllCommodities = Enum.GetValues<Commodity>();
}

/// <summary>
/// Three-level interaction system for actor interactions.
/// Proposals check capabilities and generate IInteractions.
/// See: docs/SYSTEMS.md#proposalinteraction-system
/// </summary>
/// Naming: Proposal[ActorVerb][SubjectVerb]
public interface IProposal {
    /// <summary>Can the agent make this proposal?</summary>
    bool AgentCapable(IActor agent);

    /// <summary>Can the subject receive this proposal?</summary>
    bool SubjectCapable(IActor subject);

    /// <summary>Can the combo interact? For performance, if no interaction is possible
    /// then GetInteractions() shouldn't yield any.</summary>
    bool InteractionCapable(IActor Agent, IActor Subject);

    /// <summary>Display description for debugging (Proposals aren't usually user visible)</summary>
    string Description { get; }

    /// <summary>Expiration time for proposals</summary>
    ///  0 for no expiration
    ///  -1 or t < Game.Instance.Time for immediate expiration.
    ///  t >= Game.Instance.Time for future termination.
    long ExpirationTime { get; }

    /// <summary>
    /// Generate concrete interactions.
    /// May yield multiple interactions (e.g., Accept and Refuse for demands).
    /// </summary>
    IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject);
}

public static class IProposalEx {
    public static bool Test(this IProposal proposal, IActor Agent, IActor Subject) {
        using var activity = LogCat.Interaction.StartActivity($"Test {proposal.Description} {Agent.Name} {Subject.Name}");
        activity?.SetTag("proposal.description", proposal.Description);
        activity?.SetTag("agent.name", Agent.Name);
        activity?.SetTag("subject.name", Subject.Name);

        bool ac = proposal.AgentCapable(Agent);
        bool sc = proposal.SubjectCapable(Subject);
        bool ic = proposal.InteractionCapable(Agent, Subject);
        bool result = ac && sc && ic;

        if (!result) {
            var failures = new List<string>();
            if (!ac) failures.Add("Agent");
            if (!sc) failures.Add("Subject");
            if (!ic) failures.Add("Interaction");
            activity?.SetTag("failures", string.Join(' ', failures));
        }

        activity?.SetTag("result", result);
        activity?.SetTag("agent.capable", ac);
        activity?.SetTag("subject.capable", sc);
        activity?.SetTag("interaction.capable", ic);

        return result;
    }
    public static IEnumerable<IInteraction> TestGetInteractions(this IProposal proposal, IActor Agent, IActor Subject) {
        using var activity = LogCat.Interaction.StartActivity(nameof(IProposalEx.TestGetInteractions))?
            .SetTag("Proposal", proposal.Description)
            .SetTag("Agent", Agent.Name).SetTag("Subject", Subject.Name);

        bool passed = proposal.Test(Agent, Subject);
        activity?.SetTag("test.passed", passed);

        if (passed) {
            var interactions = proposal.GetInteractions(Agent, Subject);
            //activity?.SetTag("interaction.count", interactions.Length);

            // var interactionDetails = interactions.Select(i =>
            //     $"{i.Description}: {i.Immediacy()}").ToArray();
            // activity?.SetTag("interactions", string.Join("; ", interactionDetails));

            return interactions;
        } else {
            return [];
        }
    }
}

// Agent is seller
public record ProposeExchange(
    IOffer agentOffer,
    IOffer subjectOffer,
    string OptionCode = "T"): IProposal {
    public virtual bool AgentCapable(IActor agent) => true;
    public virtual bool SubjectCapable(IActor subject) => true;
    public virtual bool InteractionCapable(IActor Agent, IActor Subject) =>
        Agent != Subject &&
        agentOffer.DisabledFor(Agent, Subject) == null &&
        subjectOffer.DisabledFor(Subject, Agent) == null &&
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Hostile;
    public virtual IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new ExchangeInteraction(Agent, agentOffer, Subject, subjectOffer, OptionCode, Description);
    }
    // Description is from the subjects POV
    public virtual string Description => $"Trade your {agentOffer.Description} for {subjectOffer.Description}";
    public virtual long ExpirationTime { get; set; } = 0;
    public override string ToString() => Description;
}

public record ProposeAgentExchange(
    IActor Agent,
    IOffer agentOffer,
    IOffer subjectOffer,
    string OptionCode = "T"): ProposeExchange(agentOffer, subjectOffer, OptionCode) {
    public override bool AgentCapable(IActor agent) => agent == Agent && base.AgentCapable(agent);
}

public record ProposeSubjectExchange(
    IOffer agentOffer,
    IActor Subject,
    IOffer subjectOffer,
    string OptionCode = "T"): ProposeExchange(agentOffer, subjectOffer, OptionCode) {
    public override bool SubjectCapable(IActor subject) => subject == Subject && base.SubjectCapable(subject);
}

// I propose that I give you my loot
record ProposeLootTake(string OptionCode, string verb = "Loot"): IProposal {
    public bool AgentCapable(IActor agent) =>
        agent is Crawler wreck &&
        wreck.EndState != null &&
        !wreck.HasFlag(EActorFlags.Looted);
    public bool SubjectCapable(IActor subject) => true;
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        string description = $"{verb} {Agent.Name}";
        yield return new ExchangeInteraction(Agent, Agent.SupplyOffer(Tuning.Game.LootReturn), Subject, new EmptyOffer(), OptionCode, description);
    }
    public string Description => $"Offer {verb}";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}

// I propose that you harvest my resources
record ProposeHarvestTake(IActor Resource, Inventory Amount, string OptionCode, string verb): IProposal {
    public bool AgentCapable(IActor agent) => agent == Resource && (agent.Flags & EActorFlags.Looted) == 0;
    public bool SubjectCapable(IActor subject) => subject != Resource;
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        string description = $"{verb} {Agent.Name}";
        yield return new ExchangeInteraction(Agent, Agent.SupplyOffer(), Subject, new EmptyOffer(), OptionCode, description);
    }
    public string Description => $"Propose {verb}";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
    // public record HarvestOffer(IActor Resource, Inventory Amount): InventoryOffer(Resource, Amount) {
    //     public override void PerformOn(IActor Subject) {
    //         base.PerformOn(Subject);
    //         Agent.Flags |= EActorFlags.Looted;
    //         Agent.End(EEndState.Destroyed, "has been harvested");
    //     }
    // }
}

record ProposeLootPay(IActor Resource, Inventory Risk, float Chance): IProposal {
    public bool AgentCapable(IActor agent) => agent == Resource && !agent.HasFlag(EActorFlags.Looted);
    public bool SubjectCapable(IActor subject) => subject != Resource;
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new ExchangeInteraction(
            Agent, Agent.SupplyOffer(Chance),
            Subject, new InventoryOffer(false, Risk),
            "H");
    }
    public string Description => $"Explore {Resource.Name}";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}

public record ProposeAttackDefend(string OptionCode): IProposal {
    public bool AgentCapable(IActor agent) => agent == Game.Instance?.Player;
    public bool SubjectCapable(IActor subject) => subject.Lives();
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var description = $"{Description} {Subject.Name}";
        yield return new ExchangeInteraction(Agent, new AttackOffer(), Subject, new EmptyOffer(), OptionCode, description);
    }
    public long ExpirationTime => 0;
    public string Description => "Attack";
}

// a proposal that I accept your surrender
public record ProposeAcceptSurrender(string OptionCode): IProposal {
    Inventory MakeSurrenderInv(IActor Loser) {
        Inventory surrenderInv = new();
        float ratio = 0;
        if (Loser is Crawler loser) {
            float totalHits = loser.Segments.Sum(s => s.Hits);
            float totalMaxHits = loser.Segments.Sum(s => s.MaxHits);
            ratio = Math.Min(totalHits, 1) / Math.Min(totalMaxHits, 1);
            ratio = (float)Math.Pow(ratio, 0.8);
        }
        surrenderInv.Add(Loser.Supplies.Loot(ratio));
        if (Loser.Supplies != Loser.Cargo) {
            surrenderInv.Add(Loser.Cargo.Loot(ratio));
        }

        return surrenderInv;
    }

    public bool AgentCapable(IActor Winner) => true;
    public bool SubjectCapable(IActor subject) =>
        subject is Crawler loser && loser.IsVulnerable && loser.Lives();
    public bool InteractionCapable(IActor Winner, IActor Loser) =>
        Winner != Loser &&
        Winner.To(Loser).Hostile &&
        !Loser.To(Winner).Surrendered;
    public IEnumerable<IInteraction> GetInteractions(IActor Winner, IActor Loser) {
        var surrenderInv = MakeSurrenderInv(Loser);
        string description = $"{Loser.Name} Surrender";
        float value = surrenderInv.ValueAt(Winner.Location);
        yield return new ExchangeInteraction(
            Winner, new AcceptSurrenderOffer(value, description),
            Loser, new InventoryOffer(false, surrenderInv), OptionCode, description);
    }
    public string Description => $"SurrenderAccept";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}


public record ProposeDemand(
    IOffer agentOfferComply,
    IOffer agentOfferRefuse,
    IOffer? subjectOfferDemanded,
    string Ultimatum,
    int timeout = 300,
    string OptionCode = "D"): IProposal {
    public virtual bool AgentCapable(IActor agent) => true;
    public virtual bool SubjectCapable(IActor subject) => true;
    public virtual bool InteractionCapable(IActor Agent, IActor Subject) =>
        Agent != Subject &&
        agentOfferComply.DisabledFor(Agent, Subject) == null &&
        agentOfferRefuse.DisabledFor(Agent, Subject) == null &&
        SubjectOffer(Subject).DisabledFor(Subject, Agent) == null;

    public virtual IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        if (ExpirationTime == 0 || Game.SafeTime <= ExpirationTime) {
            yield return new ExchangeInteraction(Agent, agentOfferComply, Subject, SubjectOffer(Subject), OptionCode + "Y", Ultimatum);
            yield return new ExchangeInteraction(Agent, agentOfferRefuse, Subject, new EmptyOffer(), OptionCode + "N", Ultimatum);
        } else {
            yield return new ExchangeInteraction(Agent, agentOfferRefuse, Subject, new EmptyOffer(), "", Ultimatum, Immediacy.Immediate);
        }
    }
    protected virtual IOffer SubjectOffer(IActor subject) => subjectOfferDemanded ?? throw new InvalidDataException($"You must override {nameof(SubjectOffer)} or specify {nameof(ProposeDemand)}");

    public virtual string Description => Ultimatum;
    public virtual long ExpirationTime { get; set; } = Game.SafeTime + timeout;
    public override string ToString() => Description;

}

// Bandit extortion: "Hand over cargo or I attack"
public record ProposeAttackOrLoot(float DemandFraction = 0.5f) 
    : ProposeDemand(
        new EmptyOffer(),
        new AttackOffer(), 
        null,
        "Extort",
        OptionCode: "D") {

    public override bool InteractionCapable(IActor Agent, IActor Subject) =>
        base.InteractionCapable(Agent, Subject) &&
        !Agent.To(Subject).Hostile &&
        !Agent.To(Subject).Surrendered;

    protected override IOffer SubjectOffer(IActor subject) => subject.SupplyOffer(DemandFraction);
    public override string Description => "Extort cargo";
}

// Civilian faction taxes: "Pay taxes or face hostility"
public record ProposeTaxes(float TaxRate = 0.05f)
    : ProposeDemand(
        new EmptyOffer(),
        new HostilityOffer("refuses to pay taxes"),
        null,
        "Demand taxes",
        OptionCode: "D") {

    public override bool AgentCapable(IActor agent) =>
        (agent.Flags & EActorFlags.Settlement) != 0 && agent.Faction.IsCivilian();

    public override bool SubjectCapable(IActor subject) =>
        subject.Faction == Faction.Player;

    public override bool InteractionCapable(IActor Agent, IActor Subject) =>
        base.InteractionCapable(Agent, Subject) && 
        Agent.Location.Sector.ControllingFaction == Agent.Faction &&
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Hostile;

    protected override IOffer SubjectOffer(IActor subject) {
        float cargoValue = subject.Supplies.ValueAt(subject.Location);
        float taxAmount = cargoValue * TaxRate;
        return new ScrapOffer(taxAmount);
    }

    public override string Description => "Demand taxes";
}

// Contraband seizure: "Surrender prohibited goods or pay fine"
public record ProposeContrabandSeizure(Inventory Contraband, float PenaltyAmount)
    : ProposeDemand(
        new EmptyOffer(),
        new HostilityOffer("refuses to surrender contraband"),
        null,
        "Seize contraband",
        OptionCode: "C") {

    public override bool AgentCapable(IActor agent) =>
        (agent.Flags & EActorFlags.Settlement) != 0 ||
        (agent.Faction == Faction.Independent || agent.Faction.IsCivilian());

    public override bool SubjectCapable(IActor subject) =>
        subject.Faction == Faction.Player && subject.Supplies.Contains(Contraband) != ContainsResult.False;

    public override bool InteractionCapable(IActor Agent, IActor Subject) =>
        base.InteractionCapable(Agent, Subject) &&
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Hostile &&
        Agent.To(Subject).StoredProposals.Any(p => p is ProposeContrabandSeizure);

    protected override IOffer SubjectOffer(IActor subject) => new InventoryOffer(false, Contraband);

    public override string Description => "Seize contraband";
}

// Player demands: Let player threaten vulnerable NPCs
public record ProposePlayerDemand(float DemandFraction = 0.5f, string OptionCode = "X")
    : ProposeDemand(
        new EmptyOffer(),
        new AttackOffer(),
        null,
        "Threaten for cargo",
        OptionCode: OptionCode) {

    public override bool AgentCapable(IActor agent) =>
        agent.Faction == Faction.Player && agent is Crawler { IsDisarmed: false };

    public override bool SubjectCapable(IActor subject) =>
        subject is Crawler { IsVulnerable: true } && subject.Faction != Faction.Player;

    public override bool InteractionCapable(IActor Agent, IActor Subject) =>
        base.InteractionCapable(Agent, Subject) &&
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Surrendered;

    protected override IOffer SubjectOffer(IActor subject) => new CompoundOffer(
        subject.SupplyOffer(DemandFraction),
        subject.CargoOffer((DemandFraction + 1) / 2)
    );

    public override string Description => "Threaten for cargo";
}

public record ProposeRepairBuy(string OptionCode = "R"): IProposal {
    public bool AgentCapable(IActor agent) =>
        (agent.Flags & EActorFlags.Settlement) != 0;
    public bool SubjectCapable(IActor subject) =>
        subject is Crawler damaged &&
        damaged.Segments.Any(IsRepairable);
    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Hostile;
    public string Description => $"Repair subject segments";
    static bool IsRepairable(Segment segment) => segment is { Hits: > 0, IsDestroyed: false };
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        if (Subject is Crawler damaged) {
            foreach (var segment in damaged.Segments.Where(IsRepairable))
            {
                float value = segment.Cost / 8;
                float price = value * Markup;
                var repairOffer = new RepairOffer(Agent, segment, value);
                yield return new ExchangeInteraction(Agent, repairOffer, Subject, new ScrapOffer(price), OptionCode);
            }
        }
    }

    public float Markup = Tuning.Trade.RepairMarkup();
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}
