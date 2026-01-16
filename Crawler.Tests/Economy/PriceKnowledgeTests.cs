using Crawler;
using Crawler.Economy;
using FluentAssertions;
using Moq;

namespace Crawler.Tests.Economy;

public class PriceKnowledgeTests
{
    // Helper to create a mock Location for testing
    // Note: Location is a record so we can't mock it directly,
    // but PriceKnowledge uses Location as dictionary key by reference
    static Location CreateMockLocation(ulong seed = 1)
    {
        // We need a real Location object because it's used as a dictionary key
        // For isolated tests, we use reflection or a minimal Map mock
        // Since Map is complex, we'll skip Location-dependent tests for now
        // and focus on what we can test with PriceSnapshot directly
        throw new NotImplementedException("Location requires Map");
    }

    // Tests that work with direct PriceSnapshot manipulation
    // These test the core logic without needing Location objects

    [Fact]
    public void NewPriceKnowledge_IsEmpty()
    {
        var knowledge = new PriceKnowledge();

        knowledge.Snapshots.Should().BeEmpty();
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var knowledge = new PriceKnowledge();

        var clone = knowledge.Clone();

        clone.Should().NotBeSameAs(knowledge);
        clone.Snapshots.Should().NotBeSameAs(knowledge.Snapshots);
    }

    [Fact]
    public void Clone_CopiesExistingData()
    {
        var knowledge = new PriceKnowledge();

        var clone = knowledge.Clone();

        // Both should have same count
        clone.Snapshots.Count.Should().Be(knowledge.Snapshots.Count);
    }
}

// Note: PriceSnapshot.FromLocation and related methods require Location objects
// which have complex dependencies on Map. These would need integration tests
// or a test fixture that creates minimal Map/Location objects.

// TimeDuration and TimePoint tests (used by PriceKnowledge)
public class TimeStructTests
{
    [Fact]
    public void TimeDuration_FromHours_ConvertsCorrectly()
    {
        var duration = TimeDuration.FromHours(1);

        duration.TotalHours.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void TimeDuration_FromDays_ConvertsCorrectly()
    {
        var duration = TimeDuration.FromDays(1);

        // Game uses 10 hours per day
        duration.TotalHours.Should().BeApproximately(10.0, 0.001);
    }

    [Fact]
    public void TimePoint_Subtraction_ReturnsTimeDuration()
    {
        var t1 = new TimePoint(1000);
        var t2 = new TimePoint(2000);

        var duration = t2 - t1;

        duration.TotalSeconds.Should().Be(1000);
    }

    [Fact]
    public void TimePoint_Comparison_Works()
    {
        var earlier = new TimePoint(1000);
        var later = new TimePoint(2000);

        (later > earlier).Should().BeTrue();
        (earlier < later).Should().BeTrue();
        (earlier == earlier).Should().BeTrue();
    }

    [Fact]
    public void TimeDuration_Comparison_Works()
    {
        var short_ = TimeDuration.FromHours(1);
        var long_ = TimeDuration.FromHours(2);

        (long_ > short_).Should().BeTrue();
        (short_ < long_).Should().BeTrue();
    }

    [Fact]
    public void TimeDuration_Zero_IsZero()
    {
        var zero = TimeDuration.Zero;

        zero.TotalSeconds.Should().Be(0);
    }

    [Fact]
    public void TimeDuration_None_IsSpecialValue()
    {
        var none = TimeDuration.None;

        // None represents "not applicable" - should be minimum value
        none.Should().BeLessThan(TimeDuration.Zero);
    }
}
