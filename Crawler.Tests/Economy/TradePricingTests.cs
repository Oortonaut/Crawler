using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Economy;

public class TradePricingTests
{
    [Theory]
    [InlineData(TradePolicy.Subsidized, 0.7f)]
    [InlineData(TradePolicy.Legal, 1.0f)]
    [InlineData(TradePolicy.Taxed, 1.3f)]
    [InlineData(TradePolicy.Controlled, 1.75f)]
    [InlineData(TradePolicy.Restricted, 2.75f)]
    [InlineData(TradePolicy.Prohibited, 5.0f)]
    public void PolicyMultiplier_ReturnsCorrectValue(TradePolicy policy, float expected)
    {
        var result = Tuning.Trade.PolicyMultiplier(policy);

        result.Should().Be(expected);
    }

    [Fact]
    public void PolicyMultiplier_UnknownPolicy_ReturnsLegal()
    {
        // Cast an invalid value to test default case
        var invalidPolicy = (TradePolicy)999;

        var result = Tuning.Trade.PolicyMultiplier(invalidPolicy);

        result.Should().Be(1.0f, "unknown policies should default to Legal (1.0x)");
    }

    [Fact]
    public void PolicyMultiplier_Ordering_SubsidizedIsLowest()
    {
        var subsidized = Tuning.Trade.PolicyMultiplier(TradePolicy.Subsidized);
        var legal = Tuning.Trade.PolicyMultiplier(TradePolicy.Legal);
        var taxed = Tuning.Trade.PolicyMultiplier(TradePolicy.Taxed);
        var controlled = Tuning.Trade.PolicyMultiplier(TradePolicy.Controlled);
        var restricted = Tuning.Trade.PolicyMultiplier(TradePolicy.Restricted);
        var prohibited = Tuning.Trade.PolicyMultiplier(TradePolicy.Prohibited);

        subsidized.Should().BeLessThan(legal);
        legal.Should().BeLessThan(taxed);
        taxed.Should().BeLessThan(controlled);
        controlled.Should().BeLessThan(restricted);
        restricted.Should().BeLessThan(prohibited);
    }

    [Fact]
    public void AllPolicies_HavePositiveMultipliers()
    {
        foreach (TradePolicy policy in Enum.GetValues<TradePolicy>())
        {
            var multiplier = Tuning.Trade.PolicyMultiplier(policy);
            multiplier.Should().BeGreaterThan(0, $"policy {policy} should have positive multiplier");
        }
    }
}
