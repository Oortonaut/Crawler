namespace Crawler;

public interface IProposal {
    bool AgentCapable(IActor Agent);
    bool SubjectCapable(IActor Subject);
    bool InteractionCapable(IActor Agent, IActor Subject);
    string Description { get; }
    // Assumes that all tests have passed.
    IEnumerable<IInteraction> GetInteractions(IActor Agent, IActor Subject);
}

public interface IInteraction {
    bool Enabled();
    int Perform();
    string Description { get; }
    string OptionCode { get; }
}

public static class IProposalEx {
    public static bool Test(this IProposal proposal, IActor Agent, IActor Subject) =>
        proposal.AgentCapable(Agent) &&
        proposal.SubjectCapable(Subject) &&
        proposal.InteractionCapable(Agent, Subject);
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
public record CommodityOffer(Commodity Commodity, float Amount): IOffer {
    public virtual string Description => Commodity.CommodityText(Amount);
    public override string ToString() => Description;
    public virtual bool EnabledFor(IActor Agent, IActor Subject) => Agent.Inv[Commodity] >= Amount;
    public virtual void PerformOn(IActor Agent, IActor Subject) {
        Agent.Inv[Commodity] -= Amount;
        Subject.Inv[Commodity] += Amount;
    }
    public float ValueFor(IActor Agent) => Commodity.Value(Agent.Location);
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
public record ProposeSellBuy(IOffer Stuff, float Cash, string OptionCode = "T"): IProposal {
    public bool AgentCapable(IActor Seller) => true;
    public bool SubjectCapable(IActor Buyer) => true;
    public bool InteractionCapable(IActor Seller, IActor Buyer) => 
        Buyer != Seller && Stuff.EnabledFor(Seller, Buyer);
    public IEnumerable<IInteraction> GetInteractions(IActor Seller, IActor Buyer) {
        var interaction = new ExchangeInteraction(Buyer, new ScrapOffer(Cash), Seller, Stuff, OptionCode, Description);
        yield return interaction;
    }
    // Description is from theI  subjects POV
    public string Description => $"Buy {Stuff.Description} for {Cash}¢¢";
    public override string ToString() => Description;
}

// Agent is buyer
public record ProposeBuySell(float Price, IOffer Stuff, string OptionCode = "T"): IProposal {
    public bool AgentCapable(IActor Buyer) => true;
    public bool SubjectCapable(IActor Seller) => true;
    public bool InteractionCapable(IActor Buyer, IActor Seller) =>
        Buyer != Seller && Stuff.EnabledFor(Seller, Buyer);
    public IEnumerable<IInteraction> GetInteractions(IActor Buyer, IActor Seller) {
        var interaction = new ExchangeInteraction(Buyer, new ScrapOffer(Price), Seller, Stuff, OptionCode, Description);
        yield return interaction;
    }
    // Description is from theI  subjects POV
    public string Description => $"Sell {Stuff.Description} for {Price}¢¢";
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

    public bool Enabled() {
        return AgentOffer.EnabledFor(Agent, Subject) &&
               SubjectOffer.EnabledFor(Subject, Agent);
    }
    
    public int Perform() {
        if (!Enabled()) {
            throw new InvalidOperationException($"ExchangeInteraction {this} requires passing offers");
        }
        AgentOffer.PerformOn(Agent, Subject);
        SubjectOffer.PerformOn(Subject, Agent);
        Agent.Message($"You gave {Subject.Name} {AgentOffer.Description} and got {SubjectOffer.Description} in return.");
        Subject.Message($"You gave {Agent.Name} {SubjectOffer.Description} and got {AgentOffer.Description} in return.");
        return 1;
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

    public static IEnumerable<IProposal> MakeTradeProposals(IActor Seller, float wealthFraction, Faction faction = Faction.Trade) {
        var Location = Seller.Location;
        var wealth = Location.Wealth * wealthFraction;
        float Cutoff = 1 - (float)Math.Pow(0.5, wealth / 1000);
        float CFrac = wealth / (Enum.GetValues<Commodity>().Length - 1);

        // Use bandit markup for bandit faction, regular for others
        bool isBandit = faction == Faction.Bandit;
        float merchantMarkup = 1.0f;
        if (Seller is Crawler seller) {
            merchantMarkup = seller.Markup;
        }

        foreach (var commodity in Enum.GetValues<Commodity>()) {
            if (commodity == Commodity.Scrap) continue; // Skip cash-for-cash trades

            // Filter illegal items based on faction
            if (!isBandit && commodity.IsIllegalAtTrade()) {
                continue; // Trade settlements don't deal in illegal goods
            }

            var markup = Tuning.Economy.LocalMarkup(commodity, Location);
            markup *= merchantMarkup;

            float r = Random.Shared.NextSingle();
            r = Math.Clamp(r, 0, 1);
            if (r < Cutoff) {
                // Sell
                var quantity = Inventory.QuantitySold(CFrac / markup, commodity, Location);
                Seller.Inv[commodity] += quantity;
                var saleQuantity = Inventory.QuantitySold(Tuning.Trade.commodityBundleCost / markup, commodity, Location);
                var offer = new CommodityOffer(commodity, (float)Math.Floor(saleQuantity));
                var price = saleQuantity * commodity.Value(Location);
                yield return new ProposeSellBuy(offer, price, "B");
            }
            if (1 - r < Cutoff) {
                // Buy
                var quantity = Inventory.QuantityBought(CFrac * markup, commodity, Location);
                var saleQuantity = Inventory.QuantityBought(Tuning.Trade.commodityBundleCost * markup, commodity, Location);
                var offer = new CommodityOffer(commodity, (float)Math.Ceiling(saleQuantity));
                var price = saleQuantity * commodity.Value(Location);
                yield return new ProposeBuySell(price, offer, "S");
            }

        }

        // Offer segments from trade inventory if available
        if (Seller is Crawler crawler && crawler.TradeInv.Segments.Any()) {
            foreach (var segment in crawler.TradeInv.Segments.ToList()) {
                var markup = Tuning.Economy.LocalMarkup(segment.SegmentKind, Location);
                markup *= merchantMarkup;
                var price = segment.Cost * markup;
                yield return new ProposeSellBuy(new SegmentOffer(segment), price);
            }
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
