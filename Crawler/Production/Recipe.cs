namespace Crawler.Production;

/// <summary>
/// Defines a production recipe that converts inputs to outputs.
/// Each recipe requires:
/// - Inputs: Primary materials consumed in the conversion
/// - Consumables: Materials used up during production (fuel, reagents)
/// - Maintenance: Materials consumed gradually to prevent segment wear
/// - Crew: Personnel hours required per batch
/// - ActivateCharge: Reactor charge consumed per production cycle
/// </summary>
public record ProductionRecipe(
    string Name,
    IndustryType RequiredIndustry,
    IReadOnlyDictionary<Commodity, float> Inputs,
    IReadOnlyDictionary<Commodity, float> Consumables,
    IReadOnlyDictionary<Commodity, float> Maintenance,
    IReadOnlyDictionary<Commodity, float> Outputs,
    IReadOnlyDictionary<Commodity, float> Waste,
    float CrewRequired,
    float ActivateCharge,
    TimeDuration CycleTime,
    GameTier TechTier,
    float Wear = 1.0f
) {
    /// <summary>
    /// Check if all required inputs are available in the inventory.
    /// </summary>
    public bool HasInputs(Inventory inventory) {
        foreach (var (commodity, amount) in Inputs) {
            if (inventory[commodity] < amount) return false;
        }
        return true;
    }

    /// <summary>
    /// Check if all required consumables are available in the inventory.
    /// </summary>
    public bool HasConsumables(Inventory inventory) {
        foreach (var (commodity, amount) in Consumables) {
            if (inventory[commodity] < amount) return false;
        }
        return true;
    }

    /// <summary>
    /// Check if maintenance materials are available (partial is OK, but affects wear).
    /// </summary>
    public float MaintenanceFraction(Inventory inventory) {
        if (Maintenance.Count == 0) return 1.0f;

        float totalRequired = 0;
        float totalAvailable = 0;
        foreach (var (commodity, amount) in Maintenance) {
            totalRequired += amount;
            totalAvailable += Math.Min(inventory[commodity], amount);
        }
        return totalRequired > 0 ? totalAvailable / totalRequired : 1.0f;
    }

    /// <summary>
    /// Consume inputs proportionally to the given progress fraction.
    /// </summary>
    public void ConsumeInputs(Inventory inventory, float fraction) {
        foreach (var (commodity, amount) in Inputs) {
            inventory.Remove(commodity, amount * fraction);
        }
    }

    /// <summary>
    /// Consume consumables proportionally to the given progress fraction.
    /// </summary>
    public void ConsumeConsumables(Inventory inventory, float fraction) {
        foreach (var (commodity, amount) in Consumables) {
            inventory.Remove(commodity, amount * fraction);
        }
    }

    /// <summary>
    /// Consume maintenance materials proportionally to the given progress fraction.
    /// Returns the fraction of maintenance that was satisfied.
    /// </summary>
    public float ConsumeMaintenance(Inventory inventory, float fraction) {
        if (Maintenance.Count == 0) return 1.0f;

        float satisfied = MaintenanceFraction(inventory);
        foreach (var (commodity, amount) in Maintenance) {
            float consume = Math.Min(inventory[commodity], amount * fraction);
            inventory.Remove(commodity, consume);
        }
        return satisfied;
    }

    /// <summary>
    /// Produce outputs into the inventory.
    /// </summary>
    public void ProduceOutputs(Inventory inventory, float efficiency = 1.0f) {
        foreach (var (commodity, amount) in Outputs) {
            inventory.Add(commodity, amount * efficiency);
        }
    }

    /// <summary>
    /// Produce waste into the inventory.
    /// </summary>
    public void ProduceWaste(Inventory inventory) {
        foreach (var (commodity, amount) in Waste) {
            inventory.Add(commodity, amount);
        }
    }
}

/// <summary>
/// Type of industry segment required to execute a recipe.
/// </summary>
public enum IndustryType {
    Refinery,    // Raw -> Refined
    Fabricator,  // Refined -> Parts
    Assembler,   // Parts -> Consumer/Ammo
    Recycler,    // Waste -> Raw (lower efficiency)
}
