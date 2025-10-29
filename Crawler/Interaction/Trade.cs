namespace Crawler;

// Offers and Exchange Interactions
//=========================================================================

// Trade policy defines how a faction treats a commodity category
public enum TradePolicy {
    Subsidized, // 0.7x base price - government subsidy or local production
    Legal, // 1.0x base price - normal trade
    Taxed, // 1.3x markup - import tariffs
    Controlled, // 1.75x markup + transaction fee - heavily regulated
    Prohibited // Cannot trade - illegal in this faction's territory
}

// Policy record encapsulates both commodity and segment trade policies
public record Policy(
    EArray<CommodityCategory, TradePolicy> Commodities,
    EArray<SegmentKind, TradePolicy> Segments,
    string Description
);

// Agent is seller
public record ProposeSellBuy(IOffer Stuff, float cash, string OptionCode = "T"): IProposal {
    public readonly float Cash = Commodity.Scrap.Round(cash);
    public bool AgentCapable(IActor agent) => true;
    public bool SubjectCapable(IActor subject) => true;
    public bool InteractionCapable(IActor agent, IActor subject) =>
        subject != agent && Stuff.DisabledFor(agent, subject) == null && !agent.To(subject).Hostile && !subject.To(agent).Hostile;
    public IEnumerable<IInteraction> GetInteractions(IActor Seller, IActor Buyer) {
        var interaction = new ExchangeInteraction(Buyer, new ScrapOffer(Cash), Seller, Stuff, OptionCode, Description);
        yield return interaction;
    }
    // Description is from theI  subjects POV
    public string Description => $"Buy {Stuff.Description} for {Cash}¢¢";
    public long ExpirationTime => 0; // Trade proposals never expire
    public override string ToString() => Description;
}

// Agent is buyer
public record ProposeBuySell(float cash, IOffer Stuff, string OptionCode = "T"): IProposal {
    public readonly float Cash = Commodity.Scrap.Round(cash);
    public bool AgentCapable(IActor agent) => true;
    public bool SubjectCapable(IActor subject) => true;
    public bool InteractionCapable(IActor agent, IActor subject) =>
        agent != subject && Stuff.DisabledFor(subject, agent) == null;
    public IEnumerable<IInteraction> GetInteractions(IActor Buyer, IActor Seller) {
        var interaction = new ExchangeInteraction(Buyer, new ScrapOffer(Cash), Seller, Stuff, OptionCode, Description);
        yield return interaction;
    }
    // Description is from theI  subjects POV
    public string Description => $"Sell {Stuff.Description} for {Cash}¢¢";
    public long ExpirationTime => 0; // Trade proposals never expire
    public override string ToString() => Description;
}

public static class TradeEx {
    public static IEnumerable<IProposal> MakeTradeProposals(this IActor Seller, float wealthFraction) {
        var faction = Seller.Faction;
        var Location = Seller.Location;
        var wealth = Location.Wealth * wealthFraction;

        // Use bandit markup for bandit faction, regular for others
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
            if (policy == TradePolicy.Prohibited && Random.Shared.NextSingle() > 0.5f) {
                continue;
            }

            // Calculate mid-price with location, scarcity, and policy markups
            var locationMarkup = Tuning.Economy.LocalMarkup(commodity, Location);
            var scarcityPremium = commodity.ScarcityPremium(Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);

            float midPrice = commodity.CostAt(Location) * scarcityPremium * policyMultiplier;

            // Calculate bid-ask spread
            float bidAskSpread = Tuning.Trade.baseBidAskSpread;
            bidAskSpread *= seller?.Spread ?? 1.0f;
            float spreadAmount = midPrice * bidAskSpread;

            // Ask price (player buys from NPC)
            float askPrice = midPrice + spreadAmount / 2;
            // Bid price (player sells to NPC)
            float bidPrice = midPrice - spreadAmount / 2;

            // Add commodity to seller's inventory
            var quantity = Inventory.QuantitySold(CFrac / locationMarkup, commodity, Location);
            Seller.Cargo[commodity] += quantity;

            // Create proposals
            var saleQuantity = 1f;
            var sellOffer = new CommodityOffer(commodity, (float)Math.Floor(saleQuantity));
            var sellPrice = saleQuantity * askPrice;

            // TODO: Transaction fees should count multiple buys as a single transaction
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
        foreach (var segment in seller?.Cargo.Segments.ToList() ?? []) {
            // Check faction policy for this segment kind
            var policy = Tuning.FactionPolicies.GetPolicy(faction, segment.SegmentKind);

            // Skip prohibited segments
            if (policy == TradePolicy.Prohibited) {
                continue;
            }

            var localCost =  segment.CostAt(Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            var price = localCost * merchantMarkup * policyMultiplier;

            // Add transaction fee for restricted segments
            if (policy == TradePolicy.Controlled) {
                price += Tuning.Trade.restrictedTransactionFee;
            }

            yield return new ProposeSellBuy(new SegmentOffer(segment), price);
        }

        int SaleSegments = CrawlerEx.SamplePoisson((float)Math.Log(Location.Wealth * wealthFraction / 300 + 1));
        while (SaleSegments --> 0) {
            var segmentDef = SegmentEx.AllDefs.ChooseRandom()!;
            var segment = segmentDef.NewSegment();

            // Check faction policy for this segment kind
            var policy = Tuning.FactionPolicies.GetPolicy(faction, segment.SegmentKind);

            // Skip prohibited segments
            if (policy == TradePolicy.Prohibited) {
                continue;
            }

            var markup = Tuning.Economy.LocalMarkup(segment.SegmentKind, Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            markup *= merchantMarkup * policyMultiplier;
            Seller.Cargo.Segments.Add(segment);
            var price = segment.Cost * markup;

            // Add transaction fee for restricted segments
            if (policy == TradePolicy.Controlled) {
                price += Tuning.Trade.restrictedTransactionFee;
            }

            yield return new ProposeSellBuy(new SegmentOffer(segment), price);
        }
    }
}
