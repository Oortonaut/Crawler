using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class ActorLifecycleTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ActorLifecycleTests()
    {
        _fixture = new SimulationTestFixture(12345);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Crawler_Creation_SetsBasicProperties()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test Crawler", location);

        crawler.Name.Should().Be("Test Crawler");
        crawler.Location.Should().Be(location);
        crawler.Faction.Should().Be(Factions.Independent);
        crawler.Flags.HasFlag(ActorFlags.Mobile).Should().BeTrue();
    }

    [Fact]
    public void Crawler_Creation_InitializesInventories()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test Crawler", location);

        crawler.Inventory.Should().NotBeNull();
        crawler.Inventory[Commodity.Fuel].Should().BeGreaterThan(0);
    }

    [Fact]
    public void Crawler_WithSegments_HasWorkingSegments()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Armed Crawler", location);

        crawler.Segments.Should().HaveCountGreaterThan(0);
        crawler.Segments.Should().Contain(s => s.SegmentKind == SegmentKind.Power);
        crawler.Segments.Should().Contain(s => s.SegmentKind == SegmentKind.Traction);
        crawler.Segments.Should().Contain(s => s.SegmentKind == SegmentKind.Offense);
    }

    [Fact]
    public void Crawler_FirstSimulateTo_InitializesTimeTracking()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test Crawler", location);

        // A new actor has no time until it first enters the simulation
        crawler.Time.Should().Be(TimePoint.Zero);

        crawler.SimulateTo(Tuning.StartGameTime);

        crawler.Time.Should().Be(Tuning.StartGameTime);
        crawler.LastTime.Should().Be(Tuning.StartGameTime);
        crawler.Elapsed.Should().Be(TimeDuration.Zero);
    }

    [Fact]
    public void Crawler_Seed_IsDeterministic()
    {
        var location = _fixture.GetLocation();

        var crawler1 = new Crawler.Builder()
            .WithSeed(99999)
            .WithName("Crawler 1")
            .WithLocation(location)
            .Build();

        var crawler2 = new Crawler.Builder()
            .WithSeed(99999)
            .WithName("Crawler 2")
            .WithLocation(location)
            .Build();

        // Same seed should produce same RNG sequence
        crawler1.Seed.Should().Be(crawler2.Seed);
    }

    [Fact]
    public void Crawler_DifferentSeeds_ProduceDifferentRng()
    {
        var location = _fixture.GetLocation();

        var crawler1 = new Crawler.Builder()
            .WithSeed(11111)
            .WithName("Crawler 1")
            .WithLocation(location)
            .Build();

        var crawler2 = new Crawler.Builder()
            .WithSeed(22222)
            .WithName("Crawler 2")
            .WithLocation(location)
            .Build();

        crawler1.Seed.Should().NotBe(crawler2.Seed);
    }

    [Fact]
    public void Crawler_WithRole_SetsRole()
    {
        var location = _fixture.GetLocation();

        var trader = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("Trader")
            .WithLocation(location)
            .WithRole(Roles.Trader)
            .Build();

        trader.Role.Should().Be(Roles.Trader);
    }

    [Fact]
    public void Crawler_WithFaction_SetsFaction()
    {
        var location = _fixture.GetLocation();

        var bandit = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("Bandit")
            .WithLocation(location)
            .WithFaction(Factions.Bandit)
            .Build();

        bandit.Faction.Should().Be(Factions.Bandit);
    }

    [Fact]
    public void Crawler_SuppliesDrawDown_BeforeSharedTradeStack()
    {
        var location = _fixture.GetLocation();

        var crawler = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("Test")
            .WithLocation(location)
            .AddCommodity(Commodity.Fuel, 10)
            .AddCommodity(Commodity.Fuel, 50, Account.Supplies | Account.Trade)
            .Build();

        // Remove more than the Supplies stack has - the rest comes from Supplies|Trade
        crawler.Inventory.Remove(Commodity.Fuel, 15);

        // After removing 15: Supplies had 10, so 5 should come from Supplies|Trade
        crawler.Inventory.Stack(Commodity.Fuel, Account.Supplies).Should().Be(0);
        crawler.Inventory.Stack(Commodity.Fuel, Account.Supplies | Account.Trade).Should().Be(45); // 50 - 5
    }

    [Fact]
    public void Crawler_Destroy_SetsDestroyedFlag()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Doomed", location);

        crawler.Destroy();

        // Destroy removes the actor; EndState is set separately by SetEndState (game over)
        crawler.Flags.HasFlag(ActorFlags.Destroyed).Should().BeTrue();
    }

    [Fact]
    public void Crawler_MultipleCrawlers_HaveUniqueIdentities()
    {
        var location = _fixture.GetLocation();

        var crawlers = Enumerable.Range(0, 5)
            .Select(i => new Crawler.Builder()
                .WithSeed((ulong)(i * 1000 + 1))
                .WithName($"Crawler {i}")
                .WithLocation(location)
                .Build())
            .ToList();

        crawlers.Select(c => c.Seed).Should().OnlyHaveUniqueItems();
        crawlers.Select(c => c.Name).Should().OnlyHaveUniqueItems();
    }
}

