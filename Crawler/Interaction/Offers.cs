using Crawler;

namespace Crawler;

/// <summary>
/// One half of a two-sided exchange interaction.
/// The agent gives or does something to the subject as part of an exchange.
/// See: docs/SYSTEMS.md#proposalinteraction-system
/// </summary>
public interface IOffer {
    // Previously: bool EnabledFor(IActor Agent, IActor Subject);
    /// <summary>Can this offer be fulfilled by the agent? Return null for success (sorry) and a string for failure.</summary>
    string? DisabledFor(IActor Agent, IActor Subject);

    /// <summary>Execute the offer (transfer commodities, perform action, etc.)</summary>
    void PerformOn(IActor Agent, IActor Subject);

    /// <summary>Calculate the value of this offer for the agent</summary>
    float ValueFor(IActor Agent);

    /// <summary>Display description (e.g., "100 Fuel", "Repair Segment")</summary>
    string Description { get; }
}

public record CompoundOffer(params IOffer[] subs): IOffer {
    public virtual string Description => string.Join(", ", subs.Select(s => s.Description));
    public override string ToString() => Description;
    public virtual string? DisabledFor(IActor Agent, IActor Subject) => subs.Select(s => s.DisabledFor(Agent, Subject)).FirstOrDefault(r => r != null);
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        foreach (var sub in subs) {
            sub.PerformOn(Agent, Subject);
        }
    }
    public float ValueFor(IActor Agent) => subs.Sum(s => s.ValueFor(Agent));
}

public record LootOfferWrapper(IOffer offer): IOffer {
    public virtual string Description => $"{offer} (Loot)";
    public override string ToString() => Description;
    public virtual string? DisabledFor(IActor Agent, IActor Subject) => Agent.EndState == EEndState.Looted ? "Already looted" : offer.DisabledFor(Agent, Subject);
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        offer.PerformOn(Agent, Subject);
        Agent.End(EEndState.Looted, $"looted by {Subject.Name}");
    }
    public float ValueFor(IActor Agent) => offer.ValueFor(Agent);
}

public record RepairOffer(
    IActor _agent,
    Segment SubjectSegment,
    float price): IOffer {
    public virtual string Description => $"Repair {SubjectSegment.StatusLine(_agent.Location)} ({SubjectSegment.Name})";
    public virtual string? DisabledFor(IActor Agent, IActor Subject) =>
        Agent.Ended() ? "Dead Mechanic" :
        Subject.Ended() ? "Dead Client" :
        !Subject.Supplies.Segments.Contains(SubjectSegment) ? "Not owned" :
        SubjectSegment.Hits <= 0 ? "Undamaged" :
        null;
    public virtual void PerformOn(IActor Agent, IActor Subject) =>
        --SubjectSegment.Hits;
    public float ValueFor(IActor Agent) => price;
}

public record LicenseOffer(
    Faction AgentFaction,
    CommodityCategory Category,
    GameTier Tier,
    float price): IOffer {
    public virtual string Description => $"{Category} License ({Tier})";
    public virtual string? DisabledFor(IActor Agent, IActor Subject) =>
        Agent.Ended() ? "Issuer Dead" :
        Subject.Ended() ? "Buyer Dead" :
        Subject is not Crawler buyer ? "Not a Crawler" :
        buyer.To(AgentFaction).GetLicense(Category) >= Tier ? "Already Licensed" :
        null;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        if (Subject is Crawler buyer) {
            buyer.To(AgentFaction).GrantLicense(Category, Tier);
            buyer.Message($"Acquired {Category} License ({Tier}) for {AgentFaction} territory");
        }
    }
    public float ValueFor(IActor Agent) => price;
}

// An offer that does nothing.
public record EmptyOffer(): IOffer {
    public string Description => "Nothing";
    public override string ToString() => Description;
    public string? DisabledFor(IActor Agent, IActor Subject) => null;
    public void PerformOn(IActor Agent, IActor Subject) { }
    public float ValueFor(IActor Agent) => 0;
}

// Give commodities from agent to subject
public record CommodityOffer(Commodity commodity, float amount): IOffer {
    public readonly float Amount = commodity.Round(amount);
    public virtual string Description => commodity.CommodityText(Amount);
    public override string ToString() => Description;
    public virtual string? DisabledFor(IActor Agent, IActor Subject) =>
        Subject.Ended() ? "Taker Dead" :
        Agent.Supplies.Contains(commodity, amount) == FromInventory.None ? $"lacks {commodity}" :
        null;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Agent.Supplies[commodity] -= Amount;
        Subject.Supplies[commodity] += Amount;
    }
    public float ValueFor(IActor Agent) => commodity.CostAt(Agent.Location);
}

