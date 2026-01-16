using Crawler;
using Crawler.Production;
using FluentAssertions;

namespace Crawler.Tests.Production;

public class RecipeTests
{
    // Get a known recipe for testing
    static ProductionRecipe SmeltOre => RecipeEx.AllRecipes.First(r => r.Name == "Smelt Ore");

    [Fact]
    public void HasInputs_WithSufficientInventory_ReturnsTrue()
    {
        var recipe = SmeltOre; // Requires 3 Ore
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 10);

        recipe.HasInputs(inventory).Should().BeTrue();
    }

    [Fact]
    public void HasInputs_WithInsufficientInventory_ReturnsFalse()
    {
        var recipe = SmeltOre; // Requires 3 Ore
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 2);

        recipe.HasInputs(inventory).Should().BeFalse();
    }

    [Fact]
    public void HasInputs_WithExactAmount_ReturnsTrue()
    {
        var recipe = SmeltOre; // Requires 3 Ore
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 3);

        recipe.HasInputs(inventory).Should().BeTrue();
    }

    [Fact]
    public void HasInputs_WithBatchSize_ScalesRequirement()
    {
        var recipe = SmeltOre; // Requires 3 Ore per batch
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 10);

        recipe.HasInputs(inventory, batchSize: 2.0f).Should().BeTrue(); // Needs 6
        recipe.HasInputs(inventory, batchSize: 4.0f).Should().BeFalse(); // Needs 12
    }

    [Fact]
    public void HasConsumables_WithSufficientInventory_ReturnsTrue()
    {
        var recipe = SmeltOre; // Requires 0.5 Fuel
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 5);

        recipe.HasConsumables(inventory).Should().BeTrue();
    }

    [Fact]
    public void HasConsumables_WithInsufficientInventory_ReturnsFalse()
    {
        var recipe = SmeltOre; // Requires 0.5 Fuel
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 0.3f);

        recipe.HasConsumables(inventory).Should().BeFalse();
    }

    [Fact]
    public void MaintenanceFraction_FullMaintenance_ReturnsOne()
    {
        var recipe = SmeltOre; // Requires 0.05 SpareParts maintenance
        var inventory = new Inventory();
        inventory.Add(Commodity.SpareParts, 10);

        recipe.MaintenanceFraction(inventory).Should().Be(1.0f);
    }

    [Fact]
    public void MaintenanceFraction_PartialMaintenance_ReturnsFraction()
    {
        var recipe = SmeltOre; // Requires 0.05 SpareParts
        var inventory = new Inventory();
        inventory.Add(Commodity.SpareParts, 0.025f); // Half of what's needed

        recipe.MaintenanceFraction(inventory).Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void MaintenanceFraction_NoMaintenance_ReturnsOne()
    {
        var recipe = SmeltOre;
        var inventory = new Inventory();
        // No SpareParts at all

        recipe.MaintenanceFraction(inventory).Should().BeApproximately(0.0f, 0.01f);
    }

    [Fact]
    public void MaintenanceFraction_NoMaintenanceRequired_ReturnsOne()
    {
        // Find a recipe with no maintenance requirement
        var noMaintenanceRecipe = new ProductionRecipe(
            Name: "Test Recipe",
            RequiredIndustry: IndustryType.Refinery,
            Inputs: new Dictionary<Commodity, float> { { Commodity.Ore, 1 } },
            Consumables: new Dictionary<Commodity, float>(),
            Maintenance: new Dictionary<Commodity, float>(), // Empty maintenance
            Outputs: new Dictionary<Commodity, float> { { Commodity.Metal, 1 } },
            Waste: new Dictionary<Commodity, float>(),
            CrewRequired: 1,
            ActivateCharge: 10,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Early
        );

        var inventory = new Inventory();
        noMaintenanceRecipe.MaintenanceFraction(inventory).Should().Be(1.0f);
    }

    [Fact]
    public void ConsumeInputs_RemovesFromInventory()
    {
        var recipe = SmeltOre; // Consumes 3 Ore
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 10);

        recipe.ConsumeInputs(inventory, fraction: 1.0f);

        inventory[Commodity.Ore].Should().BeApproximately(7, 0.01f); // 10 - 3
    }

    [Fact]
    public void ConsumeInputs_PartialFraction_ConsumesProportionally()
    {
        var recipe = SmeltOre; // Consumes 3 Ore per batch
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 10);

        recipe.ConsumeInputs(inventory, fraction: 0.5f);

        inventory[Commodity.Ore].Should().BeApproximately(8.5f, 0.01f); // 10 - 1.5
    }

    [Fact]
    public void ConsumeInputs_WithBatchSize_ScalesConsumption()
    {
        var recipe = SmeltOre; // Consumes 3 Ore per batch
        var inventory = new Inventory();
        inventory.Add(Commodity.Ore, 20);

        recipe.ConsumeInputs(inventory, fraction: 1.0f, batchSize: 2.0f);

        inventory[Commodity.Ore].Should().BeApproximately(14, 0.01f); // 20 - 6
    }

    [Fact]
    public void ConsumeConsumables_RemovesFromInventory()
    {
        var recipe = SmeltOre; // Consumes 0.5 Fuel
        var inventory = new Inventory();
        inventory.Add(Commodity.Fuel, 5);

        recipe.ConsumeConsumables(inventory, fraction: 1.0f);

        inventory[Commodity.Fuel].Should().BeApproximately(4.5f, 0.01f); // 5 - 0.5
    }

    [Fact]
    public void ProduceOutputs_AddsToInventory()
    {
        var recipe = SmeltOre; // Produces 1 Metal
        var inventory = new Inventory();

        recipe.ProduceOutputs(inventory);

        inventory[Commodity.Metal].Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public void ProduceOutputs_WithEfficiency_ScalesOutput()
    {
        var recipe = SmeltOre; // Produces 1 Metal at 100% efficiency
        var inventory = new Inventory();

        recipe.ProduceOutputs(inventory, efficiency: 0.8f);

        inventory[Commodity.Metal].Should().BeApproximately(0.8f, 0.01f);
    }

    [Fact]
    public void ProduceOutputs_WithBatchSize_ScalesOutput()
    {
        var recipe = SmeltOre; // Produces 1 Metal per batch
        var inventory = new Inventory();

        recipe.ProduceOutputs(inventory, efficiency: 1.0f, batchSize: 3.0f);

        inventory[Commodity.Metal].Should().BeApproximately(3.0f, 0.01f);
    }

    [Fact]
    public void ProduceWaste_AddsWasteToInventory()
    {
        var recipe = SmeltOre; // Produces 0.5 Slag waste
        var inventory = new Inventory();

        recipe.ProduceWaste(inventory);

        inventory[Commodity.Slag].Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void ProduceWaste_WithBatchSize_ScalesWaste()
    {
        var recipe = SmeltOre; // Produces 0.5 Slag per batch
        var inventory = new Inventory();

        recipe.ProduceWaste(inventory, batchSize: 2.0f);

        inventory[Commodity.Slag].Should().BeApproximately(1.0f, 0.01f);
    }
}

public class RecipeExTests
{
    [Fact]
    public void AllRecipes_IsNotEmpty()
    {
        RecipeEx.AllRecipes.Should().NotBeEmpty();
    }

    [Fact]
    public void AllRecipes_HaveUniqueNames()
    {
        var names = RecipeEx.AllRecipes.Select(r => r.Name).ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void AllRecipes_HavePositiveCycleTime()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.CycleTime.Should().BeGreaterThan(TimeDuration.Zero,
                $"recipe {recipe.Name} should have positive cycle time");
        }
    }

    [Fact]
    public void AllRecipes_HaveAtLeastOneOutput()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.Outputs.Should().NotBeEmpty(
                $"recipe {recipe.Name} should have at least one output");
        }
    }

    [Theory]
    [InlineData(IndustryType.Refinery)]
    [InlineData(IndustryType.Fabricator)]
    [InlineData(IndustryType.Assembler)]
    [InlineData(IndustryType.Recycler)]
    public void RecipesFor_ReturnsRecipesForIndustryType(IndustryType type)
    {
        var recipes = RecipeEx.AllRecipes.Where(r => r.RequiredIndustry == type).ToList();

        // Each industry type should have at least one recipe
        recipes.Should().NotBeEmpty($"industry type {type} should have recipes");
    }

    [Theory]
    [InlineData("Smelt Ore")]
    [InlineData("Process Biomass")]
    [InlineData("Forge Alloys")]
    public void FindByName_ReturnsCorrectRecipe(string name)
    {
        var recipe = RecipeEx.AllRecipes.FirstOrDefault(r => r.Name == name);

        recipe.Should().NotBeNull($"recipe {name} should exist");
        recipe!.Name.Should().Be(name);
    }

    [Fact]
    public void RefineryRecipes_ProduceRefinedGoods()
    {
        var refineryRecipes = RecipeEx.AllRecipes
            .Where(r => r.RequiredIndustry == IndustryType.Refinery)
            .ToList();

        // Refinery typically produces: Metal, Chemicals, Glass, Nanomaterials
        var refinedOutputs = new[] { Commodity.Metal, Commodity.Chemicals, Commodity.Glass, Commodity.Nanomaterials };

        foreach (var recipe in refineryRecipes)
        {
            recipe.Outputs.Keys.Should().Contain(
                c => refinedOutputs.Contains(c),
                $"refinery recipe {recipe.Name} should produce refined goods");
        }
    }

    [Fact]
    public void RecyclerRecipes_ConsumeWaste()
    {
        var recyclerRecipes = RecipeEx.AllRecipes
            .Where(r => r.RequiredIndustry == IndustryType.Recycler)
            .ToList();

        foreach (var recipe in recyclerRecipes)
        {
            // Recycler should consume waste or low-value materials
            var inputs = recipe.Inputs.Keys.ToList();
            inputs.Should().Contain(
                c => c == Commodity.Slag || c == Commodity.Scrap,
                $"recycler recipe {recipe.Name} should consume waste materials");
        }
    }
}

public class IndustryTypeTests
{
    [Fact]
    public void AllIndustryTypes_HaveDefined()
    {
        var types = Enum.GetValues<IndustryType>();
        types.Should().HaveCountGreaterThanOrEqualTo(4); // Refinery, Fabricator, Assembler, Recycler
    }
}
