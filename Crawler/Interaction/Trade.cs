namespace Crawler;

// Offers and Exchange Interactions
//=========================================================================

// Trade policy defines how a faction treats a commodity category
public enum TradePolicy {
    Subsidized, // 0.7x base price - government subsidy or local production
    Legal, // 1.0x base price - normal trade
    Taxed, // 1.3x markup - import tariffs
    Controlled, // 1.75x markup. Can carry but can't transact without license
    Restricted, // higher markup, license fees, and tariffs. Illegal to carry without license.
    Prohibited // Cannot trade - illegal in this faction's territory. No carry or transactions allowed.
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
    public bool PairCapable(IActor agent, IActor subject) =>
        subject != agent &&
        Stuff.EnabledFor(agent, subject) &&
        !agent.Fighting(subject);
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
    public bool PairCapable(IActor agent, IActor subject) =>
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
    public static IEnumerable<IProposal> MakeTradeProposals(this IActor Seller, ulong seed, float wealthFraction) {
        var rng = new XorShift(seed);
        var faction = Seller.Faction;
        var Location = Seller.Location;
        var wealth = Location.Wealth * wealthFraction;

        Crawler? seller = Seller as Crawler;
        float merchantMarkup = seller?.Markup ?? 1.0f;

        var commodities = Enum.GetValues<Commodity>()
            .Where(s => s != Commodity.Scrap)
            .Where(s => rng.NextSingle() < s.AvailabilityAt(Location))
            .ToList();

        float CFrac = wealth;

        foreach (var commodity in commodities) {
            // Check faction policy for this commodity
            var policy = Tuning.FactionPolicies.GetPolicy(faction, commodity);

            // TODO: Make prohibited availability dependent on bandit faction standing
            if (policy == TradePolicy.Prohibited && rng.NextSingle() > 0.5f) {
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

            yield return new ProposeSellBuy(sellOffer, sellPrice, "B");

            var buyOffer = new CommodityOffer(commodity, (float)Math.Ceiling(saleQuantity));
            var buyPrice = saleQuantity * bidPrice;

            yield return new ProposeBuySell(buyPrice, buyOffer, "S");
        }

        // Offer segments from trade inventory if available
        foreach (var segment in seller?.Cargo.Segments.ToList() ?? []) {
            // Check faction policy for this segment kind
            var policy = Tuning.FactionPolicies.GetPolicy(faction, segment.SegmentKind);

            // Skip prohibited segments
            if (policy == TradePolicy.Prohibited && rng.NextSingle() < 0.5f) {
                continue;
            }

            var localCost =  segment.CostAt(Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            var price = localCost * merchantMarkup * policyMultiplier;

            yield return new ProposeSellBuy(new SegmentOffer(segment), price);
        }

        int SaleSegments = CrawlerEx.PoissonQuantile((float)Math.Log(Location.Wealth * wealthFraction / 300 + 1), ref rng);
        while (SaleSegments --> 0) {
            var segmentDef = rng.ChooseRandom(SegmentEx.AllDefs)!;
            var segment = segmentDef.NewSegment(rng.Seed());

            // Check faction policy for this segment kind
            var policy = Tuning.FactionPolicies.GetPolicy(faction, segment.SegmentKind);

            // Skip prohibited segments
            if (policy == TradePolicy.Prohibited && rng.NextSingle() < 0.5f) {
                continue;
            }

            var markup = Tuning.Economy.LocalMarkup(segment.SegmentKind, Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            markup *= merchantMarkup * policyMultiplier;
            Seller.Cargo.Segments.Add(segment);
            var price = segment.Cost * markup;

            yield return new ProposeSellBuy(new SegmentOffer(segment), price);
        }
    }
}
