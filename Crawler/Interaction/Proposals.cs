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
public interface IProposal {
    /// <summary>Can the agent make this proposal?</summary>
    bool AgentCapable(IActor Agent);

    /// <summary>Can the subject receive this proposal?</summary>
    bool SubjectCapable(IActor Subject);

    /// <summary>Can the combo interact? For performance, if no interaction is possible
    /// then GetInteractions() shouldn't yield any.</summary>
    bool InteractionCapable(IActor Agent, IActor Subject);

    /// <summary>Display description for menus</summary>
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
    public static bool Test(this IProposal proposal, IActor Agent, IActor Subject) =>
        proposal.AgentCapable(Agent) &&
        proposal.SubjectCapable(Subject) &&
        proposal.InteractionCapable(Agent, Subject);
    public static IEnumerable<IInteraction> TestGetInteractions(this IProposal proposal, IActor Agent, IActor Subject) =>
        proposal.Test(Agent, Subject) ? proposal.GetInteractions(Agent, Subject) : [];
}

// I propose that I give you my loot
record ProposeLootFree(string OptionCode, string verb = "Loot"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        Agent is Crawler wreck &&
        wreck.EndState != null &&
        wreck.Hasnt(EActorFlags.Looted);
    public bool SubjectCapable(IActor Subject) => true;
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        string description = $"{verb} {Agent.Name}";
        yield return new ExchangeInteraction(Agent, new LootOffer(Agent, Tuning.Game.LootReturn, description), Subject, new EmptyOffer(), OptionCode, description);
    }
    public string Description => $"Offer {verb}";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}

// I propose that you harvest my resources
record ProposeHarvestFree(IActor Resource, Inventory Amount, string OptionCode, string verb): IProposal {
    public bool AgentCapable(IActor Agent) => Agent == Resource && (Agent.Flags & EActorFlags.Looted) == 0;
    public bool SubjectCapable(IActor Subject) => Subject != Resource;
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        string description = $"{verb} {Agent.Name}";
        yield return new ExchangeInteraction(Agent, new LootOffer(Agent, Agent.Inv, description), Subject, new EmptyOffer(), OptionCode, description);
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

record ProposeLootRisk(IActor Resource, Inventory Risk, float Chance): IProposal {
    public bool AgentCapable(IActor Agent) => Agent == Resource && Agent.Hasnt(EActorFlags.Looted);
    public bool SubjectCapable(IActor Subject) => Subject != Resource;
    public bool InteractionCapable(IActor Agent, IActor Subject) => true;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var impact = LootOffer.MakeLootInv(Risk, Chance);
        string description = impact.ToString();
        yield return new ExchangeInteraction(
            Agent, new LootOffer(Agent, Agent.Inv, Agent.Inv.ToString()),
            Subject, new InventoryOffer(Risk),
            "E",
            Description);
    }
    public string Description => $"Explore {Resource.Name}";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}

public record ProposeAttackDefend(string Description): IProposal {
    public bool AgentCapable(IActor Agent) => Agent is Crawler;
    public bool SubjectCapable(IActor Subject) => Subject.Faction is not Faction.Independent;
    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        Agent.Faction is Faction.Player
            ? true
            : false;
    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        yield return new AttackInteraction(Agent, Subject);
    }
    public long ExpirationTime => 0;
}

// I propose that I surrender to you
public record ProposeAcceptSurrender(string OptionCode): IProposal {
    Inventory MakeSurrenderInv(IActor Loser) {
        Inventory surrenderInv = new();
        if (Loser is Crawler loser) {
            if (loser.IsDepowered) {
                // 2/3 crew will join player, -2 morale
                int crewOffer = ( int ) (loser.CrewInv * 2f / 3f);
                surrenderInv.Add(Commodity.Crew, crewOffer);
                surrenderInv.Add(Commodity.Morale, -2);
            }
            if (loser.IsImmobile) {
                // Offer 2/3 scrap
                float scrapOffer = loser.ScrapInv * 2 / 3;
                surrenderInv.Add(Commodity.Scrap, scrapOffer);
            }
            if (loser.IsDefenseless) {
                float rationsOffer = loser.RationsInv * 2 / 3;
                surrenderInv.Add(Commodity.Rations, rationsOffer);
            }
            if (loser.IsDisarmed) {
                // Offers 2/3 fuel
                float fuelOffer = loser.FuelInv * 2 / 3;
                surrenderInv.Add(Commodity.Fuel, fuelOffer);
            }
        }
        if (surrenderInv.IsEmpty) {
            // Fallback - just offer half of everything
            foreach (var commodity in CommodityCache.AllCommodities) {
                surrenderInv.Add(commodity, Loser.Inv[commodity] / 2);
            }

        }

        return surrenderInv;
    }

