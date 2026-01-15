namespace Crawler;

/// <summary>
/// Component that handles population consumption at settlements.
/// Settlements consume rations, water, medicines, and consumer goods based on population.
/// This creates ongoing demand that drives production.
/// </summary>
public class PopulationConsumptionComponent : ActorComponentBase {
    public override int Priority => 50; // Run after all other decisions

    public override void Enter(Encounter encounter) { }
    public override void Leave(Encounter encounter) { }

    public override void Tick() {
        if (Owner is not Crawler crawler) return;
        if (!crawler.Flags.HasFlag(ActorFlags.Settlement)) return;

        var population = crawler.Location.Population;
        if (population <= 0) return;

        var hours = (float)Owner.Elapsed.TotalHours;
        if (hours <= 0) return;

        // Calculate consumption for this tick
        ConsumeEssentials(crawler, population, hours);
        ConsumeGoods(crawler, population, hours);

        // Update prices based on new stock levels
        crawler.Stock?.UpdatePrices(crawler.Cargo);
    }

    /// <summary>
    /// Consume essential supplies: rations, water, medicines.
    /// </summary>
    void ConsumeEssentials(Crawler crawler, int population, float hours) {
        // Rations
        float rationsNeeded = population * Tuning.Settlement.RationsPerPopPerHour * hours;
        float rationsConsumed = Math.Min(crawler.Supplies[Commodity.Rations], rationsNeeded);
        crawler.Supplies.Remove(Commodity.Rations, rationsConsumed);

        // Water
        float waterNeeded = population * Tuning.Settlement.WaterPerPopPerHour * hours;
        float waterConsumed = Math.Min(crawler.Supplies[Commodity.Water], waterNeeded);
        crawler.Supplies.Remove(Commodity.Water, waterConsumed);

        // Medicines (chronic care, preventive medicine)
        float medsNeeded = population * Tuning.Settlement.MedicinesPerPopPerHour * hours;
        float medsConsumed = Math.Min(crawler.Supplies[Commodity.Medicines], medsNeeded);
        crawler.Supplies.Remove(Commodity.Medicines, medsConsumed);

        // Track shortages for morale effects (future enhancement)
        // If rationsConsumed < rationsNeeded, population is going hungry
        // If waterConsumed < waterNeeded, water rationing
        // If medsConsumed < medsNeeded, healthcare lacking
    }

    /// <summary>
    /// Consume consumer goods proportionally based on availability.
    /// Goods include textiles, toys, media, liquor, etc.
    /// </summary>
    void ConsumeGoods(Crawler crawler, int population, float hours) {
        float totalGoodsNeeded = population * Tuning.Settlement.GoodsPerPopPerHour * hours;

        // Consumer goods that settlements consume
        Commodity[] consumerGoods = [
            Commodity.Textiles,
            Commodity.Toys,
            Commodity.Media,
            Commodity.Liquor,
        ];

        // Calculate total available
        float totalAvailable = 0;
        foreach (var commodity in consumerGoods) {
            totalAvailable += crawler.Supplies[commodity];
        }

        if (totalAvailable <= 0) return;

        // Consume proportionally based on availability
        float consumeFraction = Math.Min(1.0f, totalGoodsNeeded / totalAvailable);
        foreach (var commodity in consumerGoods) {
            float amount = crawler.Supplies[commodity] * consumeFraction;
            crawler.Supplies.Remove(commodity, amount);
        }
    }
}
