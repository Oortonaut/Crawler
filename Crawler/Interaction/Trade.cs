using Crawler.Economy;
using Crawler.Logging;
using Microsoft.Extensions.Logging;

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
    /// Top up a merchant's stock for sale: each good the location makes available is restocked to a
    /// wealth-based target, and the segments generated for sale last time are replaced by a new batch.
    /// The only step of trading that changes inventory; <see cref="MakeTradeOffers"/> lists what is on hand.
    /// </summary>
    /// <param name="forSale">Segments generated for sale by the previous restock; unsold ones are removed and the list refilled.</param>
    public static void Restock(this IActor Seller, ulong seed, float wealthFraction, List<Segment> forSale) {
        var rng = new XorShift(seed);
        var faction = Seller.Faction;
        var Location = Seller.Location;
        var wealth = Location.Wealth * wealthFraction;

        foreach (var commodity in Enum.GetValues<Commodity>().Where(c => c.IsTradeGood())) {
            // Small settlements roll almost nothing, so essentials are always restocked (the draw is kept for RNG stability)
            if (!(rng.NextSingle() < commodity.AvailabilityAt(Location) | commodity.IsEssential())) {
                continue;
            }
            // Top up to the target rather than adding it, so stock doesn't grow with every restock
            var shortfall = Inventory.QuantitySold(wealth, commodity, Location) - Seller.Inventory[commodity, Account.Trade];
            if (shortfall > 0) {
                Seller.Inventory.Add(commodity, shortfall, Account.Supplies | Account.Trade);
            }
        }

        // Replace last restock's unsold segments instead of piling up copies
        foreach (var segment in forSale.Where(s => Seller.Inventory.Segments.Contains(s))) {
            Seller.Inventory.Remove(segment);
        }
        forSale.Clear();
        int saleSegments = CrawlerEx.PoissonQuantile((float)Math.Log(wealth / 300 + 1), ref rng);
        while (saleSegments-- > 0) {
            var segment = rng.ChooseRandom(SegmentEx.AllDefs)!.NewSegment(rng.Seed());
            if (faction.GetPolicy(segment.SegmentKind) == TradePolicy.Prohibited && rng.NextSingle() < 0.5f) {
                continue;
            }
            // Segments are packaged by default, so just add them for use or trade
            Seller.Inventory.Add(segment, Account.Supplies | Account.Trade);
            forSale.Add(segment);
        }
    }

    /// <summary>
    /// List a merchant's offers from what it holds for trade: an ask for every good and segment on hand,
    /// and a bid on every legal good, so goods hauled in from elsewhere can be sold. Changes nothing.
    /// </summary>
    public static List<TradeOffer> MakeTradeOffers(this IActor Seller, ulong seed) {
        var offers = new List<TradeOffer>();
        var rng = new XorShift(seed);
        var faction = Seller.Faction;

        foreach (var commodity in Enum.GetValues<Commodity>().Where(c => c.IsTradeGood())) {
            var policy = faction.GetPolicy(commodity);
            // Ask only with at least one whole lot on hand; equilibrium stock leaves slivers no one can buy
            if (Seller.Inventory[commodity, Account.Trade] >= 1) {
                // Prohibited goods on hand are dealt half the time
                if (policy == TradePolicy.Prohibited && rng.NextSingle() > 0.5f) {
                    continue;
                }
                offers.Add(new TradeOffer {
                    Commodity = commodity,
                    Direction = TradeDirection.Sell, // Trader sells (player buys)
                    PricePerUnit = Seller.GetAskPrice(commodity),
                    Quantity = 1
                });
            } else if (policy == TradePolicy.Prohibited) {
                continue;
            }
            offers.Add(new TradeOffer {
                Commodity = commodity,
                Direction = TradeDirection.Buy, // Trader buys (player sells)
                PricePerUnit = Seller.GetBidPrice(commodity),
                Quantity = 1
            });
        }

        foreach (var segment in Seller.Inventory.SegmentsIn(Account.Trade)) {
            if (faction.GetPolicy(segment.SegmentKind) == TradePolicy.Prohibited && rng.NextSingle() < 0.5f) {
                continue;
            }
            // Same ask as commodities: mid price (markups, policy) plus this trader's spread
            offers.Add(new TradeOffer {
                Segment = segment,
                Direction = TradeDirection.Sell,
                PricePerUnit = Seller.GetAskPrice(segment),
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
        return location.GetEncounter().Settlements.OfType<Crawler>().FirstOrDefault();
    }

    /// <summary>
    /// NPC buys commodity from settlement. Transfers scrap from buyer to settlement,
    /// and commodity from settlement to buyer.
    /// </summary>
    public static bool TryBuyFromSettlement(Crawler buyer, Commodity commodity, float maxQty) {
        var settlement = FindSettlement(buyer.Location);
        if (settlement == null) return false;

        float available = settlement.Inventory[commodity, Account.Trade];
        float qty = Math.Min(maxQty, available);
        if (qty <= 0) return false;

        // Buyer pays settlement's ask price
        float price = settlement.GetAskPrice(commodity);
        float cost = qty * price;

        // Adjust quantity if buyer can't afford full amount
        float buyerScrap = buyer.Inventory[Commodity.Scrap];
        if (buyerScrap < cost) {
            qty = buyerScrap / price;
            cost = qty * price;
        }
        if (qty <= 0) return false;

        // Check buyer has cargo space
        float volumeNeeded = qty * commodity.Volume();
        if (buyer.Inventory.AvailableVolume < volumeNeeded) {
            qty = buyer.Inventory.AvailableVolume / commodity.Volume();
            cost = qty * price;
        }
        if (qty <= 0) return false;

        // Execute transfer
        buyer.Inventory.Remove(Commodity.Scrap, cost);
        settlement.Inventory.Add(Commodity.Scrap, cost);
        settlement.Inventory.Remove(commodity, qty, Account.Trade);
        buyer.Inventory.Add(commodity, qty, Account.Supplies | Account.Trade);

        // NPC trades drive the simulated economy; Message() is dropped for anyone but the player
        LogCat.Log.LogTrace("SettlementTrade buy {Buyer} {Commodity} {Qty} from {Settlement} cost {Cost}",
            buyer.Name, commodity, qty, settlement.Name, cost);
        return true;
    }

    /// <summary>
    /// NPC sells commodity to settlement. Transfers commodity from seller to settlement,
    /// and scrap from settlement to seller.
    /// </summary>
    public static bool TrySellToSettlement(Crawler seller, Commodity commodity, float maxQty) {
        var settlement = FindSettlement(seller.Location);
        if (settlement == null) return false;

        // Check seller has the commodity (anywhere but the reserve)
        const Account sellable = Account.Supplies | Account.Trade;
        float qty = Math.Min(maxQty, seller.Inventory[commodity, sellable]);
        if (qty <= 0) return false;

        // Seller receives settlement's bid price
        float price = settlement.GetBidPrice(commodity);
        float revenue = qty * price;

        // Check settlement can afford (settlements have limited scrap)
        float settlementScrap = settlement.Inventory[Commodity.Scrap];
        if (settlementScrap < revenue) {
            qty = settlementScrap / price;
            revenue = qty * price;
        }
        if (qty <= 0) return false;

        // Execute transfer - the draw order takes from supplies first (for harvesters)
        seller.Inventory.Remove(commodity, qty, sellable);

        settlement.Inventory.Add(commodity, qty, Account.Supplies | Account.Trade);
        settlement.Inventory.Remove(Commodity.Scrap, revenue);
        seller.Inventory.Add(Commodity.Scrap, revenue);

        LogCat.Log.LogTrace("SettlementTrade sell {Seller} {Commodity} {Qty} to {Settlement} revenue {Revenue}",
            seller.Name, commodity, qty, settlement.Name, revenue);
        return true;
    }
}
