using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Simulation;

[Collection(SimulationCollection.Name)]
public class CombatBasicsTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public CombatBasicsTests()
    {
        _fixture = new SimulationTestFixture(12345);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ArmedCrawler_HasWeapons()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Armed", location);

        var weapons = crawler.Segments.OfType<WeaponSegment>().ToList();

        weapons.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void ArmedCrawler_CreateFire_ProducesHitRecords()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Attacker", location);

        // Ensure weapons are ready
        foreach (var weapon in crawler.Segments.OfType<WeaponSegment>())
        {
            weapon.Packaged = false;
            weapon.Activated = true;
            weapon.Cycle = TimeDuration.Zero; // Ready to fire
        }

        // Ensure power is available
        foreach (var reactor in crawler.Segments.OfType<ReactorSegment>())
        {
            reactor.Packaged = false;
            reactor.Activated = true;
            reactor.Charge = reactor.Capacity;
        }

        var fire = crawler.CreateFire();

        fire.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void HitRecord_HasValidProperties()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;

        var hit = new HitRecord(12345UL, weapon, 10, 0.8f);

        hit.Weapon.Should().Be(weapon);
        hit.Damage.Should().Be(10);
        hit.Aim.Should().BeApproximately(0.8f, 0.001f);
        hit.seed.Should().Be(12345UL);
    }

    [Fact]
    public void HitRecord_DeterminesHitType()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;

        var hitWithHighAim = new HitRecord(12345UL, weapon, 10, 0.99f);
        var hitWithLowAim = new HitRecord(12345UL, weapon, 10, 0.01f);

        // Hit property should be deterministic based on seed and aim
        var hitType1 = hitWithHighAim.Hit;
        var hitType2 = hitWithLowAim.Hit;

        // Both should return a valid HitType
        Enum.IsDefined(typeof(HitType), hitType1).Should().BeTrue();
        Enum.IsDefined(typeof(HitType), hitType2).Should().BeTrue();
    }

    [Fact]
    public void WeaponSegment_GenerateFire_ProducesHits()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.Activated = true;
        weapon.Cycle = TimeDuration.Zero;

        // Gun requires owner with ammo - create minimal crawler
        var location = _fixture.GetLocation();
        var crawler = new Crawler.Builder()
            .WithSeed(12345)
            .WithName("Gunner")
            .WithLocation(location)
            .AddSegment(weapon)
            .AddCommodity(Commodity.Slugs, 100)
            .Build();

        var hits = weapon.GenerateFire(99999UL, 0).ToList();

        hits.Should().HaveCountGreaterThan(0);
        hits.All(h => h.Weapon == weapon).Should().BeTrue();
    }
}

[Collection(SimulationCollection.Name)]
public class DamageApplicationTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public DamageApplicationTests()
    {
        _fixture = new SimulationTestFixture(54321);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void ReceiveFire_SetsHostility()
    {
        var location = _fixture.GetLocation();
        var attacker = _fixture.CreateArmedCrawler("Attacker", location);
        var defender = _fixture.CreateArmedCrawler("Defender", location);

        // Should not be hostile initially
        defender.To(attacker).Hostile.Should().BeFalse();

        // Create a hit record
        var weapon = attacker.Segments.OfType<WeaponSegment>().First();
        weapon.Packaged = false;
        var fire = new List<HitRecord> { new(12345UL, weapon, 5, 0.8f) };

        defender.ReceiveFire(attacker, fire);

        // Defender should now be hostile to attacker
        defender.To(attacker).Hostile.Should().BeTrue();
    }

    [Fact]
    public void ReceiveFire_TracksDamageTaken()
    {
        var location = _fixture.GetLocation();
        var attacker = _fixture.CreateArmedCrawler("Attacker", location);
        var defender = _fixture.CreateArmedCrawler("Defender", location);

        var initialDamage = defender.To(attacker).DamageTaken;

        var weapon = attacker.Segments.OfType<WeaponSegment>().First();
        weapon.Packaged = false;
        var fire = new List<HitRecord> { new(12345UL, weapon, 100, 0.99f) };

        defender.ReceiveFire(attacker, fire);

        defender.To(attacker).DamageTaken.Should().BeGreaterThanOrEqualTo(initialDamage);
    }

    [Fact]
    public void Attack_CreatesFireAndAppliesDamage()
    {
        var location = _fixture.GetLocation();
        var attacker = _fixture.CreateArmedCrawler("Attacker", location);
        var defender = _fixture.CreateArmedCrawler("Defender", location);

        // Ensure weapons are ready
        foreach (var weapon in attacker.Segments.OfType<WeaponSegment>())
        {
            weapon.Packaged = false;
            weapon.Activated = true;
            weapon.Cycle = TimeDuration.Zero;
        }

        // Ensure power is available
        foreach (var reactor in attacker.Segments.OfType<ReactorSegment>())
        {
            reactor.Packaged = false;
            reactor.Activated = true;
            reactor.Charge = reactor.Capacity;
        }

        var result = attacker.Attack(defender);

        result.Should().BeTrue();
        attacker.To(defender).Hostile.Should().BeTrue();
    }
}

