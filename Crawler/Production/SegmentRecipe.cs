namespace Crawler.Production;

/// <summary>
/// Recipe for manufacturing a segment from raw materials.
/// Similar to ProductionRecipe but outputs a segment instead of commodities.
/// </summary>
public record SegmentRecipe(
    string Name,
    SegmentDef OutputDef,
    IReadOnlyDictionary<Commodity, float> Inputs,
    IReadOnlyDictionary<Commodity, float> Consumables,
    IReadOnlyDictionary<Commodity, float> Maintenance,
    float CrewRequired,
    float ActivateCharge,
    TimeDuration CycleTime,
    GameTier TechTier
) {
    /// <summary>
    /// Minimum industry segment size required to manufacture this segment.
    /// Formula: OutputDef.Size + 2 (a size 5 fabricator can make size 3 segments).
    /// </summary>
    public int RequiredIndustrySize => (int)OutputDef.Size.Size + 2;

    /// <summary>
    /// The industry type required to manufacture segments (always Fabricator or Assembler).
    /// </summary>
    public IndustryType RequiredIndustry => OutputDef.SegmentKind switch {
        SegmentKind.Offense => IndustryType.Assembler,
        SegmentKind.Defense => IndustryType.Fabricator,
        SegmentKind.Power => IndustryType.Fabricator,
        SegmentKind.Traction => IndustryType.Fabricator,
        SegmentKind.Industry => IndustryType.Fabricator,
        SegmentKind.Storage => IndustryType.Fabricator,
        SegmentKind.Harvest => IndustryType.Fabricator,
        _ => IndustryType.Fabricator,
    };

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
    /// </summary>
    public float ConsumeMaintenance(Inventory inventory, float fraction) {
        if (Maintenance.Count == 0) return 1.0f;

        float totalRequired = 0;
        float totalAvailable = 0;
        foreach (var (commodity, amount) in Maintenance) {
            totalRequired += amount;
            totalAvailable += Math.Min(inventory[commodity], amount);
        }
        float satisfied = totalRequired > 0 ? totalAvailable / totalRequired : 1.0f;

        foreach (var (commodity, amount) in Maintenance) {
            float consume = Math.Min(inventory[commodity], amount * fraction);
            inventory.Remove(commodity, consume);
        }
        return satisfied;
    }

    /// <summary>
    /// Create the output segment with a given seed.
    /// </summary>
    public Segment ProduceSegment(ulong seed) => OutputDef.NewSegment(seed);
}

/// <summary>
/// Recipe for upgrading an existing segment with a modification.
/// The upgrade creates a new SegmentDef and transfers instance state.
/// </summary>
public record UpgradeRecipe(
    string Name,
    string UpgradePrefix,
    SegmentKind[] ApplicableKinds,
    Func<SegmentDef, SegmentDef> ApplyUpgrade,
    float CostMultiplier,
    IReadOnlyDictionary<Commodity, float> Materials,
    float CrewRequired,
    TimeDuration CycleTime,
    GameTier TechTier
) {
    /// <summary>
    /// Check if this upgrade can be applied to the given segment.
    /// </summary>
    public bool CanApplyTo(Segment segment) {
        // Check if segment kind is applicable
        if (!ApplicableKinds.Contains(segment.SegmentKind)) return false;

        // Check if upgrade is already applied (name contains prefix)
        if (segment.Name.Contains(UpgradePrefix)) return false;

        return true;
    }

    /// <summary>
    /// Calculate the total cost for upgrading the given segment.
    /// </summary>
    public float TotalCost(Segment segment) => segment.Cost * CostMultiplier;

    /// <summary>
    /// Calculate material costs scaled by segment cost.
    /// </summary>
    public IReadOnlyDictionary<Commodity, float> ScaledMaterials(Segment segment) {
        var cost = segment.Cost;
        return Materials.ToDictionary(
            kv => kv.Key,
            kv => kv.Value * cost
        );
    }
}
