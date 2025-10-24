namespace Crawler;

/// <summary>
/// One half of a two-sided exchange interaction.
/// The agent gives or does something to the subject as part of an exchange.
/// See: docs/SYSTEMS.md#proposalinteraction-system
/// </summary>
public interface IOffer {
    /// <summary>Can this offer be fulfilled by the agent?</summary>
    bool EnabledFor(IActor Agent, IActor Subject);

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
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => subs.All(s => s.EnabledFor(Agent, Subject));
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        foreach (var sub in subs) {
            sub.PerformOn(Agent, Subject);
        }
    }
    public float ValueFor(IActor Agent) => subs.Sum(s => s.ValueFor(Agent));
}

public record LootOfferWrapper(IOffer offer): IOffer {
    public virtual string Description => offer.Description;
    public override string ToString() => $"{offer} (Loot)";
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => !Agent.HasFlag(EActorFlags.Looted) && offer.EnabledFor(Agent, Subject);
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        offer.PerformOn(Agent, Subject);
        Agent.Flags |= EActorFlags.Looted;
    }
    public float ValueFor(IActor Agent) => offer.ValueFor(Agent);
}

public record RepairOffer(
    IActor _agent,
    Segment SubjectSegment,
    float price): IOffer {
    public virtual string Description => $"Repair {SubjectSegment.StatusLine(_agent.Location)} ({SubjectSegment.Name})";
    public virtual bool EnabledFor(IActor Agent, IActor Subject) =>
        Subject.Supplies.Segments.Contains(SubjectSegment) && SubjectSegment.Hits > 0;
    public virtual void PerformOn(IActor Agent, IActor Subject) =>
        --SubjectSegment.Hits;
    public float ValueFor(IActor Agent) => price;
}

public record AcceptSurrenderOffer(float value, string _description): IOffer {
    public string Description => _description;
    public bool EnabledFor(IActor Agent, IActor Subject) => true;
    public void PerformOn(IActor Winner, IActor Loser) {
        Winner.Message($"{Loser.Name} has surrendered to you . {Tuning.Crawler.MoraleSurrenderedTo} Morale");
        Loser.Message($"You have surrendered to {Winner.Name}. {Tuning.Crawler.MoraleSurrendered} Morale");
        Loser.To(Winner).Surrendered = true;
        Winner.To(Loser).Spared = true;
        Loser.To(Winner).Hostile = false;
        Winner.To(Loser).Hostile = false;
        Winner.Supplies[Commodity.Morale] += Tuning.Crawler.MoraleSurrenderedTo;
        Loser.Supplies[Commodity.Morale] += Tuning.Crawler.MoraleSurrendered;
    }
    public float ValueFor(IActor Agent) => value;
}

// An offer that does nothing.
public record EmptyOffer(): IOffer {
    public string Description => "Nothing";
    public override string ToString() => Description;
    public bool EnabledFor(IActor Agent, IActor Subject) => true;
    public void PerformOn(IActor Agent, IActor Subject) { }
    public float ValueFor(IActor Agent) => 0;
}

// A quantity of a single commodity
public record CommodityOffer(Commodity Commodity, float amount): IOffer {
    public readonly float Amount = Commodity.Round(amount);
    public virtual string Description => Commodity.CommodityText(Amount);
    public override string ToString() => Description;
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => Agent.Supplies[Commodity] >= Amount;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Agent.Supplies[Commodity] -= Amount;
        Subject.Supplies[Commodity] += Amount;
    }
    public float ValueFor(IActor Agent) => Commodity.CostAt(Agent.Location);
}

// A cash-only offer, for convenience
public record ScrapOffer(float Scrap): CommodityOffer(Commodity.Scrap, Scrap);

// This offer moves a segment from the agent to the subject.
public record SegmentOffer(Segment Segment): IOffer {
    public string Description => Segment.Name;
    public override string ToString() => Description;
    public bool EnabledFor(IActor Agent, IActor Subject) {
        // Check both supplies and cargo
        if (Agent is Crawler crawler) {
            return crawler.Supplies.Segments.Contains(Segment) || crawler.Cargo.Segments.Contains(Segment);
        }
        return Agent.Supplies.Segments.Contains(Segment);
    }
    public void PerformOn(IActor Agent, IActor Subject) {
        // Try to remove from trade inventory first, then regular inventory
        if (Agent is Crawler crawler && crawler.Cargo.Segments.Contains(Segment)) {
            crawler.Cargo.Remove(Segment);
        } else {
            Agent.Supplies.Remove(Segment);
        }
        Subject.Supplies.Add(Segment);
    }
    public float ValueFor(IActor Agent) => Segment.Cost * Tuning.Economy.LocalMarkup(Segment.SegmentKind, Agent.Location);
}

public record AttackOffer: IOffer {
    public string Description => "Attack";
    public override string ToString() => Description;
    public bool EnabledFor(IActor Agent, IActor Subject) => Agent is Crawler attacker && !attacker.IsDisarmed;
    public void PerformOn(IActor Agent, IActor Subject) {
        if (Agent is Crawler attacker) {
            attacker.Attack(Subject);
        }
    }
    public float ValueFor(IActor Agent) => 0;
}

public record HostilityOffer(string Reason): IOffer {
    public string Description => $"Turn hostile: {Reason}";
    public override string ToString() => Description;
    public bool EnabledFor(IActor Agent, IActor Subject) => true;
    public void PerformOn(IActor Agent, IActor Subject) {
        Agent.To(Subject).Hostile = true;
        Subject.To(Agent).Hostile = true;
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
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => GetInv(Agent).Contains(Delivered) != ContainsResult.False;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Subject.Cargo.Add(Delivered);
        GetInv(Agent).Remove(Delivered);
        foreach (var segment in Delivered.Segments) {
            segment.Packaged = false;
        }
    }
    public float ValueFor(IActor Agent) => Promised.ValueAt(Agent.Location);
    Inventory GetInv(IActor Agent) => cargo ? Agent.Cargo : Agent.Supplies;
}

public static partial class OfferEx {
    public static IOffer CargoOffer(this IActor actor) => new LootOfferWrapper(new InventoryOffer(true, actor.Cargo));
    public static IOffer SupplyOffer(this IActor actor) => new LootOfferWrapper(new InventoryOffer(false, actor.Supplies));
    public static IOffer CargoOffer(this IActor actor, float fraction) => new LootOfferWrapper(new InventoryOffer(true, actor.Cargo.Loot(fraction)));
    public static IOffer SupplyOffer(this IActor actor, float fraction) => new LootOfferWrapper(new InventoryOffer(false, actor.Supplies.Loot(fraction)));
}
