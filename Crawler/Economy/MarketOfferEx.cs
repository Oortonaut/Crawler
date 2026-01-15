namespace Crawler.Economy;

/// <summary>
/// Extension methods for generating market offers and prices from actors.
/// </summary>
public static class MarketOfferEx {
    /// <summary>
    /// Get the TradeOfferComponent from an actor, if present.
    /// </summary>
    public static TradeOfferComponent? GetTradeComponent(this IActor actor) =>
        (actor as ActorBase)?.Components.OfType<TradeOfferComponent>().FirstOrDefault();

    /// <summary>
    /// Get the markup multiplier for an actor (from TradeOfferComponent or default 1.0).
    /// </summary>
    public static float GetMarkup(this IActor actor) =>
        actor.GetTradeComponent()?.Markup ?? 1.0f;

    /// <summary>
    /// Get the spread multiplier for an actor (from TradeOfferComponent or default 1.0).
    /// </summary>
    public static float GetSpread(this IActor actor) =>
        actor.GetTradeComponent()?.Spread ?? 1.0f;

    /// <summary>
    /// Get the base price for a commodity from this actor's perspective.
    /// Uses Stock.Price if available, otherwise CommodityEx.Data[c].InitialValue.
    /// </summary>
    public static float GetBasePrice(this IActor actor, Commodity commodity) {
        if (actor.Stock != null)
            return actor.Stock.Price(commodity);
        return CommodityEx.Data[commodity].InitialValue;
    }

    /// <summary>
    /// Get the mid-market price for a commodity from this actor's perspective.
    /// Applies: basePrice × LocalMarkup × Markup × PolicyMultiplier
    /// </summary>
    public static float GetMidPrice(this IActor actor, Commodity commodity) {
        float price = actor.GetBasePrice(commodity);

        // Apply location's per-commodity markup variation
        price *= actor.Location.LocalMarkup[commodity];

        // Apply actor's personal markup target
        price *= actor.GetMarkup();

        // Apply faction policy multiplier
        var policy = actor.Faction.GetPolicy(commodity);
        price *= Tuning.Trade.PolicyMultiplier(policy);

        return price;
    }

    /// <summary>
    /// Get this actor's ask price (selling price) for a commodity.
    /// Ask = MidPrice × (1 + Spread/2)
    /// </summary>
    public static float GetAskPrice(this IActor actor, Commodity commodity) {
        float midPrice = actor.GetMidPrice(commodity);
        float spread = Tuning.Trade.baseBidAskSpread * actor.GetSpread();
        return Commodity.Scrap.Round(midPrice * (1 + spread / 2));
    }

    /// <summary>
    /// Get this actor's bid price (buying price) for a commodity.
    /// Bid = MidPrice × (1 - Spread/2)
    /// </summary>
    public static float GetBidPrice(this IActor actor, Commodity commodity) {
        float midPrice = actor.GetMidPrice(commodity);
        float spread = Tuning.Trade.baseBidAskSpread * actor.GetSpread();
        return Commodity.Scrap.Round(midPrice * (1 - spread / 2));
    }

    /// <summary>
    /// Get the ask offer (sell order) this actor provides for a commodity.
    /// Quantity is how much they have available in Cargo.
    /// </summary>
    public static MarketOffer GetAskOffer(this IActor actor, Commodity commodity) {
        float price = actor.GetAskPrice(commodity);
        float quantity = actor.Cargo[commodity];
        return new MarketOffer(actor, price, quantity);
    }

    /// <summary>
    /// Get the bid offer (buy order) this actor provides for a commodity.
    /// Quantity is their purchasing power (Scrap / price).
    /// </summary>
    public static MarketOffer GetBidOffer(this IActor actor, Commodity commodity) {
        float price = actor.GetBidPrice(commodity);
        if (price <= 0) return default;

        float scrap = actor.Supplies[Commodity.Scrap];
        float maxQuantity = scrap / price;

        return new MarketOffer(actor, price, maxQuantity);
    }

    #region Segment Pricing