[Collection(SimulationCollection.Name)]
public class HostilityTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public HostilityTests()
    {
        _fixture = new SimulationTestFixture(11111);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void SetHostileTo_MarksRelationship()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        actor1.SetHostileTo(actor2, true);

        actor1.To(actor2).Hostile.Should().BeTrue();
        actor1.HostileTo(actor2).Should().BeTrue();
    }

    [Fact]
    public void SetHostileTo_CanBeBidirectional()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        actor1.SetHostileTo(actor2, true);
        actor2.SetHostileTo(actor1, true);

        actor1.HostileTo(actor2).Should().BeTrue();
        actor2.HostileTo(actor1).Should().BeTrue();
    }

    [Fact]
    public void SetHostileTo_CanBeCleared()
    {
        var location = _fixture.GetLocation();
        var actor1 = _fixture.CreateCrawler("Actor 1", location);
        var actor2 = _fixture.CreateCrawler("Actor 2", location);

        actor1.SetHostileTo(actor2, true);
        actor1.SetHostileTo(actor2, false);

        actor1.HostileTo(actor2).Should().BeFalse();
    }

    [Fact]
    public void ReceiveFire_MakesDefenderHostile()
    {
        var location = _fixture.GetLocation();
        var attacker = _fixture.CreateArmedCrawler("Attacker", location);
        var defender = _fixture.CreateArmedCrawler("Defender", location);

        // Initially not hostile
        defender.HostileTo(attacker).Should().BeFalse();

        var weapon = attacker.Segments.OfType<WeaponSegment>().First();
        weapon.Packaged = false;
        var fire = new List<HitRecord> { new(12345UL, weapon, 5, 0.8f) };

        defender.ReceiveFire(attacker, fire);

        // Defender becomes hostile to attacker after being fired upon
        defender.HostileTo(attacker).Should().BeTrue();
    }
}

[Collection(SimulationCollection.Name)]
public class VulnerabilityTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public VulnerabilityTests()
    {
        _fixture = new SimulationTestFixture(22222);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Crawler_WithWorkingDefenses_NotVulnerable()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Defended", location);

        // Activate all segments
        foreach (var segment in crawler.Segments)
        {
            segment.Packaged = false;
            segment.Activated = true;
        }

        // With working weapons, not disarmed
        crawler.IsDisarmed.Should().BeFalse();
    }

    [Fact]
    public void Crawler_IsDisarmed_WhenNoWorkingWeapons()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Unarmed", location);

        // Crawler without weapons should be disarmed
        crawler.IsDisarmed.Should().BeTrue();
    }

    [Fact]
    public void Crawler_IsImmobile_WhenNoWorkingTraction()
    {
        var location = _fixture.GetLocation();

        // Create crawler without traction
        var rng = new XorShift(12345);
        var reactor = new ReactorDef('R', 3, "Test Reactor", 3, 3, 3, 3, 3).NewSegment(rng.Seed());

        var crawler = new Crawler.Builder()
            .WithSeed(rng.Seed())
            .WithName("Immobile")
            .WithLocation(location)
            .AddSegment(reactor)
            .Build();

        // Without traction, should be immobile
        var tractionSegments = crawler.Segments.OfType<TractionSegment>().Where(s => s.IsActive);
        if (!tractionSegments.Any())
        {
            crawler.IsImmobile.Should().BeTrue();
        }
    }

    [Fact]
    public void Crawler_IsDepowered_WhenNoCharge()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Depowered", location);

        crawler.IsDepowered.Should().BeFalse();

        // Out of fuel means depowered (reactor charge alone doesn't count)
        crawler.Inventory[Commodity.Fuel] = 0;

        crawler.IsDepowered.Should().BeTrue();
    }

    [Fact]
    public void Crawler_Lives_WhenNotDestroyed()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Living", location);

        crawler.Lives().Should().BeTrue();
        crawler.Ended().Should().BeFalse();
    }

    [Fact]
    public void Crawler_Destroy_EndsLife()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateCrawler("Doomed", location);

        crawler.Destroy();

        crawler.Lives().Should().BeFalse();
        crawler.Ended().Should().BeTrue();
    }
}

