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
    public static float GetMidPrice(this IActor actor, Commodity commodity) =>
        actor.MidPrice(commodity, actor.GetTradeComponent());

    // Prices are on hot paths (every MidAt walks every actor), so callers look the component up once and pass it
    static float MidPrice(this IActor actor, Commodity commodity, TradeOfferComponent? trade) =>
        actor.GetBasePrice(commodity)
        * actor.Location.LocalMarkup[commodity]                                  // location's per-commodity variation
        * (trade?.Markup ?? 1.0f)                                                // actor's personal markup
        * Tuning.Trade.PolicyMultiplier(actor.Faction.GetPolicy(commodity));    // faction policy

    static float SpreadOf(TradeOfferComponent? trade) => Tuning.Trade.baseBidAskSpread * (trade?.Spread ?? 1.0f);

    /// <summary>
    /// Get this actor's ask price (selling price) for a commodity.
    /// Ask = MidPrice × (1 + Spread/2)
    /// </summary>
    public static float GetAskPrice(this IActor actor, Commodity commodity) {
        var trade = actor.GetTradeComponent();
        return Tuning.Trade.AskFrom(actor.MidPrice(commodity, trade), SpreadOf(trade));
    }

    /// <summary>
    /// Get this actor's bid price (buying price) for a commodity.
    /// Bid = MidPrice × (1 - Spread/2)
    /// </summary>
    public static float GetBidPrice(this IActor actor, Commodity commodity) {
        var trade = actor.GetTradeComponent();
        return Tuning.Trade.BidFrom(actor.MidPrice(commodity, trade), SpreadOf(trade));
    }

    /// <summary>
    /// Get the ask offer (sell order) this actor provides for a commodity.
    /// Quantity is how much they hold in stacks marked for trade.
    /// </summary>
    public static MarketOffer GetAskOffer(this IActor actor, Commodity commodity) {
        float price = actor.GetAskPrice(commodity);
        float quantity = actor.Inventory[commodity, Account.Trade];
        return new MarketOffer(actor, price, quantity);
    }

    /// <summary>
    /// Get the bid offer (buy order) this actor provides for a commodity.
    /// Quantity is their purchasing power (Scrap / price).
    /// </summary>
    public static MarketOffer GetBidOffer(this IActor actor, Commodity commodity) {
        float price = actor.GetBidPrice(commodity);
        if (price <= 0) return default;

        float scrap = actor.Inventory[Commodity.Scrap];
        float maxQuantity = scrap / price;

        return new MarketOffer(actor, price, maxQuantity);
    }

    #region Segment Pricing

    /// <summary>
    /// Get the mid-market price for a specific segment from this actor's perspective.
    /// Applies: baseCost × EncounterMarkup × TerrainMarkup × ActorMarkup × Policy
    /// </summary>
    public static float GetMidPrice(this IActor actor, Segment segment) =>
        actor.MidPrice(segment, actor.GetTradeComponent());

    static float MidPrice(this IActor actor, Segment segment, TradeOfferComponent? trade) =>
        segment.Cost
        * Tuning.Economy.EncounterSegmentKindMarkup[actor.Location.Type][segment.SegmentKind]
        * Tuning.Economy.LocationSegmentKindMarkup[actor.Location.Terrain][segment.SegmentKind]
        * (trade?.Markup ?? 1.0f)
        * Tuning.Trade.PolicyMultiplier(actor.Faction.GetPolicy(segment.SegmentKind));

    /// <summary>
    /// Get this actor's ask price (selling price) for a segment.
    /// Ask = MidPrice × (1 + Spread/2)
    /// </summary>
    public static float GetAskPrice(this IActor actor, Segment segment) {
        var trade = actor.GetTradeComponent();
        return Tuning.Trade.AskFrom(actor.MidPrice(segment, trade), SpreadOf(trade));
    }

    /// <summary>
    /// Get this actor's bid price (buying price) for a segment.
    /// Bid = MidPrice × (1 - Spread/2)
    /// </summary>
    public static float GetBidPrice(this IActor actor, Segment segment) {
        var trade = actor.GetTradeComponent();
        return Tuning.Trade.BidFrom(actor.MidPrice(segment, trade), SpreadOf(trade));
    }

    #endregion
}
