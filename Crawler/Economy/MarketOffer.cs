namespace Crawler.Economy;

/// <summary>
/// Represents a single bid or ask offer from an actor at a location.
/// Used to aggregate market prices from all actors in an encounter.
/// </summary>
/// <param name="Actor">The actor making this offer</param>
/// <param name="Price">Price per unit</param>
/// <param name="Quantity">Available quantity (ask) or purchasing power in units (bid)</param>
public readonly record struct MarketOffer(IActor Actor, float Price, float Quantity) {
    /// <summary>Total value of this offer (Price × Quantity)</summary>
    public float TotalValue => Price * Quantity;

    /// <summary>True if this offer has meaningful quantity and price</summary>
    public bool IsValid => Quantity > 0 && Price > 0;
}

/// <summary>
/// Represents a single bid or ask offer for a specific segment from an actor.
/// Unlike commodities, segments are discrete items with individual size/quality.
/// </summary>
/// <param name="Actor">The actor making this offer</param>
/// <param name="Price">Price for this specific segment</param>
/// <param name="Segment">The actual segment being offered</param>
public readonly record struct SegmentMarketOffer(IActor Actor, float Price, Segment Segment) {
    /// <summary>True if this is a valid segment offer</summary>
    public bool IsValid => Price > 0 && Segment != null;
}