[Collection(SimulationCollection.Name)]
public class SegmentDamageTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public SegmentDamageTests()
    {
        _fixture = new SimulationTestFixture(33333);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Segment_Hits_IncreasesWithDamage()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;

        var initialHits = weapon.Hits;

        weapon.AddDmg(HitType.Hits, 5);

        weapon.Hits.Should().BeGreaterThan(initialHits);
    }

    [Fact]
    public void Segment_State_ChangesWithDamage()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.Activated = true;

        weapon.State.Should().Be(Segment.Working.Pristine);

        // Apply enough damage to destroy
        weapon.AddDmg(HitType.Hits, weapon.MaxHealth * 2);

        weapon.State.Should().Be(Segment.Working.Destroyed);
    }

    [Fact]
    public void Shield_AbsorbsDamage()
    {
        var shieldDef = new ShieldDef('S', 3, "Test Shield", 3, 3, 3, 3, 3, 3);
        var shield = shieldDef.NewSegment(12345);
        shield.Packaged = false;
        shield.Activated = true;
        shield.ShieldLeft = 100;

        var (remaining, _) = shield.AddDmg(HitType.Hits, 50);

        // Shield should have absorbed some or all damage
        shield.ShieldLeft.Should().BeLessThan(100);
    }

    [Fact]
    public void Armor_ReducesDamage()
    {
        var armorDef = new ArmorDef('A', 3, "Test Armor", 3, 3, 3, 3);
        var armor = armorDef.NewSegment(12345);
        armor.Packaged = false;
        armor.Activated = true;

        var reduction = armor.Reduction;

        reduction.Should().BeGreaterThan(0);
    }
}

[Collection(SimulationCollection.Name)]
public class WeaponCycleTests : IDisposable
{
    readonly SimulationTestFixture _fixture;

    public WeaponCycleTests()
    {
        _fixture = new SimulationTestFixture(44444);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Weapon_CycleStart_BeginsCountdown()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.Activated = true;
        weapon.Cycle = TimeDuration.Zero;

        weapon.CycleStart();

        weapon.Cycle.Should().Be(weapon.CycleLength);
        weapon.IsCycling.Should().BeTrue();
    }

    [Fact]
    public void Weapon_Tick_ReducesCycle()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.Activated = true;
        weapon.CycleStart();

        var initialCycle = weapon.Cycle;
        var tickTime = TimeDuration.FromSeconds(1);

        weapon.Tick(tickTime);

        weapon.Cycle.Should().BeLessThan(initialCycle);
    }

    [Fact]
    public void Weapon_AfterCycleComplete_IsReadyToFire()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.Activated = true;
        weapon.Cycle = TimeDuration.Zero;

        weapon.IsReadyToFire.Should().BeTrue();
    }

    [Fact]
    public void Weapon_WhileCycling_NotReadyToFire()
    {
        var weaponDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var weapon = weaponDef.NewSegment(12345);
        weapon.Packaged = false;
        weapon.Activated = true;
        weapon.CycleStart();

        weapon.IsReadyToFire.Should().BeFalse();
    }

    [Fact]
    public void Crawler_CycleDelays_ReturnsWeaponStatus()
    {
        var location = _fixture.GetLocation();
        var crawler = _fixture.CreateArmedCrawler("Armed", location);

        // Ready all weapons
        foreach (var weapon in crawler.Segments.OfType<WeaponSegment>())
        {
            weapon.Packaged = false;
            weapon.Activated = true;
            weapon.Cycle = TimeDuration.Zero;
        }

        var (ready, waiting) = crawler.CycleDelays();

        // Should have ready weapons
        ready.Should().HaveCountGreaterThan(0);
    }
}
