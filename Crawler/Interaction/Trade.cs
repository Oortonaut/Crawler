using Crawler.Economy;

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

            // Use actor's market pricing (includes Markup and Spread)
            float askPrice = Seller.GetAskPrice(commodity);
            float bidPrice = Seller.GetBidPrice(commodity);

            var quantity = Inventory.QuantitySold(CFrac, commodity, Location);
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

            var price = segment.CostAt(Location, Seller, policy) * merchantMarkup;

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

            // Segments are packaged by default, so just add to cargo
            Seller.Cargo.Segments.Add(segment);
            var price = segment.CostAt(Location, Seller, policy) * merchantMarkup;

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

/// <summary>
/// Helper methods for NPC-to-settlement trade that affects both inventories.
/// </summary>
public static class SettlementTrade {
    /// <summary>
    /// Find the settlement actor at a location, if any.
    /// </summary>
    public static Crawler? FindSettlement(Location location) {
        if (!location.HasEncounter) return null;
        return location.GetEncounter().Actors.OfType<Crawler>()
            .FirstOrDefault(c => c.Role == Roles.Settlement);
    }

    /// <summary>
    /// NPC buys commodity from settlement. Transfers scrap from buyer to settlement,
    /// and commodity from settlement to buyer.
    /// </summary>
    public static bool TryBuyFromSettlement(Crawler buyer, Commodity commodity, float maxQty) {
        var settlement = FindSettlement(buyer.Location);
        if (settlement == null) return false;

        float available = settlement.Cargo[commodity];
        float qty = Math.Min(maxQty, available);
        if (qty <= 0) return false;

        // Buyer pays settlement's ask price
        float price = settlement.GetAskPrice(commodity);
        float cost = qty * price;

        // Adjust quantity if buyer can't afford full amount
        float buyerScrap = buyer.Supplies[Commodity.Scrap];
        if (buyerScrap < cost) {
            qty = buyerScrap / price;
            cost = qty * price;
        }
        if (qty <= 0) return false;

        // Check buyer has cargo space
        float volumeNeeded = qty * commodity.Volume();
        if (buyer.Cargo.AvailableVolume < volumeNeeded) {
            qty = buyer.Cargo.AvailableVolume / commodity.Volume();
            cost = qty * price;
        }
        if (qty <= 0) return false;

        // Execute transfer
        buyer.Supplies.Remove(Commodity.Scrap, cost);
        settlement.Supplies.Add(Commodity.Scrap, cost);
        settlement.Cargo.Remove(commodity, qty);
        buyer.Cargo.Add(commodity, qty);

        buyer.Message($"{buyer.Name} bought {qty:F1} {commodity} from {settlement.Name} for {cost:F0} scrap");
        return true;
    }

    /// <summary>
    /// NPC sells commodity to settlement. Transfers commodity from seller to settlement,
    /// and scrap from settlement to seller.
    /// </summary>
    public static bool TrySellToSettlement(Crawler seller, Commodity commodity, float maxQty) {
        var settlement = FindSettlement(seller.Location);
        if (settlement == null) return false;

        // Check seller has the commodity (in cargo or supplies)
        float haveInCargo = seller.Cargo[commodity];
        float haveInSupplies = seller.Supplies[commodity];
        float qty = Math.Min(maxQty, haveInCargo + haveInSupplies);
        if (qty <= 0) return false;

        // Seller receives settlement's bid price
        float price = settlement.GetBidPrice(commodity);
        float revenue = qty * price;

        // Check settlement can afford (settlements have limited scrap)
        float settlementScrap = settlement.Supplies[Commodity.Scrap];
        if (settlementScrap < revenue) {
            qty = settlementScrap / price;
            revenue = qty * price;
        }
        if (qty <= 0) return false;

        // Execute transfer - prefer taking from supplies first (for harvesters)
        float fromSupplies = Math.Min(qty, haveInSupplies);
        float fromCargo = qty - fromSupplies;

        if (fromSupplies > 0) seller.Supplies.Remove(commodity, fromSupplies);
        if (fromCargo > 0) seller.Cargo.Remove(commodity, fromCargo);

        settlement.Cargo.Add(commodity, qty);
        settlement.Supplies.Remove(Commodity.Scrap, revenue);
        seller.Supplies.Add(Commodity.Scrap, revenue);

        seller.Message($"{seller.Name} sold {qty:F1} {commodity} to {settlement.Name} for {revenue:F0} scrap");
        return true;
    }
}
