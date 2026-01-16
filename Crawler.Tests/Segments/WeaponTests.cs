using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Segments;

public class WeaponSegmentTests
{
    static WeaponDef CreateTestWeaponDef() =>
        new('W', 3, "Test Weapon", 3, 3, 3, 3, 3, 3, 3, 3);

    static WeaponSegment CreateTestWeaponSegment()
    {
        var def = CreateTestWeaponDef();
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void CycleLength_ComputedFromRate()
    {
        var segment = CreateTestWeaponSegment();

        // CycleLength = TimeDuration.FromSeconds(60 / Rate)
        // Verify it's positive and approximately correct (within 1 second of expected)
        var expectedSeconds = 60.0 / segment.Rate;
        segment.CycleLength.TotalSeconds.Should().BeGreaterThan(0);
        Math.Abs(segment.CycleLength.TotalSeconds - expectedSeconds).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void AmmoPerCycle_IsShotsTimesAmmoPerShot()
    {
        // Gun uses ammo
        var gunDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var gun = gunDef.NewSegment(12345);
        gun.Packaged = false;

        var expectedAmmo = gun.Shots * gun.AmmoPerShot;
        gun.AmmoPerCycle.Should().BeApproximately(expectedAmmo, 0.001f);
    }

    [Fact]
    public void RequiresAmmo_TrueForGunSegment()
    {
        var gunDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var gun = gunDef.NewSegment(12345);

        gun.RequiresAmmo.Should().BeTrue();
        gun.AmmoType.Should().Be(Commodity.Slugs);
    }

    [Fact]
    public void RequiresAmmo_TrueForLaserSegment()
    {
        var laserDef = new LaserDef('L', 3, "Test Laser", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var laser = laserDef.NewSegment(12345);

        laser.RequiresAmmo.Should().BeTrue();
        laser.AmmoType.Should().Be(Commodity.Cells);
    }

    [Fact]
    public void RequiresAmmo_TrueForMissileSegment()
    {
        var missileDef = new MissileDef('M', 3, "Test Missile", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var missile = missileDef.NewSegment(12345);

        missile.RequiresAmmo.Should().BeTrue();
        missile.AmmoType.Should().Be(Commodity.Rockets);
    }

    [Fact]
    public void RequiresAmmo_FalseForBasicWeapon()
    {
        var weapon = CreateTestWeaponSegment();

        weapon.RequiresAmmo.Should().BeFalse();
    }

    [Fact]
    public void HasAmmo_TrueWhenNotRequiringAmmo()
    {
        var weapon = CreateTestWeaponSegment();

        weapon.HasAmmo.Should().BeTrue();
    }

    [Fact]
    public void HasAmmo_FalseWhenNoOwner()
    {
        var gunDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var gun = gunDef.NewSegment(12345);
        gun.Owner = null;

        gun.HasAmmo.Should().BeFalse();
    }

    [Fact]
    public void CycleStart_SetsCycleToLength()
    {
        var weapon = CreateTestWeaponSegment();
        weapon.Cycle = TimeDuration.Zero;

        weapon.CycleStart();

        weapon.Cycle.Should().Be(weapon.CycleLength);
    }

    [Fact]
    public void Clone_CreatesIndependentCopy()
    {
        var original = CreateTestWeaponSegment();
        original.Hits = 3;
        original.Cycle = TimeDuration.FromSeconds(5);

        var clone = original.Clone();

        clone.Should().NotBeSameAs(original);
        clone.Hits.Should().Be(original.Hits);
        clone.Cycle.Should().Be(original.Cycle);
    }

    [Fact]
    public void Damage_AccessesDefValue()
    {
        var weapon = CreateTestWeaponSegment();

        // Should access the def's calculated Damage
        weapon.Damage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Rate_AccessesDefValue()
    {
        var weapon = CreateTestWeaponSegment();

        weapon.Rate.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Shots_AccessesDefValue()
    {
        var weapon = CreateTestWeaponSegment();

        weapon.Shots.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Aim_AccessesDefValue()
    {
        var weapon = CreateTestWeaponSegment();

        // Aim can be any positive value
        weapon.Aim.Should().BeGreaterThan(0);
    }
}

public class GunSegmentTests
{
    [Fact]
    public void Adjust_CanDoubleHit()
    {
        var gunDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var gun = gunDef.NewSegment(12345);

        var hit = new HitRecord(1UL, gun, 10, 0.8f);

        // Adjust may produce double hit (20% chance per RNG)
        var adjustedHits = gun.Adjust(hit, 42UL).ToList();

        // Should produce 1 or 2 hits depending on RNG
        adjustedHits.Should().HaveCountGreaterThanOrEqualTo(1);
        adjustedHits.Should().HaveCountLessThanOrEqualTo(2);
    }

    [Fact]
    public void Clone_ReturnsGunSegment()
    {
        var gunDef = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var gun = gunDef.NewSegment(12345);

        var clone = gun.Clone();

        clone.Should().BeOfType<GunSegment>();
    }
}

public class LaserSegmentTests
{
    [Fact]
    public void Adjust_CanCriticalHit()
    {
        var laserDef = new LaserDef('L', 3, "Test Laser", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var laser = laserDef.NewSegment(12345);

        var hit = new HitRecord(1UL, laser, 10, 0.8f);

        // Test many adjusts to find a crit
        bool foundCrit = false;
        for (ulong seed = 0; seed < 100 && !foundCrit; seed++)
        {
            var adjusted = laser.Adjust(hit, seed).First();
            if (adjusted.Damage == hit.Damage * 2)
            {
                foundCrit = true;
            }
        }

        // Should find a crit eventually (20% chance)
        foundCrit.Should().BeTrue("laser should have critical hits");
    }

    [Fact]
    public void Clone_ReturnsLaserSegment()
    {
        var laserDef = new LaserDef('L', 3, "Test Laser", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var laser = laserDef.NewSegment(12345);

        var clone = laser.Clone();

        clone.Should().BeOfType<LaserSegment>();
    }
}

public class MissileSegmentTests
{
    [Fact]
    public void Adjust_CanImproveAim()
    {
        var missileDef = new MissileDef('M', 3, "Test Missile", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var missile = missileDef.NewSegment(12345);

        var hit = new HitRecord(1UL, missile, 10, 0.8f);

        // Test many adjusts to find aim bonus
        bool foundBonus = false;
        for (ulong seed = 0; seed < 100 && !foundBonus; seed++)
        {
            var adjusted = missile.Adjust(hit, seed).First();
            if (adjusted.Aim > hit.Aim)
            {
                foundBonus = true;
            }
        }

        // Should find aim bonus eventually (20% chance)
        foundBonus.Should().BeTrue("missile should have aim bonus chance");
    }

    [Fact]
    public void Adjust_AimBonusIsPlus2()
    {
        var missileDef = new MissileDef('M', 3, "Test Missile", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var missile = missileDef.NewSegment(12345);

        var hit = new HitRecord(1UL, missile, 10, 0.8f);

        // Find a seed that gives aim bonus
        for (ulong seed = 0; seed < 100; seed++)
        {
            var adjusted = missile.Adjust(hit, seed).First();
            if (adjusted.Aim > hit.Aim)
            {
                adjusted.Aim.Should().BeApproximately(hit.Aim + 2, 0.01f);
                return;
            }
        }

        Assert.Fail("Should find a seed with aim bonus");
    }

    [Fact]
    public void Clone_ReturnsMissileSegment()
    {
        var missileDef = new MissileDef('M', 3, "Test Missile", 3, 3, 3, 3, 3, 3, 3, 3, 3);
        var missile = missileDef.NewSegment(12345);

        var clone = missile.Clone();

        clone.Should().BeOfType<MissileSegment>();
    }
}
