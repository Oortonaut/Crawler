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


public record RepairOffer(
    IActor _agent,
    Segment SubjectSegment,
    float price): IOffer {
    public virtual string Description => $"Repair {SubjectSegment.StatusLine(_agent.Location)} ({SubjectSegment.Name})";
    public virtual bool EnabledFor(IActor Agent, IActor Subject) =>
        Subject.Inv.Segments.Contains(SubjectSegment) && SubjectSegment.Hits > 0;
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
        Loser.To(Winner).Hostile = false;
        Winner.To(Loser).Hostile = false;
        Winner.Inv[Commodity.Morale] += Tuning.Crawler.MoraleSurrenderedTo;
        Loser.Inv[Commodity.Morale] += Tuning.Crawler.MoraleSurrendered;
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
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => Agent.Inv[Commodity] >= Amount;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Agent.Inv[Commodity] -= Amount;
        Subject.Inv[Commodity] += Amount;
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
        // Check both regular inventory and trade inventory
        if (Agent is Crawler crawler) {
            return crawler.Inv.Segments.Contains(Segment) || crawler.TradeInv.Segments.Contains(Segment);
        }
        return Agent.Inv.Segments.Contains(Segment);
    }
    public void PerformOn(IActor Agent, IActor Subject) {
        // Try to remove from trade inventory first, then regular inventory
        if (Agent is Crawler crawler && crawler.TradeInv.Segments.Contains(Segment)) {
            crawler.TradeInv.Remove(Segment);
        } else {
            Agent.Inv.Remove(Segment);
        }
        Subject.Inv.Add(Segment);
    }
    public float ValueFor(IActor Agent) => Segment.Cost * Tuning.Economy.LocalMarkup(Segment.SegmentKind, Agent.Location);
}

// This offer moves the Delivered inventory from the agent to the subject.
public record InventoryOffer(
    Inventory Delivered,
    Inventory? _promised = null) : IOffer {

    public virtual string Description => Promised.ToString();
    public override string ToString() => Description;
    public Inventory Promised => _promised ?? Delivered;
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => Agent.Inv.Contains(Delivered);
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Subject.Inv.Add(Delivered);
        Agent.Inv.Remove(Delivered);
        foreach (var segment in Delivered.Segments) {
            segment.Packaged = false;
        }
    }
    public float ValueFor(IActor Agent) => Promised.ValueAt(Agent.Location);
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

public record LootOffer(IActor Wreck, Inventory LootInv, string _description): InventoryOffer(LootInv) {
    public LootOffer(IActor Wreck, float lootReturn, string _description): this(Wreck, MakeLootInv(Wreck.Inv, lootReturn), _description) {
    }
    public override void PerformOn(IActor Agent, IActor Subject) {
        base.PerformOn(Agent, Subject);
        Agent.Flags |= EActorFlags.Looted;
    }
    public static Inventory MakeLootInv(Inventory from, float? lootReturn) {
        var loot = new Inventory();
        foreach (var commodity in Enum.GetValues<Commodity>()) {
            float x = Random.Shared.NextSingle();
            loot[commodity] += from[commodity] * x * (lootReturn ?? Tuning.Game.LootReturn);
        }
        var lootableSegments = from.Segments.Where(s => s.Health > 0).ToArray();
        loot.Segments.AddRange(lootableSegments
            .Where(s => Random.Shared.NextDouble() < lootReturn));
        return loot;
    }
    public override string Description => _description;
}
