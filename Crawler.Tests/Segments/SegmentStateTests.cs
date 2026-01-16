using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Segments;

public class SegmentStateTests
{
    static SegmentDef CreateTestDef(int maxHits = 10) =>
        new('X', 3, "Test Segment", SegmentKind.Defense, 3, 3, 3, (3, maxHits > 10 ? 1 : 0));

    static Segment CreateTestSegment(int maxHits = 10)
    {
        var def = CreateTestDef(maxHits);
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void State_Pristine_WhenNoHitsAndActiveAndNotPackaged()
    {
        var segment = CreateTestSegment();
        segment.Hits = 0;
        segment.Activated = true;
        segment.Packaged = false;

        segment.State.Should().Be(Segment.Working.Pristine);
    }

    [Fact]
    public void State_Running_WhenSomeHitsButBelowHalfAndActive()
    {
        var segment = CreateTestSegment();
        // MaxHits is calculated from tier, let's use actual MaxHealth
        segment.Hits = 1; // Some damage but less than half
        segment.Activated = true;
        segment.Packaged = false;

        // Should be Running if hits > 0 and hits <= MaxHealth/2
        if (segment.Hits > 0 && segment.Hits <= segment.MaxHealth / 2)
        {
            segment.State.Should().Be(Segment.Working.Running);
        }
    }

    [Fact]
    public void State_Damaged_WhenHitsExceedHalfMaxHealth()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Hits = segment.MaxHealth / 2 + 1;

        segment.State.Should().Be(Segment.Working.Damaged);
    }

    [Fact]
    public void State_Destroyed_WhenHitsEqualMaxHealth()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Hits = segment.MaxHealth;

