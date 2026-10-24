using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class TimeProgressionTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public TimeProgressionTests()
    {
        _fixture = new SimulationTestFixture(12345);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SimulateTo_AdvancesActorTime()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Time Traveler", location);

        var startTime = crawler.Time;
        var targetTime = startTime + TimeDuration.FromHours(1);

        crawler.SimulateTo(targetTime);

        crawler.Time.Should().BeGreaterThanOrEqualTo(targetTime);
    }

    [Fact]
    public void SimulateTo_UpdatesElapsed()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Elapsed Tracker", location);

        crawler.SimulateTo(Tuning.StartGameTime);
        var startTime = crawler.Time;
        var duration = TimeDuration.FromHours(2);
        var targetTime = startTime + duration;

        crawler.SimulateTo(targetTime);

        crawler.Elapsed.TotalSeconds.Should().BeGreaterThanOrEqualTo(duration.TotalSeconds);
    }

    [Fact]
    public void SimulateTo_PastTime_DoesNotGoBackward()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("No Time Travel", location);

        crawler.SimulateTo(Tuning.StartGameTime);
        var startTime = crawler.Time;

        // Try to simulate to a past time
        var pastTime = startTime - TimeDuration.FromHours(1);
        crawler.SimulateTo(pastTime);

        // Time should not go backward
        crawler.Time.Should().BeGreaterThanOrEqualTo(startTime);
    }

    [Fact]
    public void TimeDuration_Arithmetic_Works()
    {
        var hour = TimeDuration.FromHours(1);
        var twoHours = TimeDuration.FromHours(2);

        (hour + hour).TotalSeconds.Should().Be(twoHours.TotalSeconds);
        (twoHours - hour).TotalSeconds.Should().Be(hour.TotalSeconds);
    }

    [Fact]
    public void TimePoint_Arithmetic_Works()
    {
        var start = new TimePoint(10000);
        var duration = TimeDuration.FromHours(1);

        var end = start + duration;

        end.Elapsed.Should().BeGreaterThan(start.Elapsed);
        (end - start).TotalSeconds.Should().Be(duration.TotalSeconds);
    }

    [Fact]
    public void TimePoint_Comparison_Works()
    {
        var earlier = new TimePoint(1000);
        var later = new TimePoint(2000);

        (later > earlier).Should().BeTrue();
        (earlier < later).Should().BeTrue();
        (earlier <= later).Should().BeTrue();
        (later >= earlier).Should().BeTrue();
    }

    [Fact]
    public void MultipleActors_CanAdvanceIndependently()
    {
        var location = _fixture.GetLocation();
        var crawler1 = _fixture.CreateCrawler("Crawler 1", location);
        var crawler2 = _fixture.CreateCrawler("Crawler 2", location);

        var time1 = crawler1.Time + TimeDuration.FromHours(1);
        var time2 = crawler2.Time + TimeDuration.FromHours(2);

        crawler1.SimulateTo(time1);
        crawler2.SimulateTo(time2);

        // Each crawler advanced independently
        crawler1.Time.Should().BeGreaterThanOrEqualTo(time1);
        crawler2.Time.Should().BeGreaterThanOrEqualTo(time2);
        crawler2.Time.Should().BeGreaterThan(crawler1.Time);
    }

    [Fact]
    public void Game_CurrentTime_IsTracked()
    {
        var startTime = _fixture.Game.CurrentTime;

        startTime.Should().NotBe(TimePoint.Zero);
    }

    [Fact]
    public void Game_ProcessEventsUntil_AdvancesTime()
    {
        var startTime = _fixture.Game.CurrentTime;
        var targetTime = startTime + TimeDuration.FromHours(1);

        _fixture.Game.ProcessEventsUntil(targetTime, () => false);

        _fixture.Game.CurrentTime.Should().BeGreaterThanOrEqualTo(targetTime);
    }
}

[Collection(SimulationCollection.Name)]
public class SegmentTickTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public SegmentTickTests()
    {
        _fixture = new SimulationTestFixture(99999);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Segment_Tick_ReducesCycle()
    {
        var weaponDef = new WeaponDef('W', 3, "Test Weapon", 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.CycleStart(); // Start cycling

        var initialCycle = weapon.Cycle;

        weapon.Tick(TimeDuration.FromSeconds(5));

        weapon.Cycle.Should().BeLessThan(initialCycle);
    }

    [Fact]
    public void Shield_Tick_ChargesOverTime()
    {
        var shieldDef = new ShieldDef('S', 3, "Test Shield", 3, 3, 3, 3, 3, 3);
        var shield = shieldDef.NewSegment(12345);
        shield.Packaged = false;
        shield.Activated = true;
        shield.ShieldLeft = 0;

        shield.Tick(TimeDuration.FromHours(0.5f));

        shield.ShieldLeft.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Reactor_Generate_ProducesCharge()
    {
        var reactorDef = new ReactorDef('R', 3, "Test Reactor", 3, 3, 3, 3, 3);
        var reactor = reactorDef.NewSegment(12345);
        reactor.Packaged = false;
        reactor.Activated = true;
        reactor.Charge = 0;

        var generated = reactor.Generate(1.0f); // 1 hour

        reactor.Charge.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Crawler_WithSegments_SegmentsHaveOwner()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Segment Owner", location);

        foreach (var segment in crawler.Segments)
        {
            segment.Owner.Should().Be(crawler);
        }
    }
}
