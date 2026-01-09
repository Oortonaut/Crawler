namespace Crawler.Production;

/// <summary>
/// Extension methods and static data for production recipes.
/// </summary>
public static class RecipeEx {
    // Helper to create immutable dictionaries more concisely
    static IReadOnlyDictionary<Commodity, float> D(params (Commodity c, float amt)[] items) =>
        items.ToDictionary(x => x.c, x => x.amt);

    static IReadOnlyDictionary<Commodity, float> Empty => D();

    /// <summary>
    /// All available production recipes in the game.
    /// </summary>
    public static IReadOnlyList<ProductionRecipe> AllRecipes { get; } = [
        // ============================================
        // TIER 1: REFINING (Raw -> Refined)
        // ============================================

        new ProductionRecipe(
            Name: "Smelt Ore",
            RequiredIndustry: IndustryType.Refinery,
            Inputs: D((Commodity.Ore, 3.0f)),
            Consumables: D((Commodity.Fuel, 0.5f)),
            Maintenance: D((Commodity.SpareParts, 0.05f)),
            Outputs: D((Commodity.Metal, 1.0f)),
            Waste: D((Commodity.Slag, 0.5f)),
            CrewRequired: 2,
            ActivateCharge: 10,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Early
        ),

        new ProductionRecipe(
            Name: "Process Biomass",
            RequiredIndustry: IndustryType.Refinery,
            Inputs: D((Commodity.Biomass, 2.0f)),
            Consumables: D((Commodity.Fuel, 0.3f)),
            Maintenance: D((Commodity.Lubricants, 0.02f)),
            Outputs: D((Commodity.Chemicals, 1.0f)),
            Waste: D((Commodity.Slag, 0.2f)),
            CrewRequired: 1,
            ActivateCharge: 8,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Early
        ),

        new ProductionRecipe(
            Name: "Fuse Silicates",
            RequiredIndustry: IndustryType.Refinery,
            Inputs: D((Commodity.Silicates, 2.5f)),
            Consumables: D((Commodity.Fuel, 0.4f)),
            Maintenance: D((Commodity.SpareParts, 0.03f)),
            Outputs: D((Commodity.Glass, 1.0f)),
            Waste: D((Commodity.Slag, 0.3f)),
            CrewRequired: 1,
            ActivateCharge: 12,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Early
        ),

        new ProductionRecipe(
            Name: "Enrich Isotopes",
            RequiredIndustry: IndustryType.Refinery,
            Inputs: D((Commodity.Isotopes, 2.0f)),
            Consumables: D((Commodity.Fuel, 1.0f), (Commodity.Coolant, 0.2f)),
            Maintenance: D((Commodity.SpareParts, 0.1f)),
            Outputs: D((Commodity.Nanomaterials, 0.5f)),
            Waste: D((Commodity.Slag, 0.8f)),
            CrewRequired: 3,
            ActivateCharge: 50,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Late
        ),

        // ============================================
        // TIER 2: FABRICATION (Refined -> Parts)
        // ============================================

        new ProductionRecipe(
            Name: "Forge Alloys",
            RequiredIndustry: IndustryType.Fabricator,
            Inputs: D((Commodity.Metal, 2.0f), (Commodity.Chemicals, 1.0f)),
            Consumables: D((Commodity.Fuel, 1.0f), (Commodity.Lubricants, 0.1f)),
            Maintenance: D((Commodity.SpareParts, 0.08f)),
            Outputs: D((Commodity.Alloys, 1.0f)),
            Waste: D((Commodity.Slag, 0.3f)),
            CrewRequired: 2,
            ActivateCharge: 20,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Mid
        ),

        new ProductionRecipe(
            Name: "Synthesize Polymers",
            RequiredIndustry: IndustryType.Fabricator,
            Inputs: D((Commodity.Chemicals, 2.0f)),
            Consumables: D((Commodity.Fuel, 0.5f)),
            Maintenance: D((Commodity.Lubricants, 0.05f)),
            Outputs: D((Commodity.Polymers, 1.0f)),
            Waste: D((Commodity.Slag, 0.1f)),
            CrewRequired: 1,
            ActivateCharge: 15,
            CycleTime: TimeDuration.FromHours(1.5),
            TechTier: GameTier.Mid
        ),

        new ProductionRecipe(
            Name: "Fire Ceramics",
            RequiredIndustry: IndustryType.Fabricator,
            Inputs: D((Commodity.Glass, 1.5f), (Commodity.Metal, 0.5f)),
            Consumables: D((Commodity.Fuel, 0.8f)),
            Maintenance: D((Commodity.SpareParts, 0.04f)),
            Outputs: D((Commodity.Ceramics, 1.0f)),
            Waste: D((Commodity.Slag, 0.2f)),
            CrewRequired: 2,
            ActivateCharge: 18,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Mid
        ),

        new ProductionRecipe(
            Name: "Fabricate Electronics",
            RequiredIndustry: IndustryType.Fabricator,
            Inputs: D((Commodity.Metal, 1.0f), (Commodity.Glass, 0.5f), (Commodity.Chemicals, 0.5f)),
            Consumables: D((Commodity.Fuel, 0.3f), (Commodity.Coolant, 0.1f)),
            Maintenance: D((Commodity.SpareParts, 0.1f), (Commodity.Lubricants, 0.05f)),
            Outputs: D((Commodity.Electronics, 1.0f)),
            Waste: D((Commodity.Slag, 0.15f)),
            CrewRequired: 3,
            ActivateCharge: 25,
            CycleTime: TimeDuration.FromHours(3),
            TechTier: GameTier.Late
        ),

        new ProductionRecipe(
            Name: "Mix Explosives",
            RequiredIndustry: IndustryType.Fabricator,
            Inputs: D((Commodity.Chemicals, 2.0f), (Commodity.Metal, 0.5f)),
            Consumables: D((Commodity.Fuel, 0.2f)),
            Maintenance: D((Commodity.SpareParts, 0.02f)),
            Outputs: D((Commodity.Explosives, 1.0f)),
            Waste: Empty,
            CrewRequired: 2,
            ActivateCharge: 5,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Mid
        ),

        // ============================================
        // TIER 3: ASSEMBLY (Parts -> Consumer/Ammo)
        // ============================================

        new ProductionRecipe(
            Name: "Manufacture Slugs",
            RequiredIndustry: IndustryType.Assembler,
            Inputs: D((Commodity.Metal, 1.0f), (Commodity.Explosives, 0.2f)),
            Consumables: D((Commodity.Fuel, 0.1f)),
            Maintenance: D((Commodity.SpareParts, 0.02f)),
            Outputs: D((Commodity.Slugs, 10.0f)),
            Waste: D((Commodity.Slag, 0.1f)),
            CrewRequired: 1,
            ActivateCharge: 8,
            CycleTime: TimeDuration.FromMinutes(30),
            TechTier: GameTier.Early
        ),

        new ProductionRecipe(
            Name: "Manufacture Cells",
            RequiredIndustry: IndustryType.Assembler,
            Inputs: D((Commodity.Electronics, 0.5f), (Commodity.Chemicals, 0.3f)),
            Consumables: D((Commodity.Fuel, 0.2f), (Commodity.Coolant, 0.05f)),
            Maintenance: D((Commodity.SpareParts, 0.05f)),
            Outputs: D((Commodity.Cells, 5.0f)),
            Waste: D((Commodity.Slag, 0.1f)),
            CrewRequired: 2,
            ActivateCharge: 15,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Mid
        ),

        new ProductionRecipe(
            Name: "Manufacture Rockets",
            RequiredIndustry: IndustryType.Assembler,
            Inputs: D((Commodity.Alloys, 0.5f), (Commodity.Explosives, 1.0f), (Commodity.Electronics, 0.2f)),
            Consumables: D((Commodity.Fuel, 0.3f)),
            Maintenance: D((Commodity.SpareParts, 0.08f), (Commodity.Lubricants, 0.03f)),
            Outputs: D((Commodity.Rockets, 3.0f)),
            Waste: D((Commodity.Slag, 0.2f)),
            CrewRequired: 3,
            ActivateCharge: 20,
            CycleTime: TimeDuration.FromHours(2),
            TechTier: GameTier.Mid
        ),

        new ProductionRecipe(
            Name: "Produce Medicines",
            RequiredIndustry: IndustryType.Assembler,
            Inputs: D((Commodity.Chemicals, 1.0f), (Commodity.Biomass, 0.5f)),
            Consumables: D((Commodity.Fuel, 0.1f)),
            Maintenance: D((Commodity.SpareParts, 0.02f)),
            Outputs: D((Commodity.Medicines, 5.0f)),
            Waste: Empty,
            CrewRequired: 2,
            ActivateCharge: 10,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Mid
        ),

        new ProductionRecipe(
            Name: "Assemble Machines",
            RequiredIndustry: IndustryType.Assembler,
            Inputs: D((Commodity.Alloys, 2.0f), (Commodity.Electronics, 1.0f), (Commodity.Polymers, 0.5f)),
            Consumables: D((Commodity.Fuel, 0.5f), (Commodity.Lubricants, 0.1f)),
            Maintenance: D((Commodity.SpareParts, 0.15f)),
            Outputs: D((Commodity.Machines, 1.0f)),
            Waste: D((Commodity.Slag, 0.3f)),
            CrewRequired: 4,
            ActivateCharge: 35,
            CycleTime: TimeDuration.FromHours(4),
            TechTier: GameTier.Late
        ),

        new ProductionRecipe(
            Name: "Manufacture Small Arms",
            RequiredIndustry: IndustryType.Assembler,
            Inputs: D((Commodity.Metal, 0.5f), (Commodity.Polymers, 0.2f)),
            Consumables: D((Commodity.Fuel, 0.1f)),
            Maintenance: D((Commodity.SpareParts, 0.03f)),
            Outputs: D((Commodity.SmallArms, 5.0f)),
            Waste: D((Commodity.Slag, 0.05f)),
            CrewRequired: 1,
            ActivateCharge: 6,
            CycleTime: TimeDuration.FromMinutes(45),
            TechTier: GameTier.Mid
        ),

        // ============================================
        // RECYCLING (Waste -> Raw)
        // ============================================

        new ProductionRecipe(
            Name: "Reclaim Slag",
            RequiredIndustry: IndustryType.Recycler,
            Inputs: D((Commodity.Slag, 5.0f)),
            Consumables: D((Commodity.Fuel, 0.3f)),
            Maintenance: D((Commodity.SpareParts, 0.02f)),
            Outputs: D((Commodity.Ore, 1.0f)),
            Waste: Empty,
            CrewRequired: 1,
            ActivateCharge: 8,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Early
        ),

        new ProductionRecipe(
            Name: "Recover Scrap",
            RequiredIndustry: IndustryType.Recycler,
            Inputs: D((Commodity.Scrap, 100.0f)),
            Consumables: D((Commodity.Fuel, 0.2f)),
            Maintenance: D((Commodity.Lubricants, 0.01f)),
            Outputs: D((Commodity.Metal, 0.5f), (Commodity.Ore, 0.3f)),
            Waste: D((Commodity.Slag, 0.2f)),
            CrewRequired: 1,
            ActivateCharge: 5,
            CycleTime: TimeDuration.FromMinutes(30),
            TechTier: GameTier.Early
        ),
    ];

    /// <summary>
    /// Get all recipes that can be executed by a given industry type.
    /// </summary>
    public static IEnumerable<ProductionRecipe> RecipesFor(IndustryType type) =>
        AllRecipes.Where(r => r.RequiredIndustry == type);

    /// <summary>
    /// Get all recipes available at or below a given tech tier.
    /// </summary>
    public static IEnumerable<ProductionRecipe> RecipesAtTier(GameTier maxTier) =>
        AllRecipes.Where(r => r.TechTier <= maxTier);

    /// <summary>
    /// Find a recipe by name (case-insensitive).
    /// </summary>
    public static ProductionRecipe? FindByName(string name) =>
        AllRecipes.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
