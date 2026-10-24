using Crawler;
using Crawler.Production;

namespace Crawler.Tests.Simulation;

/// <summary>
/// Shared test fixture for simulation tests.
/// Creates a minimal game world with map and locations.
/// </summary>
public class SimulationTestFixture : IDisposable
{
    public Game Game { get; }
    public Map Map => Game.Map;
    public ulong Seed { get; }

    public SimulationTestFixture(ulong seed = 12345, int mapSize = 2)
    {
        Seed = seed;
        Game = Game.NewSimulation(seed, mapSize);
    }

    /// <summary>Get a location from the map for testing.</summary>
    public Location GetLocation(int index = 0) =>
        Map.AllLocations.Skip(index).First();

    /// <summary>Get a settlement location if available.</summary>
    public Location? GetSettlement() =>
        Map.AllLocations.FirstOrDefault(l => l.Type == EncounterType.Settlement);

    /// <summary>Create a basic crawler at a location.</summary>
    public Crawler CreateCrawler(
        string name,
        Location location,
        Roles role = Roles.None,
        Factions faction = Factions.Independent,
        bool initializeComponents = false)
    {
        var rng = new XorShift(Seed);
        return new Crawler.Builder()
            .WithSeed(rng.Seed())
            .WithName(name)
            .WithFaction(faction)
            .WithLocation(location)
            .WithRole(role)
            .WithComponentInitialization(initializeComponents)
            .AddSupplies(Commodity.Fuel, 100)
            .AddSupplies(Commodity.Rations, 50)
            .AddSupplies(Commodity.Water, 50)
            .Build();
    }

    /// <summary>Create a crawler with basic segments (power + traction).</summary>
    public Crawler CreateArmedCrawler(
        string name,
        Location location,
        Factions faction = Factions.Independent)
    {
        var rng = new XorShift(Seed);

        // Create basic segments
        var reactor = new ReactorDef('R', 3, "Test Reactor", 3, 3, 3, 3, 3).NewSegment(rng.Seed());
        var traction = new TractionDef('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, TerrainType.Rough).NewSegment(rng.Seed());
        var weapon = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3).NewSegment(rng.Seed());

        return new Crawler.Builder()
            .WithSeed(rng.Seed())
            .WithName(name)
            .WithFaction(faction)
            .WithLocation(location)
            .WithRole(Roles.None)
            .WithComponentInitialization(false)
            .AddSegment(reactor)
            .AddSegment(traction)
            .AddSegment(weapon)
            .AddSupplies(Commodity.Fuel, 100)
            .AddSupplies(Commodity.Rations, 50)
            .AddSupplies(Commodity.Water, 50)
            .AddSupplies(Commodity.Slugs, 100) // Ammo for gun
            .Build();
    }

    /// <summary>Create a trader crawler with cargo.</summary>
    public Crawler CreateTrader(
        string name,
        Location location,
        Factions faction = Factions.Independent)
    {
        var rng = new XorShift(Seed);

        var reactor = new ReactorDef('R', 3, "Trader Reactor", 3, 3, 3, 3, 3).NewSegment(rng.Seed());
        var traction = new TractionDef('T', 3, "Trader Traction", 3, 3, 3, 3, 3, 3, TerrainType.Rough).NewSegment(rng.Seed());

        return new Crawler.Builder()
            .WithSeed(rng.Seed())
            .WithName(name)
            .WithFaction(faction)
            .WithLocation(location)
            .WithRole(Roles.Trader)
            .WithComponentInitialization(true)
            .AddSegment(reactor)
            .AddSegment(traction)
            .AddSupplies(Commodity.Fuel, 200)
            .AddSupplies(Commodity.Rations, 100)
            .AddSupplies(Commodity.Water, 100)
            .AddCargo(Commodity.Metal, 50)
            .AddCargo(Commodity.Alloys, 25)
            .Build();
    }

    public void Dispose()
    {
        // Game cleanup if needed
    }
}

/// <summary>
/// Simulation tests share the Game.Instance singleton, so they run one at a time.
/// Every test class that creates a Game belongs in this collection.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class SimulationCollection
{
    public const string Name = "Simulation";
}
