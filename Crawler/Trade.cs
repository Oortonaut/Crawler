namespace Crawler;

public enum InteractionCapability {
    Disabled,
    Possible,
    Mandatory,
}

public interface IProposal {
    bool AgentCapable(IActor Agent);
    bool SubjectCapable(IActor Subject);
    InteractionCapability InteractionCapable(IActor Agent, IActor Subject);
    string Description { get; }
    // Assumes that all tests have passed.
    IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject);
}

public interface IInteraction {
    bool Enabled(string args = "");
    int Perform(string args = "");
    string Description { get; }
    string OptionCode { get; }
}

public static class IProposalEx {
    public static bool Test(this IProposal proposal, IActor Agent, IActor Subject) =>
        proposal.AgentCapable(Agent) &&
        proposal.SubjectCapable(Subject) &&
        proposal.InteractionCapable(Agent, Subject) != InteractionCapability.Disabled;
    public static IEnumerable<IInteraction> TestGetInteractions(this IProposal proposal, IActor Agent, IActor Subject) =>
        proposal.Test(Agent, Subject) ? proposal.GetInteractions(Agent, Subject) : [];
}

// Offers and Exchange Interactions
//=========================================================================

// An offer is one half of a two-sided exchange interaction.
// The Agent gives or does something to the subject, within their agency,
// as part of their offer with a corresponding subject service or goods
// in return.
public interface IOffer {
    bool EnabledFor(IActor Agent, IActor Subject);
    void PerformOn(IActor Agent, IActor Subject);
    float ValueFor(IActor Agent);
    string Description { get; }
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

// Agent is seller
public record ProposeSellBuy(IOffer Stuff, float cash, string OptionCode = "T"): IProposal {
    public readonly float Cash = Commodity.Scrap.Round(cash);
    public bool AgentCapable(IActor Seller) => true;
    public bool SubjectCapable(IActor Buyer) => true;
    public InteractionCapability InteractionCapable(IActor Seller, IActor Buyer) =>
        Buyer != Seller && Stuff.EnabledFor(Seller, Buyer)
            ? InteractionCapability.Possible
            : InteractionCapability.Disabled;
    public IEnumerable<IInteraction> GetInteractions(IActor Seller, IActor Buyer) {
        var interaction = new ExchangeInteraction(Buyer, new ScrapOffer(Cash), Seller, Stuff, OptionCode, Description);
        yield return interaction;
    }
    // Description is from theI  subjects POV
    public string Description => $"Buy {Stuff.Description} for {Cash}¢¢";
    public override string ToString() => Description;
}

// Agent is buyer
public record ProposeBuySell(float cash, IOffer Stuff, string OptionCode = "T"): IProposal {
    public readonly float Cash = Commodity.Scrap.Round(cash);
    public bool AgentCapable(IActor Buyer) => true;
    public bool SubjectCapable(IActor Seller) => true;
    public InteractionCapability InteractionCapable(IActor Buyer, IActor Seller) =>
        Buyer != Seller && Stuff.EnabledFor(Seller, Buyer)
            ? InteractionCapability.Possible
            : InteractionCapability.Disabled;
    public IEnumerable<IInteraction> GetInteractions(IActor Buyer, IActor Seller) {
        var interaction = new ExchangeInteraction(Buyer, new ScrapOffer(Cash), Seller, Stuff, OptionCode, Description);
        yield return interaction;
    }
    // Description is from theI  subjects POV
    public string Description => $"Sell {Stuff.Description} for {Cash}¢¢";
    public override string ToString() => Description;
}

public record ExchangeInteraction: IInteraction {
    public ExchangeInteraction(IActor agent,
        IOffer agentOffer,
        IActor subject,
        IOffer subjectOffer,
        string optionCode,
        string? description = null) {
        Agent = agent;
        AgentOffer = agentOffer;
        Subject = subject;
        SubjectOffer = subjectOffer;
        OptionCode = optionCode;
        Description = description ?? MakeDescription();
    }
    public IActor Agent { get; init; }
    public IActor Subject { get; init; }

    public bool Enabled(string args = "") {
        return AgentOffer.EnabledFor(Agent, Subject) &&
               SubjectOffer.EnabledFor(Subject, Agent);
    }

