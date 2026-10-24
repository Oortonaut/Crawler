using Crawler;
using Crawler.Production;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class ProductionRecipeTests
{
    [Fact]
    public void AllRecipes_HaveValidNames()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.Name.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void AllRecipes_HaveValidIndustryType()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            Enum.IsDefined(typeof(IndustryType), recipe.RequiredIndustry).Should().BeTrue();
        }
    }

    [Fact]
    public void AllRecipes_HavePositiveCycleTime()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.CycleTime.TotalSeconds.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void AllRecipes_HaveOutputs()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.Outputs.Should().NotBeEmpty($"Recipe {recipe.Name} should have outputs");
        }
    }

    [Fact]
    public void AllRecipes_HaveValidTechTier()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            Enum.IsDefined(typeof(GameTier), recipe.TechTier).Should().BeTrue();
        }
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeInputTests
{
    static ProductionRecipe GetSmeltOreRecipe() =>
        RecipeEx.AllRecipes.First(r => r.Name == "Smelt Ore");

    [Fact]
    public void HasInputs_TrueWhenSufficient()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Add more than enough ore
        inventory.Add(Commodity.Ore, 10);

        recipe.HasInputs(inventory).Should().BeTrue();
    }

    [Fact]
    public void HasInputs_FalseWhenInsufficient()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Add less than required ore
        inventory.Add(Commodity.Ore, 1);

        recipe.HasInputs(inventory).Should().BeFalse();
    }

    [Fact]
    public void HasInputs_RespectsBatchSize()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Add enough for 1 batch but not 2
        inventory.Add(Commodity.Ore, 5);

        recipe.HasInputs(inventory, 1.0f).Should().BeTrue();
        recipe.HasInputs(inventory, 2.0f).Should().BeFalse();
    }

    [Fact]
    public void HasConsumables_TrueWhenSufficient()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Add more than enough fuel
        inventory.Add(Commodity.Fuel, 10);

        recipe.HasConsumables(inventory).Should().BeTrue();
    }

    [Fact]
    public void HasConsumables_FalseWhenInsufficient()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Add no fuel
        recipe.HasConsumables(inventory).Should().BeFalse();
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeConsumptionTests
{
    static ProductionRecipe GetSmeltOreRecipe() =>
        RecipeEx.AllRecipes.First(r => r.Name == "Smelt Ore");

    [Fact]
    public void ConsumeInputs_RemovesFromInventory()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        inventory.Add(Commodity.Ore, 10);
        var initial = inventory[Commodity.Ore];

        recipe.ConsumeInputs(inventory, 1.0f);

        inventory[Commodity.Ore].Should().BeLessThan(initial);
    }

    [Fact]
    public void ConsumeInputs_PartialFraction()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        inventory.Add(Commodity.Ore, 10);

        // Consume 50%
        recipe.ConsumeInputs(inventory, 0.5f);

        // Should have consumed half the recipe amount
        var oreRequired = recipe.Inputs[Commodity.Ore];
        inventory[Commodity.Ore].Should().BeApproximately(10 - (oreRequired * 0.5f), 0.01f);
    }

    [Fact]
    public void ConsumeConsumables_RemovesFromInventory()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        inventory.Add(Commodity.Fuel, 10);
        var initial = inventory[Commodity.Fuel];

        recipe.ConsumeConsumables(inventory, 1.0f);

        inventory[Commodity.Fuel].Should().BeLessThan(initial);
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeProductionTests
{
    static ProductionRecipe GetSmeltOreRecipe() =>
        RecipeEx.AllRecipes.First(r => r.Name == "Smelt Ore");

    [Fact]
    public void ProduceOutputs_AddsToInventory()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        var initial = inventory[Commodity.Metal];

        recipe.ProduceOutputs(inventory);

        inventory[Commodity.Metal].Should().BeGreaterThan(initial);
    }

    [Fact]
    public void ProduceOutputs_RespectsEfficiency()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Produce at 50% efficiency
        recipe.ProduceOutputs(inventory, 0.5f);

        var expected = recipe.Outputs[Commodity.Metal] * 0.5f;
        inventory[Commodity.Metal].Should().BeApproximately(expected, 0.01f);
    }

    [Fact]
    public void ProduceOutputs_RespectsBatchSize()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Produce double batch
        recipe.ProduceOutputs(inventory, 1.0f, 2.0f);

        var expected = recipe.Outputs[Commodity.Metal] * 2.0f;
        inventory[Commodity.Metal].Should().BeApproximately(expected, 0.01f);
    }

    [Fact]
    public void ProduceWaste_AddsToInventory()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        var initial = inventory[Commodity.Slag];

        recipe.ProduceWaste(inventory);

        // Recipe produces slag as waste
        if (recipe.Waste.ContainsKey(Commodity.Slag))
        {
            inventory[Commodity.Slag].Should().BeGreaterThan(initial);
        }
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeMaintenanceTests
{
    static ProductionRecipe GetSmeltOreRecipe() =>
        RecipeEx.AllRecipes.First(r => r.Name == "Smelt Ore");

    [Fact]
    public void MaintenanceFraction_ReturnsOneWhenFull()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Add more than enough maintenance materials
        foreach (var (commodity, _) in recipe.Maintenance)
        {
            inventory.Add(commodity, 100);
        }

        var fraction = recipe.MaintenanceFraction(inventory);

        fraction.Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public void MaintenanceFraction_ReturnsZeroWhenEmpty()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        // Don't add any maintenance materials
        if (recipe.Maintenance.Count > 0)
        {
            var fraction = recipe.MaintenanceFraction(inventory);
            fraction.Should().Be(0);
        }
    }

    [Fact]
    public void MaintenanceFraction_ReturnsPartialWhenPartial()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        if (recipe.Maintenance.Count > 0)
        {
            // Add half of the required maintenance
            foreach (var (commodity, amount) in recipe.Maintenance)
            {
                inventory.Add(commodity, amount / 2);
            }

            var fraction = recipe.MaintenanceFraction(inventory);

            fraction.Should().BeGreaterThan(0);
            fraction.Should().BeLessThanOrEqualTo(1.0f);
        }
    }

    [Fact]
    public void ConsumeMaintenance_ConsumesMaterials()
    {
        var recipe = GetSmeltOreRecipe();
        var inventory = new Inventory();

        if (recipe.Maintenance.Count > 0)
        {
            // Add maintenance materials
            foreach (var (commodity, _) in recipe.Maintenance)
            {
                inventory.Add(commodity, 10);
            }

            var initialAmounts = recipe.Maintenance.Keys
                .ToDictionary(c => c, c => inventory[c]);

            recipe.ConsumeMaintenance(inventory, 1.0f);

            // At least one material should be consumed
            var anyConsumed = recipe.Maintenance.Keys
                .Any(c => inventory[c] < initialAmounts[c]);
            anyConsumed.Should().BeTrue();
        }
    }
}

