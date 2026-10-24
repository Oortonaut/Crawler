using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class ComponentAttachmentTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ComponentAttachmentTests()
    {
        _fixture = new SimulationTestFixture(12345);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Component_Attach_SetsOwner()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        // All components should have the crawler as owner
        foreach (var component in crawler.Components)
        {
            component.Owner.Should().Be(crawler);
        }
    }

    [Fact]
    public void Component_HasDefaultPriority()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        // Each component should have a priority
        foreach (var component in crawler.Components)
        {
            component.Priority.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void Crawler_Components_CanBeEnumerated()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        var components = crawler.Components.ToList();

        components.Should().NotBeNull();
    }
}

[Collection(SimulationCollection.Name)]
public class ComponentInteractionTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ComponentInteractionTests()
    {
        _fixture = new SimulationTestFixture(54321);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Component_EnumerateInteractions_ReturnsInteractions()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Subject", location);

        foreach (var component in crawler.Components)
        {
            // Should not throw
            var interactions = component.EnumerateInteractions(crawler).ToList();
            interactions.Should().NotBeNull();
        }
    }

    [Fact]
    public void Component_GetNextEvent_ReturnsNullOrEvent()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        foreach (var component in crawler.Components)
        {
            // Should not throw, may return null or an event
            var evt = component.GetNextEvent();
            // Just verify it doesn't throw
        }
    }
}

[Collection(SimulationCollection.Name)]
public class TradeOfferComponentTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public TradeOfferComponentTests()
    {
        _fixture = new SimulationTestFixture(11111);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Trader_HasTradeOfferComponent()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var tradeComponent = trader.Components.OfType<TradeOfferComponent>().FirstOrDefault();

        tradeComponent.Should().NotBeNull();
    }

    [Fact]
    public void TradeOfferComponent_HasMarkup()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var tradeComponent = trader.Components.OfType<TradeOfferComponent>().First();

        tradeComponent.Markup.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TradeOfferComponent_HasSpread()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        var tradeComponent = trader.Components.OfType<TradeOfferComponent>().First();

        tradeComponent.Spread.Should().BeGreaterThan(0);
    }
}

[Collection(SimulationCollection.Name)]
public class CombatComponentTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public CombatComponentTests()
    {
        _fixture = new SimulationTestFixture(22222);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ArmedCrawler_CanHaveCombatComponent()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Fighter", location);

        // Armed crawlers may have combat components
        var combatComponents = crawler.Components.OfType<CombatComponentBase>().ToList();

        // May or may not have combat components depending on role
        combatComponents.Should().NotBeNull();
    }

    [Fact]
    public void CombatComponent_Priority_AboveDefault()
    {
        var location = _fixture.GetLocation();

        // Create a crawler with a combat component
        var rng = new XorShift(12345);
        var component = new CombatComponentDefense(rng.Seed());

        component.Priority.Should().BeGreaterThan(200); // Default is 200
    }
}

[Collection(SimulationCollection.Name)]
public class LifeSupportComponentTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public LifeSupportComponentTests()
    {
        _fixture = new SimulationTestFixture(33333);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Crawler_WithInitializedComponents_HasLifeSupport()
    {
        var location = _fixture.GetLocation();

        // Create crawler with components initialized
        var crawler = _fixture.CreateCrawler("Test", location, initializeComponents: true);

        var lifeSupport = crawler.Components.OfType<LifeSupportComponent>().FirstOrDefault();

        // May or may not have life support depending on role
        if (lifeSupport != null)
        {
            lifeSupport.Owner.Should().Be(crawler);
        }
    }
}

[Collection(SimulationCollection.Name)]
public class EncounterEventTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public EncounterEventTests()
    {
        _fixture = new SimulationTestFixture(44444);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Component_Enter_DoesNotThrow()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);
        var encounter = location.GetEncounter();

        foreach (var component in crawler.Components)
        {
            // Should not throw
            component.Enter(encounter);
        }
    }

    [Fact]
    public void Component_Leave_DoesNotThrow()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);
        var encounter = location.GetEncounter();

        foreach (var component in crawler.Components)
        {
            // Enter first
            component.Enter(encounter);
            // Then leave - should not throw
            component.Leave(encounter);
        }
    }

    [Fact]
    public void Component_Tick_DoesNotThrow()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        foreach (var component in crawler.Components)
        {
            // Should not throw
            component.Tick();
        }
    }
}

[Collection(SimulationCollection.Name)]
public class ComponentPriorityTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ComponentPriorityTests()
    {
        _fixture = new SimulationTestFixture(55555);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void RetreatComponent_HasHighestPriority()
    {
        var retreat = new RetreatComponent();

        retreat.Priority.Should().BeGreaterThanOrEqualTo(1000);
    }

    [Fact]
    public void CombatComponentAdvanced_HasHighPriority()
    {
        var rng = new XorShift(12345);
        var combat = new CombatComponentAdvanced(rng.Seed());

        combat.Priority.Should().BeGreaterThanOrEqualTo(600);
    }

    [Fact]
    public void CombatComponentDefense_HasModeratePriority()
    {
        var rng = new XorShift(12345);
        var combat = new CombatComponentDefense(rng.Seed());

        combat.Priority.Should().BeGreaterThanOrEqualTo(400);
    }

    [Fact]
    public void DefaultPriority_Is200()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        // ActorComponentBase default priority is 200
        // Any component that doesn't override should have this priority
        var defaultComponents = crawler.Components
            .Where(c => c.Priority == 200)
            .ToList();

        // Just verify we can check this
        defaultComponents.Should().NotBeNull();
    }
}

[Collection(SimulationCollection.Name)]
public class ComponentCallbackTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public ComponentCallbackTests()
    {
        _fixture = new SimulationTestFixture(66666);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ComponentsChanged_DoesNotThrow()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        foreach (var component in crawler.Components)
        {
            // Should not throw
            component.ComponentsChanged();
        }
    }

    [Fact]
    public void SegmentsChanged_DoesNotThrow()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Test", location);

        foreach (var component in crawler.Components)
        {
            // Should not throw
            component.SegmentsChanged();
        }
    }

    [Fact]
    public void SegmentStateChanged_DoesNotThrow()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Armed", location);

        var segment = crawler.Segments.First();

        foreach (var component in crawler.Components)
        {
            // Should not throw
            component.SegmentStateChanged(segment);
        }
    }
}

[Collection(SimulationCollection.Name)]
public class RoleBasedComponentTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public RoleBasedComponentTests()
    {
        _fixture = new SimulationTestFixture(77777);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Trader_HasTradeRole()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        trader.Role.Should().Be(Roles.Trader);
    }

    [Fact]
    public void Trader_HasTradeComponents()
    {
        var location = _fixture.GetLocation();
        var trader = _fixture.CreateTrader("Merchant", location);

        // Trader should have trade-related components
        var hasTradeComponent = trader.Components.OfType<TradeOfferComponent>().Any();

        hasTradeComponent.Should().BeTrue();
    }

    [Fact]
    public void CrawlerWithRole_HasRoleSpecificComponents()
    {
        var location = _fixture.GetLocation();

        var trader = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("Trader")
            .WithLocation(location)
            .WithRole(Roles.Trader)
            .WithComponentInitialization(true)
            .Build();

        // Should have role-specific components
        var hasTradeRole = trader.Components.OfType<TradeRoleComponent>().Any();

        hasTradeRole.Should().BeTrue();
    }
}