    public int Perform(string args = "") {
        int count = 1;
        if (!string.IsNullOrWhiteSpace(args) && int.TryParse(args, out int parsed)) {
            count = Math.Max(1, parsed);
        }

        int performed = 0;
        for (int i = 0; i < count; i++) {
            if (!Enabled()) {
                break;
            }
            AgentOffer.PerformOn(Agent, Subject);
            SubjectOffer.PerformOn(Subject, Agent);
            performed++;
        }

        if (performed > 0) {
            Agent.Message($"You gave {Subject.Name} {AgentOffer.Description} and got {SubjectOffer.Description} in return. (x{performed})");
            Subject.Message($"You gave {Agent.Name} {SubjectOffer.Description} and got {AgentOffer.Description} in return. (x{performed})");
        }
        return performed;
    }
    public string Description { get; init; }
    public override string ToString() => Description;
    public IOffer AgentOffer { get; init; }
    public IOffer SubjectOffer { get; init; }
    public string OptionCode { get; init; }
    public string MakeDescription() {
        // Note: Can't access Value here without Agent binding
        var buyerDesc = AgentOffer.Description;
        var sellerDesc = SubjectOffer.Description;
        if (AgentOffer is EmptyOffer) {
            return $"{sellerDesc}";
        } else if (SubjectOffer is EmptyOffer) {
            return $"{buyerDesc}";
        } else if (AgentOffer is ScrapOffer) {
            return $"Sell {sellerDesc} for {buyerDesc}";
        } else if (SubjectOffer is ScrapOffer) {
            return $"Buy {buyerDesc} for {sellerDesc}";
        }
        return $"Give {sellerDesc} for {buyerDesc}";
    }

}

public static class TradeEx {
        public static IEnumerable<IProposal> MakeTradeProposals(this IActor Seller, float wealthFraction, Faction faction = Faction.Independent) {
        var Location = Seller.Location;
        var wealth = Location.Wealth * wealthFraction;

        // Use bandit markup for bandit faction, regular for others
        bool isBandit = faction == Faction.Bandit;
        float merchantMarkup = 1.0f;
        Crawler? seller = Seller as Crawler;
        merchantMarkup = seller?.Markup ?? 1.0f;

        var commodities = Enum.GetValues<Commodity>()
            .Where(s => s != Commodity.Scrap)
            .Where(s => Random.Shared.NextSingle() < s.AvailabilityAt(Location))
            .ToList();
        commodities.Shuffle();

        float CFrac = wealth;

        foreach (var commodity in commodities) {
            // Check faction policy for this commodity
            var policy = Tuning.FactionPolicies.GetPolicy(faction, commodity);

            // Skip prohibited goods
            if (policy == TradePolicy.Prohibited) {
                continue;
            }

            // Vice category goods are only sold by Bandits
            if (!isBandit && commodity.Category() == CommodityCategory.Vice) {
                continue;
            }

            // Calculate mid-price with location, scarcity, and policy markups
            var locationMarkup = Tuning.Economy.LocalMarkup(commodity, Location);
            var scarcityPremium = commodity.ScarcityPremium(Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);

            float midPrice = commodity.CostAt(Location) * scarcityPremium * policyMultiplier;

            // Calculate bid-ask spread
            float bidAskSpread = Tuning.Trade.baseBidAskSpread;
            bidAskSpread *= seller?.BidAskMultiplier ?? 1.0f;
            float spreadAmount = midPrice * bidAskSpread;

            // Ask price (player buys from NPC)
            float askPrice = midPrice + spreadAmount / 2;
            // Bid price (player sells to NPC)
            float bidPrice = midPrice - spreadAmount / 2;

            // Add commodity to seller's inventory
            var quantity = Inventory.QuantitySold(CFrac / locationMarkup, commodity, Location);
            Seller.Inv[commodity] += quantity;

            // Create proposals
            var saleQuantity = 1f;
            var sellOffer = new CommodityOffer(commodity, (float)Math.Floor(saleQuantity));
            var sellPrice = saleQuantity * askPrice;

            // Add transaction fee for restricted goods
            if (policy == TradePolicy.Controlled) {
                sellPrice += Tuning.Trade.restrictedTransactionFee;
            }

            yield return new ProposeSellBuy(sellOffer, sellPrice, "B");

            var buyOffer = new CommodityOffer(commodity, (float)Math.Ceiling(saleQuantity));
            var buyPrice = saleQuantity * bidPrice;

            // Transaction fee applies to purchases too
            if (policy == TradePolicy.Controlled) {
                buyPrice = Math.Max(0, buyPrice - Tuning.Trade.restrictedTransactionFee);
            }

            yield return new ProposeBuySell(buyPrice, buyOffer, "S");
        }

        // Offer segments from trade inventory if available
        foreach (var segment in seller?.TradeInv.Segments.ToList() ?? []) {
            var localCost =  segment.CostAt(Location);
            var price = localCost * merchantMarkup;
            yield return new ProposeSellBuy(new SegmentOffer(segment), price);
        }
        int SaleSegments = CrawlerEx.SamplePoisson((float)Math.Log(Location.Wealth * wealthFraction / 300 + 1));
        while (SaleSegments --> 0) {
            var segmentDef = SegmentEx.AllDefs.ChooseRandom()!;
            var segment = segmentDef.NewSegment();
            var markup = Tuning.Economy.LocalMarkup(segment.SegmentKind, Location);
            markup *= merchantMarkup;
            Seller.Inv.Segments.Add(segment);
            var price = segment.Cost * markup;
            yield return new ProposeSellBuy(new SegmentOffer(segment), price);
        }
    }

}
