using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

/// <summary>
/// A faction searches the player once, through its best-equipped customs actor in the encounter.
/// </summary>
[Collection(SimulationCollection.Name)]
public class CustomsSearchTests
{
    readonly Game _game = Game.NewSimulation(42, 2);
    readonly Encounter _encounter;
    readonly IActor _settlement;
    Factions Faction => _settlement.Faction;

    public CustomsSearchTests()
    {
        var location = _game.Map.AllLocations.First(l => l.HasEncounter && l.GetEncounter().Settlements.Any());
        _encounter = location.GetEncounter();
        _settlement = _encounter.Settlements.First();
    }

    Crawler AddOfficer(string name, ulong seed)
    {
        var rng = new XorShift(seed);
        var reactor = new ReactorDef('R', 3, "Test Reactor", 3, 3, 3, 3, 3).NewSegment(rng.Seed());
        var traction = new TractionDef('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, TerrainType.Rough).NewSegment(rng.Seed());
        var officer = new Crawler.Builder()
            .WithSeed(rng.Seed())
            .WithName(name)
            .WithFaction(Faction)
            .WithLocation(_encounter.Location)
            .WithRole(Roles.Customs)
            .WithComponentInitialization(true)
            .AddSegment(reactor)
            .AddSegment(traction)
            .AddCommodity(Commodity.Fuel, 100)
            .Build();
        _encounter.AddActor(officer);
        return officer;
    }

    Crawler AddPlayer()
    {
        var player = _encounter.GeneratePlayerActor(7);
        _encounter.AddActor(player);
        return player;
    }

    // Faction actors currently demanding a search of the player
    List<IActor> Searchers(IActor player) =>
        _encounter.ActorsExcept(player)
            .Where(a => a.Faction == Faction)
            .Where(a => a.InteractionsWith(player).Any(i => i is CustomsComponent.ContrabandAcceptInteraction))
            .ToList();

    [Fact]
    public void OnlyTheBestEquippedActorSearches()
    {
        AddOfficer("Officer A", 1);
        AddOfficer("Officer B", 2);
        var player = AddPlayer();

        player.To(Faction).Customs.IsPending.Should().BeTrue();
        var designated = CustomsComponent.DesignatedSearcher(_encounter, Faction);
        designated.Should().BeSameAs(_settlement, "the settlement outclasses any patrol crawler");
        Searchers(player).Should().ContainSingle().Which.Should().BeSameAs(designated);
    }

    [Fact]
    public void WhenTheSearcherLeaves_TheNextOfficerKeepsTheDeadline()
    {
        var officer = AddOfficer("Officer A", 1);
        var player = AddPlayer();
        var deadline = player.To(Faction).Customs.Deadline;

        _encounter.RemoveActor(_settlement);

        CustomsComponent.DesignatedSearcher(_encounter, Faction).Should().BeSameAs(officer);
        player.To(Faction).Customs.Deadline.Should().Be(deadline);
        Searchers(player).Should().ContainSingle().Which.Should().BeSameAs(officer);
    }

    [Fact]
    public void OneSearch_SatisfiesTheFaction_AndNewOfficersDontSearchAgain()
    {
        var player = AddPlayer();
        var accept = Searchers(player).Single().InteractionsWith(player)
            .OfType<CustomsComponent.ContrabandAcceptInteraction>().Single();

        accept.Perform();

        player.To(Faction).Customs.IsPending.Should().BeFalse();
        AddOfficer("Late Officer", 3);
        player.To(Faction).Customs.IsPending.Should().BeFalse("the faction searched the player recently");
        Searchers(player).Should().BeEmpty();
    }

    [Fact]
    public void MissingTheDeadline_TurnsOnlyTheSearcherHostile_AndCostsStanding()
    {
        var officer = AddOfficer("Officer A", 1);
        var player = AddPlayer();
        var standing = player.To(Faction).FactionStanding;
        var searcher = (ActorBase)CustomsComponent.DesignatedSearcher(_encounter, Faction)!;

        searcher.SimulateTo(player.To(Faction).Customs.Deadline + TimeDuration.FromMinutes(1));
        var expired = searcher.InteractionsWith(player).OfType<CustomsComponent.ContrabandExpiredInteraction>().Single();
        expired.Perform();

        searcher.To(player).Hostile.Should().BeTrue();
        officer.To(player).Hostile.Should().BeFalse("other officers of the faction don't join in on their own");
        player.To(Faction).FactionStanding.Should().Be(standing - Tuning.Crawler.CustomsDefianceStanding);
        player.To(Faction).Customs.IsPending.Should().BeFalse();
    }
}
