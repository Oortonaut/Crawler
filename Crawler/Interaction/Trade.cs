namespace Crawler;

// Offers and Exchange Interactions
//=========================================================================

// Direction of trade from trader's perspective
public enum TradeDirection {
    Sell,  // Trader sells to player (player buys)
    Buy    // Trader buys from player (player sells)
}

// Simple trade listing: commodity/segment, direction, price, quantity
public record TradeOffer {
    public Commodity? Commodity { get; init; }
    public Segment? Segment { get; init; }
    public TradeDirection Direction { get; init; }
    public float PricePerUnit { get; init; }
    public float Quantity { get; init; }
    public bool IsCommodity => Commodity.HasValue;
    public bool IsSegment => Segment != null;
}

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


public static class TradeEx {
    /// <summary>
    /// Generate trade offer listings for a merchant
    /// </summary>
    public static List<TradeOffer> MakeTradeOffers(this IActor Seller, ulong seed, float wealthFraction) {
        var offers = new List<TradeOffer>();
        var rng = new XorShift(seed);
        var faction = Seller.Faction;
        var Location = Seller.Location;
        var wealth = Location.Wealth * wealthFraction;

        var tradeComponent = (Seller as ActorBase)?.Components.OfType<TradeOfferComponent>().FirstOrDefault();
        float merchantMarkup = tradeComponent?.Markup ?? 1.0f;

        var commodities = Enum.GetValues<Commodity>()
            .Where(s => s != Commodity.Scrap)
            .Where(s => rng.NextSingle() < s.AvailabilityAt(Location))
            .ToList();

        float CFrac = wealth;

        foreach (var commodity in commodities) {
            var policy = faction.GetPolicy(commodity);

            if (policy == TradePolicy.Prohibited && rng.NextSingle() > 0.5f) {
                continue;
            }

            var locationMarkup = Tuning.Economy.LocalMarkup(commodity, Location);
            var scarcityPremium = commodity.ScarcityPremium(Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            // Dynamic stock multiplier: low stock = higher prices, high stock = lower prices
            var stockMultiplier = Seller.Stock?.PriceMultiplier(commodity, Seller.Cargo[commodity]) ?? 1.0f;

            float midPrice = commodity.CostAt(Location) * scarcityPremium * policyMultiplier * stockMultiplier;

            float bidAskSpread = Tuning.Trade.baseBidAskSpread;
            bidAskSpread *= tradeComponent?.Spread ?? 1.0f;
            float spreadAmount = midPrice * bidAskSpread;

            float askPrice = midPrice + spreadAmount / 2;
            float bidPrice = midPrice - spreadAmount / 2;

            var quantity = Inventory.QuantitySold(CFrac / locationMarkup, commodity, Location);
            Seller.Cargo[commodity] += quantity;

            var saleQuantity = 1f;

            // Trader sells (player buys)
            offers.Add(new TradeOffer {
                Commodity = commodity,
                Direction = TradeDirection.Sell,
                PricePerUnit = askPrice,
                Quantity = (float)Math.Floor(saleQuantity)
            });

            // Trader buys (player sells)
            offers.Add(new TradeOffer {
                Commodity = commodity,
                Direction = TradeDirection.Buy,
                PricePerUnit = bidPrice,
                Quantity = (float)Math.Ceiling(saleQuantity)
            });
        }

        // Offer segments from trade inventory
        foreach (var segment in Seller.Cargo.Segments.ToList()) {
            var policy = faction.GetPolicy(segment.SegmentKind);

            if (policy == TradePolicy.Prohibited && rng.NextSingle() < 0.5f) {
                continue;
            }

            var localCost = segment.CostAt(Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            var price = localCost * merchantMarkup * policyMultiplier;

            offers.Add(new TradeOffer {
                Segment = segment,
                Direction = TradeDirection.Sell,
                PricePerUnit = price,
                Quantity = 1
            });
        }

        // Generate segments for sale
        int SaleSegments = CrawlerEx.PoissonQuantile((float)Math.Log(Location.Wealth * wealthFraction / 300 + 1), ref rng);
        while (SaleSegments-- > 0) {
            var segmentDef = rng.ChooseRandom(SegmentEx.AllDefs)!;
            var segment = segmentDef.NewSegment(rng.Seed());

            var policy = faction.GetPolicy(segment.SegmentKind);

            if (policy == TradePolicy.Prohibited && rng.NextSingle() < 0.5f) {
                continue;
            }

            var markup = Tuning.Economy.LocalMarkup(segment.SegmentKind, Location);
            var policyMultiplier = Tuning.Trade.PolicyMultiplier(policy);
            markup *= merchantMarkup * policyMultiplier;

            // Segments are packaged by default, so just add to cargo
            Seller.Cargo.Segments.Add(segment);
            var price = segment.Cost * markup;

            offers.Add(new TradeOffer {
                Segment = segment,
                Direction = TradeDirection.Sell,
                PricePerUnit = price,
                Quantity = 1
            });
        }

        return offers;
    }
}
