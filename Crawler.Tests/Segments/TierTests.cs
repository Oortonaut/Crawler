using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Segments;

public class TierTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var tier = new Tier(3, 1.5f);

        tier.Size.Should().Be(3);
        tier.Quality.Should().Be(1.5f);
    }

    [Fact]
    public void Constructor_SingleArg_SetsQualityToZero()
    {
        var tier = new Tier(5);

        tier.Size.Should().Be(5);
        tier.Quality.Should().Be(0);
    }

    [Fact]
    public void Addition_TierPlusTier_AddsBothComponents()
    {
        var a = new Tier(3, 1);
        var b = new Tier(2, 0.5f);

        var result = a + b;

        result.Size.Should().Be(5);
        result.Quality.Should().Be(1.5f);
    }

    [Fact]
    public void Subtraction_TierMinusTier_SubtractsBothComponents()
    {
        var a = new Tier(5, 2);
        var b = new Tier(2, 0.5f);

        var result = a - b;

        result.Size.Should().Be(3);
        result.Quality.Should().Be(1.5f);
    }

    [Fact]
    public void Addition_TierPlusFloat_AddsToQualityOnly()
    {
        var tier = new Tier(3, 1);

        var result = tier + 0.5f;

        result.Size.Should().Be(3);
        result.Quality.Should().Be(1.5f);
    }

    [Fact]
    public void Subtraction_TierMinusFloat_SubtractsFromQualityOnly()
    {
        var tier = new Tier(3, 1);

        var result = tier - 0.5f;

        result.Size.Should().Be(3);
        result.Quality.Should().Be(0.5f);
    }

    [Fact]
    public void PlusLevel_AddsToSizeOnly()
    {
        var tier = new Tier(3, 1);

        var result = Tier.PlusLevel(tier, 2);

        result.Size.Should().Be(5);
        result.Quality.Should().Be(1);
    }

    [Fact]
    public void MinusLevel_SubtractsFromSizeOnly()
    {
        var tier = new Tier(5, 1);

        var result = Tier.MinusLevel(tier, 2);

        result.Size.Should().Be(3);
        result.Quality.Should().Be(1);
    }

    [Fact]
    public void NA_IsSpecialValue()
    {
        var na = Tier.NA;

        // NA has very negative size value
        na.Size.Should().BeLessThan(-100);
        na.Quality.Should().Be(0);
    }

    [Fact]
    public void ImplicitConversion_FromFloat_CreatesTierWithZeroQuality()
    {
        Tier tier = 5f;

        tier.Size.Should().Be(5);
        tier.Quality.Should().Be(0);
    }

    [Fact]
    public void ImplicitConversion_FromTuple_CreatesTier()
    {
        Tier tier = (3f, 1.5f);

        tier.Size.Should().Be(3);
        tier.Quality.Should().Be(1.5f);
    }

    [Fact]
    public void ToString_FormatsCorrectly()
    {
        var tier = new Tier(3, 1);

        tier.ToString().Should().Contain("3").And.Contain("1");
    }
}