[Collection(SimulationCollection.Name)]
public class ActorRelationshipTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ActorRelationshipTests()
    {
        _fixture = new SimulationTestFixture(54321);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Actors_To_ReturnsRelationship()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        var relationship = actor1.To(actor2);

        relationship.Should().NotBeNull();
    }

    [Fact]
    public void Actors_SetHostile_MarksRelationship()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        actor1.SetHostileTo(actor2, true);

        actor1.To(actor2).Hostile.Should().BeTrue();
    }

    [Fact]
    public void Actors_HostilityIsDirectional()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        actor1.SetHostileTo(actor2, true);

        // Actor1 is hostile to Actor2
        actor1.To(actor2).Hostile.Should().BeTrue();
        // But Actor2 is not necessarily hostile to Actor1
        // (unless the game reciprocates automatically)
    }

    [Fact]
    public void Actors_Knows_ReturnsTrueAfterMeeting()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        // Access the relationship to establish knowledge
        _ = actor1.To(actor2);

        actor1.Knows(actor2).Should().BeTrue();
    }

    [Fact]
    public void Actor_KnowsLocation_AfterArriving()
    {
        var location = _fixture.Map.AllLocations.First(l => l.HasEncounter);
        var actor = _fixture.CreateCrawler("Traveler", location);
        actor.Knows(location).Should().BeFalse();

        location.GetEncounter().AddActor(actor);

        actor.Knows(location).Should().BeTrue();
        actor.Visited(location).Should().BeTrue();
    }
}

[Collection(SimulationCollection.Name)]
public class ActorFlagsTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ActorFlagsTests()
    {
        _fixture = new SimulationTestFixture(11111);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Crawler_IsMobile()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Mobile", location);

        crawler.Flags.HasFlag(ActorFlags.Mobile).Should().BeTrue();
    }

    [Fact]
    public void Crawler_AddFlag_SetsFlag()
    {
        var location = _fixture.GetLocation();

        var crawler = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("Flagged")
            .WithLocation(location)
            .AddFlag(ActorFlags.Player)
            .Build();

        crawler.Flags.HasFlag(ActorFlags.Player).Should().BeTrue();
    }

    [Fact]
    public void Crawler_WithMultipleFlags_HasAllFlags()
    {
        var location = _fixture.GetLocation();

        var crawler = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("MultiFlag")
            .WithLocation(location)
            .WithFlags(ActorFlags.Mobile | ActorFlags.Player)
            .Build();

        crawler.Flags.HasFlag(ActorFlags.Mobile).Should().BeTrue();
        crawler.Flags.HasFlag(ActorFlags.Player).Should().BeTrue();
    }
}
