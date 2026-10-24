using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class PlayerWeaponTests
{
    [Fact]
    public void NewPlayer_WeaponsAreOwnedArmedAndFire()
    {
        var game = Game.NewSimulation(42, 2);
        var location = game.Map.AllLocations.First(l => l.HasEncounter);
        var player = location.GetEncounter().GeneratePlayerActor(7);

        var weapons = player.OffenseSegments.OfType<WeaponSegment>().ToList();
        weapons.Should().NotBeEmpty();
        weapons.Should().OnlyContain(w => ReferenceEquals(w.Owner, player), "segments must know their crawler");
        weapons.Should().OnlyContain(w => w.HasAmmo, "the player starts with ammunition");

        player.CreateFire().Should().NotBeEmpty();
    }
}
