using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class LocalMarkupTests
{
    // GaussianSampler is a struct; passed by value it never advanced, so every commodity got the same markup
    [Fact]
    public void LocalMarkup_VariesAcrossCommodities()
    {
        var game = Game.NewSimulation(42, 2);

        foreach (var location in game.Map.AllLocations.Take(5)) {
            var markups = Enum.GetValues<Commodity>().Select(c => location.LocalMarkup[c]).ToList();
            markups.Distinct().Should().HaveCountGreaterThan(markups.Count / 2,
                "each commodity draws its own local markup");
        }
    }

    [Fact]
    public void TraderMarkupAndSpread_AreIndependentDraws()
    {
        var gaussian = new GaussianSampler(7);

        var first = Tuning.Trade.TradeMarkup(ref gaussian);
        var second = Tuning.Trade.TradeMarkup(ref gaussian);

        second.Should().NotBe(first);
    }
}