    public bool AgentCapable(IActor Winner) => true;
    public bool SubjectCapable(IActor Loser) =>
        Loser is Crawler loser && loser.IsVulnerable;
    public bool InteractionCapable(IActor Winner, IActor Loser) =>
        Winner != Loser &&
        Winner.To(Loser).Hostile &&
        !Loser.To(Winner).Surrendered
            ? true
            : false;
    public IEnumerable<IInteraction> GetInteractions(IActor Winner, IActor Loser) {
        var surrenderInv = MakeSurrenderInv(Loser);
        string Description = $"{Loser.Name} Surrender";
        float value = surrenderInv.ValueAt(Winner.Location);
        yield return new ExchangeInteraction(
            Winner, new AcceptSurrenderOffer(value, Description),
            Loser, new InventoryOffer(surrenderInv), OptionCode, Description);
    }
    public string Description => $"SurrenderAccept";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}

// I propose that I repair you
// Forced demand proposal: "Give me A or I'll do B to you"
// This creates an interaction that presents a choice to the subject.
// If they refuse or ignore, the consequence is automatically triggered.
public record ProposeDemand(
    IOffer Demand,
    Func<IActor, IActor, IInteraction> ConsequenceFn,
    string Ultimatum,
    Func<IActor, IActor, bool>? Condition = null): IProposal {

    public long ExpirationTime { get; set; } = 0;

    public bool AgentCapable(IActor Agent) => true;
    public bool SubjectCapable(IActor Subject) => true;
    public bool InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent == Subject) return false;
        if (!(Condition?.Invoke(Agent, Subject) ?? true)) return false;

        return Agent.To(Subject).PersistentProposals.Contains(this);
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        var consequence = ConsequenceFn(Agent, Subject);
        yield return new AcceptDemandInteraction(Agent, Subject, Demand, Ultimatum, this);
        yield return new RefuseDemandInteraction(Agent, Subject, consequence, Ultimatum, this);
    }

    public string Description => Ultimatum;
    public override string ToString() => Description;

}

// Bandit extortion: "Hand over cargo or I attack"
public record ProposeExtortion(float DemandFraction = 0.5f): IProposal {
    public long ExpirationTime { get; set; } = 0;

    public bool AgentCapable(IActor Agent) =>
        Agent is Crawler bandit && bandit.Faction == Faction.Bandit && !bandit.IsDisarmed;

    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction == Faction.Player;

    public bool InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent.To(Subject).Hostile) return false;
        if (Agent.To(Subject).Surrendered) return false;
        if (Subject.Inv.ValueAt(Subject.Location) <= 0) return false;
        return Agent.To(Subject).PersistentProposals.Any(p => p is ProposeExtortion);
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        // Calculate demand: fraction of subject's valuable commodities
        var demand = new Inventory();
        foreach (var commodity in CommodityCache.AllCommodities) {
            if (commodity == Commodity.Crew || commodity == Commodity.Morale) continue; // Don't demand crew/morale
            float amount = Subject.Inv[commodity] * DemandFraction;
            if (amount > 0) {
                demand.Add(commodity, amount);
            }
        }

        if (demand.IsEmpty) yield break;

        float value = demand.ValueAt(Subject.Location);
        string ultimatum = $"{Agent.Name} demands {value:F0}¢¢ worth of cargo or they will attack";

        var consequence = new AttackInteraction(Agent, Subject);
        yield return new AcceptDemandInteraction(Agent, Subject, new InventoryOffer(demand), ultimatum, this);
        yield return new RefuseDemandInteraction(Agent, Subject, consequence, ultimatum, this);
    }

    public string Description => "Extort cargo";
    public override string ToString() => Description;
}

// Civilian faction taxes: "Pay taxes or face hostility"
public record ProposeTaxes(float TaxRate = 0.05f): IProposal {
    public long ExpirationTime { get; set; } = 0;

    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0 && Agent.Faction.IsCivilian();

    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction == Faction.Player;