[Collection(SimulationCollection.Name)]
public class IndustryTypeTests
{
    [Theory]
    [InlineData(IndustryType.Refinery)]
    [InlineData(IndustryType.Fabricator)]
    [InlineData(IndustryType.Assembler)]
    [InlineData(IndustryType.Recycler)]
    public void IndustryType_AllValuesAreDefined(IndustryType type)
    {
        Enum.IsDefined(typeof(IndustryType), type).Should().BeTrue();
    }

    [Fact]
    public void Recipes_ExistForAllIndustryTypes()
    {
        var industryTypes = Enum.GetValues<IndustryType>();

        foreach (var type in industryTypes)
        {
            var recipes = RecipeEx.AllRecipes
                .Where(r => r.RequiredIndustry == type)
                .ToList();

            recipes.Should().HaveCountGreaterThan(0, $"Industry type {type} should have at least one recipe");
        }
    }
}

[Collection(SimulationCollection.Name)]
public class GameTierTests
{
    [Theory]
    [InlineData(GameTier.Early)]
    [InlineData(GameTier.Mid)]
    [InlineData(GameTier.Late)]
    public void GameTier_AllValuesAreDefined(GameTier tier)
    {
        Enum.IsDefined(typeof(GameTier), tier).Should().BeTrue();
    }

    [Fact]
    public void Recipes_ExistForAllTiers()
    {
        // None is the "no tier" placeholder, not a tech level
        var tiers = Enum.GetValues<GameTier>().Where(t => t != GameTier.None);

        foreach (var tier in tiers)
        {
            var recipes = RecipeEx.AllRecipes
                .Where(r => r.TechTier == tier)
                .ToList();

            recipes.Should().HaveCountGreaterThan(0, $"Game tier {tier} should have at least one recipe");
        }
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeCrewTests
{
    [Fact]
    public void AllRecipes_HavePositiveCrewRequirement()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.CrewRequired.Should().BeGreaterThan(0, $"Recipe {recipe.Name} should require crew");
        }
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeActivateChargeTests
{
    [Fact]
    public void AllRecipes_HavePositiveActivateCharge()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.ActivateCharge.Should().BeGreaterThan(0, $"Recipe {recipe.Name} should require activation charge");
        }
    }
}

[Collection(SimulationCollection.Name)]
public class RecipeWearTests
{
    [Fact]
    public void AllRecipes_HavePositiveWear()
    {
        foreach (var recipe in RecipeEx.AllRecipes)
        {
            recipe.Wear.Should().BeGreaterThan(0, $"Recipe {recipe.Name} should have wear factor");
        }
    }

    [Fact]
    public void DefaultWear_IsOne()
    {
        // Create a recipe with default wear
        var recipe = new ProductionRecipe(
            Name: "Test",
            RequiredIndustry: IndustryType.Refinery,
            Inputs: new Dictionary<Commodity, float>(),
            Consumables: new Dictionary<Commodity, float>(),
            Maintenance: new Dictionary<Commodity, float>(),
            Outputs: new Dictionary<Commodity, float> { { Commodity.Metal, 1 } },
            Waste: new Dictionary<Commodity, float>(),
            CrewRequired: 1,
            ActivateCharge: 10,
            CycleTime: TimeDuration.FromHours(1),
            TechTier: GameTier.Early
            // Wear defaults to 1.0f
        );

        recipe.Wear.Should().Be(1.0f);
    }
}
