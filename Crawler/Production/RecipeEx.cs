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

    /// <summary>
    /// Calculate the total input cost for a recipe at a location.
    /// </summary>
    public static float InputCost(this ProductionRecipe recipe, Location location) =>
        recipe.Inputs.Sum(kv => kv.Key.MidAt(location) * kv.Value);

    /// <summary>
    /// Calculate the total consumable cost for a recipe at a location.
    /// </summary>
    public static float ConsumableCost(this ProductionRecipe recipe, Location location) =>
        recipe.Consumables.Sum(kv => kv.Key.MidAt(location) * kv.Value);

    /// <summary>
    /// Calculate the total maintenance cost for a recipe at a location.
    /// </summary>
    public static float MaintenanceCost(this ProductionRecipe recipe, Location location) =>
        recipe.Maintenance.Sum(kv => kv.Key.MidAt(location) * kv.Value);

    /// <summary>
    /// Calculate the total production cost including charge (valued at scrap-per-charge rate).
    /// </summary>
    public static float TotalCost(this ProductionRecipe recipe, Location location, float chargeValue = 1.0f) =>
        recipe.InputCost(location) + recipe.ConsumableCost(location) + recipe.MaintenanceCost(location) +
        recipe.ActivateCharge * chargeValue;

    /// <summary>
    /// Calculate suggested price per unit of primary output based on costs + margin.
    /// </summary>
    public static float SuggestedPrice(this ProductionRecipe recipe, Location location, float margin = 1.5f, float chargeValue = 1.0f) {
        var primaryOutput = recipe.Outputs.FirstOrDefault();
        if (primaryOutput.Value <= 0) return 0;
        return recipe.TotalCost(location, chargeValue) * margin / primaryOutput.Value;
    }

    /// <summary>
    /// Get the current price of the primary output at a location.
    /// </summary>
    public static float CurrentPrice(this ProductionRecipe recipe, Location location) {
        var primaryOutput = recipe.Outputs.FirstOrDefault();
        return primaryOutput.Key.MidAt(location);
    }

    /// <summary>
    /// Format recipes as a report string showing costs and suggested prices at a location.
    /// </summary>
    public static string FormatReport(this IEnumerable<ProductionRecipe> recipes, Location location, float chargeValue = 1.0f) {
        var table = new Table(
            ("Recipe", -22),
            ("Industry", -11),
            ("Inputs", 7),
            ("Consum", 7),
            ("Maint", 6),
            ("Chrg", 5),
            ("Total", 8),
            ("Output", -14),
            ("Current", 8),
            ("Suggest", 8),
            ("Delta", 7)
        );

        foreach (var recipe in recipes) {
            var primaryOutput = recipe.Outputs.FirstOrDefault();
            var outputQty = primaryOutput.Value;
            var outputName = primaryOutput.Key.ToString();

            var inputCost = recipe.InputCost(location);
            var consumCost = recipe.ConsumableCost(location);
            var maintCost = recipe.MaintenanceCost(location);
            var totalCost = recipe.TotalCost(location, chargeValue);
            var currentPrice = recipe.CurrentPrice(location);
            var suggestedPrice = recipe.SuggestedPrice(location, 1.5f, chargeValue);

            var delta = currentPrice > 0 ? (currentPrice - suggestedPrice) / suggestedPrice * 100 : 0;
            var deltaStr = delta >= 0 ? $"+{delta:F0}%" : $"{delta:F0}%";

            table.AddRow(
                recipe.Name,
                recipe.RequiredIndustry.ToString(),
                $"{inputCost:F0}",
                $"{consumCost:F1}",
                $"{maintCost:F1}",
                $"{recipe.ActivateCharge:F0}",
                $"{totalCost:F0}",
                $"{outputQty:F0}x {outputName}",
                $"{currentPrice:F0}",
                $"{suggestedPrice:F0}",
                deltaStr
            );
        }

        return table.ToString();
    }

    /// <summary>
    /// Format recipes as a detailed report showing each input, consumable, and maintenance item.
    /// </summary>
    public static string FormatDetailReport(this IEnumerable<ProductionRecipe> recipes, Location location, float chargeValue = 1.0f) {
        var result = new System.Text.StringBuilder();

        foreach (var recipe in recipes) {
            var table = new Table(
                ("Type", -8),
                ("Commodity", -14),
                ("Qty", 6),
                ("Unit", 8),
                ("Cost", 8)
            );

            float totalCost = 0;

            // Inputs
            foreach (var (commodity, amount) in recipe.Inputs) {
                var unitCost = commodity.MidAt(location);
                var lineCost = unitCost * amount;
                totalCost += lineCost;
                table.AddRow("Input", commodity.ToString(), $"{amount:F1}", $"{unitCost:F1}", $"{lineCost:F1}");
            }

            // Consumables
            foreach (var (commodity, amount) in recipe.Consumables) {
                var unitCost = commodity.MidAt(location);
                var lineCost = unitCost * amount;
                totalCost += lineCost;
                table.AddRow("Consum", commodity.ToString(), $"{amount:F2}", $"{unitCost:F1}", $"{lineCost:F1}");
            }

            // Maintenance
            foreach (var (commodity, amount) in recipe.Maintenance) {
                var unitCost = commodity.MidAt(location);
                var lineCost = unitCost * amount;
                totalCost += lineCost;
                table.AddRow("Maint", commodity.ToString(), $"{amount:F2}", $"{unitCost:F1}", $"{lineCost:F1}");
            }

            // Charge
            var chargeCost = recipe.ActivateCharge * chargeValue;
            totalCost += chargeCost;
            table.AddRow("Charge", "", $"{recipe.ActivateCharge:F0}", $"{chargeValue:F1}", $"{chargeCost:F1}");

            // Total
            table.AddRow("", "", "", "TOTAL", $"{totalCost:F1}");

            result.AppendLine($"Recipe: {recipe.Name} ({recipe.RequiredIndustry})");
            result.Append(table.ToString());
            result.AppendLine();
        }

        return result.ToString();
    }
}
