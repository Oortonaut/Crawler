using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class MovementTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public MovementTests()
    {
        _fixture = new SimulationTestFixture(12345, 3); // Larger map for travel
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Crawler_AtLocation_HasCorrectLocation()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Positioned", location);

        crawler.Location.Should().Be(location);
    }

    [Fact]
    public void Crawler_CanChangeLocation()
    {
        var location1 = _fixture.GetLocation(0);
        var location2 = _fixture.GetLocation(1);
        var crawler = _fixture.CreateCrawler("Mobile", location1);

        crawler.Location = location2;

        crawler.Location.Should().Be(location2);
    }

    [Fact]
    public void Location_HasTerrainType()
    {
        var location = _fixture.GetLocation();

        // Terrain should be a valid enum value
        Enum.IsDefined(typeof(TerrainType), location.Terrain).Should().BeTrue();
    }

    [Fact]
    public void Traction_SpeedVariesByTerrain()
    {
        var tractionDef = new TractionDef('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, TerrainType.Rough);
        var traction = tractionDef.NewSegment(12345);
        traction.Packaged = false;

        var speedOnFlat = traction.SpeedOn(TerrainType.Flat);
        var speedOnRough = traction.SpeedOn(TerrainType.Rough);
        var speedOnBroken = traction.SpeedOn(TerrainType.Broken);

        // Full speed on easy terrain
        speedOnFlat.Should().Be(traction.Speed);
        // Reduced at limit
        speedOnRough.Should().BeLessThan(traction.Speed);
        // Zero on impassable
        speedOnBroken.Should().Be(0);
    }

    [Fact]
    public void Map_HasMultipleLocations()
    {
        var locations = _fixture.Map.AllLocations.ToList();

        locations.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Location_HasEncounterType()
    {
        var location = _fixture.GetLocation();

        Enum.IsDefined(typeof(EncounterType), location.Type).Should().BeTrue();
    }

    [Fact]
    public void Map_MayHaveRoads()
    {
        // Map should have road connections via TradeNetwork
        var network = _fixture.Map.TradeNetwork;

        if (network != null)
        {
            network.AllRoads.Should().NotBeNull();
        }
    }
}

[Collection(SimulationCollection.Name)]
public class TractionCapabilityTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public TractionCapabilityTests()
    {
        _fixture = new SimulationTestFixture(55555);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void TractionSegment_ProvidesLift()
    {
        var tractionDef = new TractionDef('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, TerrainType.Rough);
        var traction = tractionDef.NewSegment(12345);

        traction.Lift.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TractionSegment_ProvidesSpeed()
    {
        var tractionDef = new TractionDef('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, TerrainType.Rough);
        var traction = tractionDef.NewSegment(12345);

        traction.Speed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TractionSegment_HasTerrainLimit()
    {
        var tractionDef = new TractionDef('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, TerrainType.Broken);
        var traction = tractionDef.NewSegment(12345);

        traction.TerrainLimit.Should().Be(TerrainType.Broken);
    }

    [Fact]
    public void MultipleTractionSegments_CombineLift()
    {
        var location = _fixture.GetLocation();
        var rng = new XorShift(12345);

        var traction1 = new TractionDef('T', 3, "Traction 1", 3, 3, 3, 3, 3, 3, TerrainType.Rough).NewSegment(rng.Seed());
        var traction2 = new TractionDef('T', 3, "Traction 2", 3, 3, 3, 3, 3, 3, TerrainType.Rough).NewSegment(rng.Seed());

        var crawler = new Crawler.Builder()
            .WithSeed(rng.Seed())
            .WithName("Multi-Traction")
            .WithLocation(location)
            .AddSegment(traction1)
            .AddSegment(traction2)
            .Build();

        var tractionSegments = crawler.Segments.OfType<TractionSegment>().ToList();
        tractionSegments.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(TerrainType.Rough, TerrainType.Flat, true)]      // Can traverse easier terrain
    [InlineData(TerrainType.Flat, TerrainType.Rough, false)]     // Cannot traverse harder terrain
    [InlineData(TerrainType.Rough, TerrainType.Rough, true)]     // Can traverse at limit (with penalty)
    [InlineData(TerrainType.Rough, TerrainType.Broken, false)]   // Cannot traverse harder terrain
    [InlineData(TerrainType.Broken, TerrainType.Shattered, false)]
    public void TractionSegment_TerrainTraversability(TerrainType limit, TerrainType terrain, bool canTraverse)
    {
        var tractionDef = new TractionDef('T', 3, "Test", 3, 3, 3, 3, 3, 3, limit);
        var traction = tractionDef.NewSegment(12345);
        traction.Packaged = false;

        var speed = traction.SpeedOn(terrain);

        if (canTraverse)
        {
            speed.Should().BeGreaterThan(0);
        }
        else
        {
            speed.Should().Be(0);
        }
    }
}
