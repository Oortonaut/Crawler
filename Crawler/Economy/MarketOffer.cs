namespace Crawler.Economy;

/// <summary>
/// Represents a single bid or ask offer from an actor at a location.
/// Used to aggregate market prices from all actors in an encounter.
/// </summary>
/// <param name="Actor">The actor making this offer</param>
/// <param name="Price">Price per unit</param>
/// <param name="Quantity">Available quantity (ask) or purchasing power in units (bid)</param>
public readonly record struct MarketOffer(IActor Actor, float Price, float Quantity) {
    /// <summary>True if this offer has meaningful quantity and price</summary>
    public bool IsValid => Quantity > 0 && Price > 0;
}