    public bool InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent.Location.Sector.ControllingFaction != Agent.Faction) return false;
        if (Agent.To(Subject).Hostile) return false;
        if (!(Subject.Inv.ValueAt(Subject.Location) > 0 || Subject.Inv[Commodity.Scrap] > 0)) return false;
        return Agent.To(Subject).PersistentProposals.Any(p => p is ProposeTaxes);
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        float cargoValue = Subject.Inv.ValueAt(Subject.Location);
        float taxAmount = cargoValue * TaxRate;

        if (taxAmount < 1f) yield break; // Don't bother for tiny amounts

        string ultimatum = $"{Agent.Name} demands {taxAmount:F0}¢¢ in taxes for entering their territory";

        var consequence = new HostilityInteraction(Agent, Subject, "refuses to pay taxes");
        yield return new AcceptDemandInteraction(Agent, Subject, new ScrapOffer(taxAmount), ultimatum, this);
        yield return new RefuseDemandInteraction(Agent, Subject, consequence, ultimatum, this);
    }

    public string Description => "Demand taxes";
    public override string ToString() => Description;

}

// Contraband seizure: "Surrender prohibited goods or pay fine"
public record ProposeContrabandSeizure(Inventory Contraband, float PenaltyAmount): IProposal {
    public long ExpirationTime { get; set; } = 0;

    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0 ||
        (Agent.Faction == Faction.Independent || Agent.Faction.IsCivilian());

    public bool SubjectCapable(IActor Subject) =>
        Subject.Faction == Faction.Player && Subject.Inv.Contains(Contraband);

    public bool InteractionCapable(IActor Agent, IActor Subject) {
        if (Agent.To(Subject).Hostile) return false;
        return Agent.To(Subject).PersistentProposals.Any(p => p is ProposeContrabandSeizure);
    }

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        float contrabandValue = Contraband.ValueAt(Subject.Location);
        string contrabandList = Contraband.ToString();

        string ultimatum = $"{Agent.Name} detected prohibited goods: {contrabandList}. " +
                          $"Surrender them or pay {PenaltyAmount:F0}¢¢ fine";

        // Create a choice: surrender contraband OR pay fine
        yield return new ContrabandInteraction(Agent, Subject, Contraband, PenaltyAmount, ultimatum, this);
    }

    public string Description => "Seize contraband";
    public override string ToString() => Description;
}

// Player demands: Let player threaten vulnerable NPCs
public record ProposePlayerDemand(float DemandFraction = 0.5f, string OptionCode = "X"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        Agent.Faction == Faction.Player && Agent is Crawler player && !player.IsDisarmed;

    public bool SubjectCapable(IActor Subject) =>
        Subject is Crawler target && target.IsVulnerable && Subject.Faction != Faction.Player;

    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        !Agent.To(Subject).Hostile &&
        !Subject.To(Agent).Surrendered &&
        Subject.Inv.ValueAt(Subject.Location) > 0
            ? true
            : false;

    public IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject) {
        // Calculate demand: fraction of subject's valuable commodities
        var demand = new Inventory();
        foreach (var commodity in CommodityCache.AllCommodities) {
            if (commodity == Commodity.Crew || commodity == Commodity.Morale) continue;
            float amount = Subject.Inv[commodity] * DemandFraction;
            if (amount > 0) {
                demand.Add(commodity, amount);
            }
        }

        if (demand.IsEmpty) yield break;

        float value = demand.ValueAt(Subject.Location);
        string ultimatum = $"Demand {value:F0}¢¢ worth of cargo from {Subject.Name} or attack";

        var demandProposal = new ProposeDemand(
            new InventoryOffer(demand),
            (agent, subject) => new AttackInteraction(agent, subject),
            ultimatum,
            (agent, subject) => true
        );

        foreach (var interaction in demandProposal.GetInteractions(Agent, Subject)) {
            yield return interaction;
        }
    }

    public string Description => "Threaten for cargo";
    public long ExpirationTime => 0;
    public override string ToString() => Description;
}

public record ProposeRepairBuy(string OptionCode = "R"): IProposal {
    public bool AgentCapable(IActor Agent) =>
        (Agent.Flags & EActorFlags.Settlement) != 0;
    public bool SubjectCapable(IActor Subject) =>
        Subject is Crawler damaged &&
        damaged.Segments.Any(IsRepairable);
    public bool InteractionCapable(IActor Agent, IActor Subject) =>
        Agent.Faction == Faction.Independent ||
        Agent.Faction == Subject.Faction
            ? true
            : false;
    public string Description => $"Repair subject segments";
    static bool IsRepairable(Segment segment) => segment.Hits > 0;
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