// A cash-only offer, for convenience
public record ScrapOffer(float Scrap): CommodityOffer(Commodity.Scrap, Scrap);

// This offer moves a segment from the agent to the subject.
public record SegmentOffer(Segment Segment): IOffer {
    public string Description => Segment.Name;
    public override string ToString() => Description;
    public string? DisabledFor(IActor Agent, IActor Subject) =>
        Subject.Ended() ? "Taker Dead" :
        Agent.Cargo.Contains(Segment) == FromInventory.None ? "No cargo" :
        null;
    public void PerformOn(IActor Agent, IActor Subject) {
        Agent.Cargo.Remove(Segment);
        Subject.Supplies.Add(Segment);
    }
    public float ValueFor(IActor Agent) => Segment.Cost * Tuning.Economy.LocalMarkup(Segment.SegmentKind, Agent.Location);
}

public record HostilityOffer(string Reason): IOffer {
    public string Description => $"Turn hostile: {Reason}";
    public override string ToString() => Description;
    public string? DisabledFor(IActor Agent, IActor Subject) =>
        Agent.Ended() ? "Aggressor Dead" :
        Subject.Ended() ? "Subject Dead" :
        null;

    public void PerformOn(IActor Agent, IActor Subject) {
        if (Agent is Crawler agentCrawler) agentCrawler.SetHostileTo(Subject, true);
        if (Subject is Crawler subjectCrawler) subjectCrawler.SetHostileTo(Agent, true);
        Agent.Message($"{Subject.Name} {Reason}. You are now hostile.");
        Subject.Message($"{Agent.Name} turns hostile because you {Reason.Replace("refuses", "refused")}!");
        Subject.Supplies[Commodity.Morale] -= 2;
    }
    public float ValueFor(IActor Agent) => 0;
}

// This offer moves the Delivered inventory from the agent's trade inventory to the subjects main inventory.
public record InventoryOffer(
    bool cargo,
    Inventory Delivered,
    Inventory? _promised = null) : IOffer {

    public virtual string Description => Promised.ToString();
    public override string ToString() => Description;
    public Inventory Promised => _promised ?? Delivered;
    public virtual string? DisabledFor(IActor Agent, IActor Subject) =>
        Subject.Ended() ? "Taker Dead" :
        GetInv(Agent).Contains(Delivered) == FromInventory.None ? "No inventory" :
        null;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Subject.Cargo.Add(Delivered);
        GetInv(Agent).Remove(Delivered);
        foreach (var segment in Delivered.Segments) {
            segment.Packaged = true;
        }
    }
    public float ValueFor(IActor Agent) => Promised.ValueAt(Agent.Location);
    Inventory GetInv(IActor Agent) => cargo ? Agent.Cargo : Agent.Supplies;
}

public static partial class OfferEx {
    public static IOffer CargoOffer(this IActor actor) => new LootOfferWrapper(new InventoryOffer(true, actor.Cargo));
    public static IOffer SupplyOffer(this IActor actor) => new LootOfferWrapper(new InventoryOffer(false, actor.Supplies));
    public static IOffer CargoOffer(this IActor actor, XorShift rng, float fraction) => new LootOfferWrapper(new InventoryOffer(true, actor.Cargo.Loot(rng, fraction)));
    public static IOffer SupplyOffer(this IActor actor, XorShift rng, float fraction) => new LootOfferWrapper(new InventoryOffer(false, actor.Supplies.Loot(rng, fraction)));
    public static bool EnabledFor(this IOffer Offer, IActor Agent, IActor Subject) => Offer.DisabledFor(Agent, Subject) == null;
    public static bool HostileTo(this IActor from, IActor to) => from.To(to).Hostile;
    public static bool Warring(this IActor from, IActor to) => from.HostileTo(to) && to.HostileTo(from);
    public static bool Fighting(this IActor from, IActor to) => from.HostileTo(to) || to.HostileTo(from);
    public static bool PeacefulTo(this IActor from, IActor to) => !from.To(to).Hostile;
    public static bool SurrenderedTo(this IActor from, IActor to) => from.To(to).Surrendered;
}