    /// <summary>
    /// Get the mid-market price for a specific segment from this actor's perspective.
    /// Applies: baseCost × EncounterMarkup × TerrainMarkup × ActorMarkup × Policy
    /// </summary>
    public static float GetMidPrice(this IActor actor, Segment segment) {
        float price = segment.Cost;

        // Apply encounter type markup
        price *= Tuning.Economy.EncounterSegmentKindMarkup[actor.Location.Type][segment.SegmentKind];

        // Apply terrain markup
        price *= Tuning.Economy.LocationSegmentKindMarkup[actor.Location.Terrain][segment.SegmentKind];

        // Apply actor's personal markup target
        price *= actor.GetMarkup();

        // Apply faction policy multiplier
        var policy = actor.Faction.GetPolicy(segment.SegmentKind);
        price *= Tuning.Trade.PolicyMultiplier(policy);

        return price;
    }

    /// <summary>
    /// Get this actor's ask price (selling price) for a segment.
    /// Ask = MidPrice × (1 + Spread/2)
    /// </summary>
    public static float GetAskPrice(this IActor actor, Segment segment) {
        float midPrice = actor.GetMidPrice(segment);
        float spread = Tuning.Trade.baseBidAskSpread * actor.GetSpread();
        return Commodity.Scrap.Round(midPrice * (1 + spread / 2));
    }

    /// <summary>
    /// Get this actor's bid price (buying price) for a segment.
    /// Bid = MidPrice × (1 - Spread/2)
    /// </summary>
    public static float GetBidPrice(this IActor actor, Segment segment) {
        float midPrice = actor.GetMidPrice(segment);
        float spread = Tuning.Trade.baseBidAskSpread * actor.GetSpread();
        return Commodity.Scrap.Round(midPrice * (1 - spread / 2));
    }

    /// <summary>
    /// Enumerate segment sell offers from this actor matching kind and size range.
    /// Searches both Cargo and Supplies for packaged segments.
    /// </summary>
    public static IEnumerable<SegmentMarketOffer> GetSegmentAskOffers(
        this IActor actor,
        SegmentKind kind,
        float minSize,
        float maxSize) {

        var segments = actor.Cargo.Segments
            .Concat(actor.Supplies.Segments)
            .Where(s => s.SegmentKind == kind)
            .Where(s => s.SegmentDef.Size.Size >= minSize && s.SegmentDef.Size.Size <= maxSize);

        foreach (var segment in segments) {
            float price = actor.GetAskPrice(segment);
            yield return new SegmentMarketOffer(actor, price, segment);
        }
    }

    /// <summary>
    /// Enumerate hypothetical segment buy offers from this actor for a given kind and size range.
    /// This represents what the actor would pay for segments in this range.
    /// Returns offers based on actor's purchasing power.
    /// </summary>
    public static IEnumerable<SegmentMarketOffer> GetSegmentBidOffers(
        this IActor actor,
        SegmentKind kind,
        float minSize,
        float maxSize) {

        // For bid offers on segments, we calculate the theoretical price the actor would pay
        // Since we don't have a specific segment, we use the average size
        float avgSize = (minSize + maxSize) / 2;
        float theoreticalCost = Tuning.Segments.CostTiers.Value(avgSize, 0);

        // Apply markups for this kind at actor's location
        float price = theoreticalCost;
        price *= Tuning.Economy.EncounterSegmentKindMarkup[actor.Location.Type][kind];
        price *= Tuning.Economy.LocationSegmentKindMarkup[actor.Location.Terrain][kind];
        price *= actor.GetMarkup();
        price *= Tuning.Trade.PolicyMultiplier(actor.Faction.GetPolicy(kind));

        // Apply bid spread
        float spread = Tuning.Trade.baseBidAskSpread * actor.GetSpread();
        price = Commodity.Scrap.Round(price * (1 - spread / 2));

        // Check if actor can afford anything
        float scrap = actor.Supplies[Commodity.Scrap];
        if (price > 0 && scrap >= price) {
            // Return a single theoretical bid offer (null segment indicates "wanting to buy")
            yield return new SegmentMarketOffer(actor, price, null!);
        }
    }

    #endregion
}