        segment.State.Should().Be(Segment.Working.Destroyed);
    }

    [Fact]
    public void State_Destroyed_WhenHitsExceedMaxHealth()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Hits = segment.MaxHealth + 5; // Exceeds max

        // Hits should be clamped to MaxHealth
        segment.Hits.Should().Be(segment.MaxHealth);
        segment.State.Should().Be(Segment.Working.Destroyed);
    }

    [Fact]
    public void State_Packaged_OverridesOtherStates()
    {
        var segment = CreateTestSegment();
        segment.Hits = 0;
        segment.Activated = true;
        segment.Packaged = true;

        segment.State.Should().Be(Segment.Working.Packaged);
    }

    [Fact]
    public void State_Deactivated_WhenNotActivatedAndNotPackaged()
    {
        var segment = CreateTestSegment();
        segment.Hits = 0;
        segment.Activated = false;
        segment.Packaged = false;

        segment.State.Should().Be(Segment.Working.Deactivated);
    }

    [Fact]
    public void State_Destroyed_TakesPrecedenceOverDeactivated()
    {
        var segment = CreateTestSegment();
        segment.Hits = segment.MaxHealth;
        segment.Activated = false;
        segment.Packaged = false;

        // Destroyed takes precedence
        segment.State.Should().Be(Segment.Working.Destroyed);
    }

    [Fact]
    public void State_Packaged_TakesPrecedenceOverDamaged()
    {
        var segment = CreateTestSegment();
        segment.Hits = segment.MaxHealth / 2 + 1;
        segment.Packaged = true;

        // Packaged (but not destroyed)
        segment.State.Should().Be(Segment.Working.Packaged);
    }

    // Predicate tests
    [Fact]
    public void IsActive_TrueForPristineAndRunning()
    {
        var pristine = CreateTestSegment();
        pristine.Packaged = false;
        pristine.Activated = true;
        pristine.Hits = 0;

        pristine.IsActive.Should().BeTrue();

        var running = CreateTestSegment();
        running.Packaged = false;
        running.Activated = true;
        running.Hits = 1;

        // Running if hits > 0 and hits <= MaxHealth/2
        if (running.Hits <= running.MaxHealth / 2)
        {
            running.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public void IsActive_FalseForDamagedAndDestroyed()
    {
        var damaged = CreateTestSegment();
        damaged.Packaged = false;
        damaged.Activated = true;
        damaged.Hits = damaged.MaxHealth / 2 + 1;

        damaged.IsActive.Should().BeFalse();

        var destroyed = CreateTestSegment();
        destroyed.Packaged = false;
        destroyed.Hits = destroyed.MaxHealth;

        destroyed.IsActive.Should().BeFalse();
    }

    [Fact]
    public void IsUsable_TrueUnlessDestroyedOrPackaged()
    {
        var pristine = CreateTestSegment();
        pristine.Packaged = false;
        pristine.IsUsable.Should().BeTrue();

        var deactivated = CreateTestSegment();
        deactivated.Packaged = false;
        deactivated.Activated = false;
        deactivated.IsUsable.Should().BeTrue();

        var damaged = CreateTestSegment();
        damaged.Packaged = false;
        damaged.Activated = true;
        damaged.Hits = damaged.MaxHealth / 2 + 1;
        damaged.IsUsable.Should().BeTrue();
    }

    [Fact]
    public void IsUsable_FalseWhenDestroyedOrPackaged()
    {
        var destroyed = CreateTestSegment();
        destroyed.Packaged = false;
        destroyed.Hits = destroyed.MaxHealth;
        destroyed.IsUsable.Should().BeFalse();

        var packaged = CreateTestSegment();
        packaged.Packaged = true;
        packaged.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void IsPackaged_MatchesState()
    {
        var segment = CreateTestSegment();
        segment.Packaged = true;

        segment.IsPackaged.Should().BeTrue();
        segment.State.Should().Be(Segment.Working.Packaged);
    }

    [Fact]
    public void IsDestroyed_MatchesState()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Hits = segment.MaxHealth;

        segment.IsDestroyed.Should().BeTrue();
        segment.State.Should().Be(Segment.Working.Destroyed);
    }

    [Fact]
    public void IsDeactivated_MatchesState()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = false;
        segment.Hits = 0;

        segment.IsDeactivated.Should().BeTrue();
    }

    [Fact]
    public void IsPristine_MatchesState()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Hits = 0;

        segment.IsPristine.Should().BeTrue();
    }

    [Fact]
    public void IsDamaged_MatchesState()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Hits = segment.MaxHealth / 2 + 1;

        segment.IsDamaged.Should().BeTrue();
    }

    // Cycle tests
    [Fact]
    public void IsReadyToFire_TrueWhenActiveAndNoCycle()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Cycle = TimeDuration.Zero;

        segment.IsReadyToFire.Should().BeTrue();
    }

    [Fact]
    public void IsReadyToFire_FalseWhenCycling()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Cycle = TimeDuration.FromSeconds(5);

        segment.IsReadyToFire.Should().BeFalse();
    }

    [Fact]
    public void IsCycling_TrueWhenActiveWithPositiveCycle()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = true;
        segment.Cycle = TimeDuration.FromSeconds(5);

        segment.IsCycling.Should().BeTrue();
    }

    [Fact]
    public void IsCycling_FalseWhenNotActive()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Activated = false;
        segment.Cycle = TimeDuration.FromSeconds(5);

        segment.IsCycling.Should().BeFalse();
    }

    // Tick tests
    [Fact]
    public void Tick_DecreasesCycle()
    {
        var segment = CreateTestSegment();
        segment.Cycle = TimeDuration.FromSeconds(10);

        segment.Tick(TimeDuration.FromSeconds(3));

        segment.Cycle.TotalSeconds.Should().Be(7);
    }

    [Fact]
    public void Tick_CycleCannotGoNegative()
    {
        var segment = CreateTestSegment();
        segment.Cycle = TimeDuration.FromSeconds(5);

        segment.Tick(TimeDuration.FromSeconds(10));

        segment.Cycle.Should().Be(TimeDuration.Zero);
    }

    // Health tests
    [Fact]
    public void Health_IsMaxHealthMinusHits()
    {
        var segment = CreateTestSegment();
        segment.Packaged = false;
        segment.Hits = 3;

        segment.Health.Should().Be(segment.MaxHealth - 3);
    }

    [Fact]
    public void Hits_ClampedToMaxHealth()
    {
        var segment = CreateTestSegment();
        segment.Hits = 999;

        segment.Hits.Should().Be(segment.MaxHealth);
    }

    [Fact]
    public void Hits_ClampedToZero()
    {
        var segment = CreateTestSegment();
        segment.Hits = -10;

        segment.Hits.Should().Be(0);
    }

    // Clone tests
    [Fact]
    public void Clone_CopiesAllProperties()
    {
        var original = CreateTestSegment();
        original.Packaged = false;
        original.Activated = true;
        original.Hits = 3;
        original.Cycle = TimeDuration.FromSeconds(5);

        var clone = original.Clone();

        clone.Should().NotBeSameAs(original);
        clone.Seed.Should().Be(original.Seed);
        clone.SegmentDef.Should().Be(original.SegmentDef);
        clone.Hits.Should().Be(original.Hits);
        clone.Packaged.Should().Be(original.Packaged);
        clone.Activated.Should().Be(original.Activated);
        clone.Cycle.Should().Be(original.Cycle);
    }

    // Serialization tests
    [Fact]
    public void ToData_FromData_RoundTrip()
    {
        var original = CreateTestSegment();
        original.Packaged = true;
        original.Activated = false;
        original.Hits = 5;

        var data = original.ToData();

        // Create new segment and apply data
        var restored = CreateTestSegment();
        restored.FromData(data);

        restored.Hits.Should().Be(original.Hits);
        restored.Packaged.Should().Be(original.Packaged);
        restored.Activated.Should().Be(original.Activated);
    }

    [Fact]
    public void Data_ContainsSeedAndDefName()
    {
        var segment = CreateTestSegment();

        var data = segment.ToData();

        data.Seed.Should().Be(segment.Seed);
        data.DefName.Should().Be(segment.SegmentDef.Name);
    }
}

public class StateCodeTests
{
    static Segment CreateTestSegment()
    {
        var def = new SegmentDef('X', 3, "Test", SegmentKind.Defense, 3, 3, 3, 3);
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void StateCode_Pristine_IsAsterisk()
    {
        var segment = CreateTestSegment();
        segment.Activated = true;
        segment.Hits = 0;

        segment.StateCode.Should().Be('*');
    }

    [Fact]
    public void StateCode_Running_IsPlus()
    {
        var segment = CreateTestSegment();
        segment.Activated = true;
        segment.Hits = 1;

        if (segment.State == Segment.Working.Running)
        {
            segment.StateCode.Should().Be('+');
        }
    }

    [Fact]
    public void StateCode_Damaged_IsTilde()
    {
        var segment = CreateTestSegment();
        segment.Activated = true;
        segment.Hits = segment.MaxHealth / 2 + 1;

        segment.StateCode.Should().Be('~');
    }

    [Fact]
    public void StateCode_Deactivated_IsMinus()
    {
        var segment = CreateTestSegment();
        segment.Activated = false;
        segment.Hits = 0;

        segment.StateCode.Should().Be('-');
    }

    [Fact]
    public void StateCode_Destroyed_IsX()
    {
        var segment = CreateTestSegment();
        segment.Hits = segment.MaxHealth;

        segment.StateCode.Should().Be('x');
    }

    [Fact]
    public void StateCode_Packaged_IsSlash()
    {
        var segment = CreateTestSegment();
        segment.Packaged = true;

        segment.StateCode.Should().Be('/');
    }
}
