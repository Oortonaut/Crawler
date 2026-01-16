using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Segments;

public class SegmentDefTests
{
    // Create a basic SegmentDef for testing
    static SegmentDef CreateTestDef(Tier size, Tier weight, Tier drain, Tier cost, Tier maxHits) =>
        new('X', size, "Test Segment", SegmentKind.Defense, weight, drain, cost, maxHits);

    [Fact]
    public void Resize_ScalesAllTiers()
    {
        var def = CreateTestDef(
            size: 3,
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var resized = def.Resize(5);

        // Delta is 5 - 3 = 2, so all tiers should increase by 2
        resized.Size.Size.Should().Be(5);
        resized.WeightTier.Size.Should().Be(5);
        resized.DrainTier.Size.Should().Be(5);
        resized.CostTier.Size.Should().Be(5);
        resized.MaxHitsTier.Size.Should().Be(5);
    }

    [Fact]
    public void Resize_PreservesQuality()
    {
        var def = CreateTestDef(
            size: (3, 1.5f),
            weight: (3, 1.0f),
            drain: (3, 0.5f),
            cost: (3, 2.0f),
            maxHits: (3, 0)
        );

        var resized = def.Resize(5);

        // Quality should be preserved on all tiers
        resized.Size.Quality.Should().Be(1.5f);
        resized.WeightTier.Quality.Should().Be(1.0f);
        resized.DrainTier.Quality.Should().Be(0.5f);
        resized.CostTier.Quality.Should().Be(2.0f);
        resized.MaxHitsTier.Quality.Should().Be(0);
    }

    [Fact]
    public void Resize_Down_DecreasesAllTiers()
    {
        var def = CreateTestDef(
            size: 5,
            weight: (5, 0),
            drain: (5, 0),
            cost: (5, 0),
            maxHits: (5, 0)
        );

        var resized = def.Resize(2);

        // Delta is 2 - 5 = -3
        resized.Size.Size.Should().Be(2);
        resized.WeightTier.Size.Should().Be(2);
        resized.DrainTier.Size.Should().Be(2);
        resized.CostTier.Size.Should().Be(2);
        resized.MaxHitsTier.Size.Should().Be(2);
    }

    [Fact]
    public void UpgradeWeight_OnlyChangesWeightQuality()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var upgraded = def.UpgradeWeight(2);

        upgraded.WeightTier.Quality.Should().Be(2);
        upgraded.Size.Quality.Should().Be(0);
        upgraded.DrainTier.Quality.Should().Be(0);
        upgraded.CostTier.Quality.Should().Be(0);
        upgraded.MaxHitsTier.Quality.Should().Be(0);
    }

    [Fact]
    public void UpgradeDrain_OnlyChangesDrainQuality()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var upgraded = def.UpgradeDrain(2);

        upgraded.DrainTier.Quality.Should().Be(2);
        upgraded.WeightTier.Quality.Should().Be(0);
    }

    [Fact]
    public void UpgradeCost_OnlyChangesCostQuality()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var upgraded = def.UpgradeCost(2);

        upgraded.CostTier.Quality.Should().Be(2);
        upgraded.WeightTier.Quality.Should().Be(0);
    }

    [Fact]
    public void UpgradeMaxHits_OnlyChangesMaxHitsQuality()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var upgraded = def.UpgradeMaxHits(2);

        upgraded.MaxHitsTier.Quality.Should().Be(2);
        upgraded.WeightTier.Quality.Should().Be(0);
    }

    [Fact]
    public void UpgradeChaining_AccumulatesUpgrades()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var upgraded = def
            .UpgradeWeight(1)
            .UpgradeDrain(2)
            .UpgradeMaxHits(3);

        upgraded.WeightTier.Quality.Should().Be(1);
        upgraded.DrainTier.Quality.Should().Be(2);
        upgraded.MaxHitsTier.Quality.Should().Be(3);
    }

    [Fact]
    public void NewSegment_CreatesSegmentWithDef()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        var segment = def.NewSegment(12345);

        segment.SegmentDef.Should().Be(def);
        segment.Seed.Should().Be(12345UL);
        segment.Name.Should().Be("Test Segment");
        segment.SegmentKind.Should().Be(SegmentKind.Defense);
    }

    [Fact]
    public void FactorySize_DefaultsToSize()
    {
        var def = CreateTestDef(
            size: (3, 0),
            weight: (3, 0),
            drain: (3, 0),
            cost: (3, 0),
            maxHits: (3, 0)
        );

        def.FactorySize.Should().Be(def.Size);
    }

    [Fact]
    public void FactorySize_UsesExplicitValue_WhenSet()
    {
        var def = new SegmentDef('X', 3, "Test", SegmentKind.Defense, 3, 3, 3, 3, FactorySizeTier: (5, 1));

        def.FactorySize.Size.Should().Be(5);
        def.FactorySize.Quality.Should().Be(1);
    }

    [Fact]
    public void NameSize_FormatsCorrectly()
    {
        var def = CreateTestDef(size: (3, 0), weight: 3, drain: 3, cost: 3, maxHits: 3);

        def.NameSize.Should().Contain("Test Segment");
        def.NameSize.Should().Contain("III");
    }

    [Theory]
    [InlineData(1, "I")]
    [InlineData(2, "II")]
    [InlineData(3, "III")]
    [InlineData(4, "IV")]
    [InlineData(5, "V")]
    public void NameSize_ShowsCorrectRomanNumeral(int size, string expected)
    {
        var def = CreateTestDef(size: (size, 0), weight: 1, drain: 1, cost: 1, maxHits: 1);

        def.NameSize.Should().Contain(expected);
    }

    [Fact]
    public void NameSize_ShowsQuality_WhenNonZero()
    {
        var def = CreateTestDef(size: (3, 1.5f), weight: 3, drain: 3, cost: 3, maxHits: 3);

        def.NameSize.Should().Contain("1.50");
    }

    [Fact]
    public void NameSize_OmitsQuality_WhenZero()
    {
        var def = CreateTestDef(size: (3, 0), weight: 3, drain: 3, cost: 3, maxHits: 3);

        // Should not contain a decimal number after the roman numeral
        def.NameSize.Should().EndWith("III");
    }
}

public class WeaponDefTests
{
    static WeaponDef CreateTestWeaponDef() =>
        new('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3);

    [Fact]
    public void CycleLength_CalculatedFromRate()
    {
        var def = CreateTestWeaponDef();

        // CycleLength = TimeDuration.FromSeconds(60 / Rate)
        // Verify it's positive and approximately correct (within 1 second of expected)
        var rate = def.Rate;
        var expectedSeconds = 60.0 / rate;
        def.CycleLength.TotalSeconds.Should().BeGreaterThan(0);
        Math.Abs(def.CycleLength.TotalSeconds - expectedSeconds).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Resize_ScalesWeaponSpecificTiers()
    {
        var def = CreateTestWeaponDef();

        var resized = (WeaponDef)def.Resize(5);

        // Delta is 5 - 3 = 2
        resized.DamageTier.Size.Should().Be(5);
        resized.RateTier.Size.Should().Be(5);
        resized.ShotsTier.Size.Should().Be(5);
        resized.AimTier.Size.Should().Be(5);
        // AmmoUseTier for base WeaponDef is default (0), scales to 2
        resized.AmmoUseTier.Size.Should().Be(2);
    }

    [Fact]
    public void RequiresAmmo_TrueWhenAmmoTypeSet()
    {
        var def = new GunDef('G', 3, "Test Gun", 3, 3, 3, 3, 3, 3, 3, 3, 3);

        def.RequiresAmmo.Should().BeTrue();
        def.AmmoType.Should().Be(Commodity.Slugs);
    }

    [Fact]
    public void RequiresAmmo_FalseWhenNoAmmoType()
    {
        var def = new WeaponDef('W', 3, "Test Melee", 3, 3, 3, 3, 3, 3, 3, 3);

        def.RequiresAmmo.Should().BeFalse();
    }
}

public class TractionDefTests
{
    static TractionDef CreateTestTractionDef(TerrainType limit = TerrainType.Rough) =>
        new('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, limit);

    [Fact]
    public void Resize_ScalesTractionSpecificTiers()
    {
        var def = CreateTestTractionDef();

        var resized = (TractionDef)def.Resize(5);

        resized.LiftTier.Size.Should().Be(5);
        resized.SpeedTier.Size.Should().Be(5);
    }

    [Fact]
    public void TerrainLimit_PreservedOnResize()
    {
        var def = CreateTestTractionDef(TerrainType.Broken);

        var resized = (TractionDef)def.Resize(5);

        resized.TerrainLimit.Should().Be(TerrainType.Broken);
    }
}

public class DefenseDefTests
{
    [Fact]
    public void ArmorDef_Resize_ScalesReductionTier()
    {
        var def = new ArmorDef('A', 3, "Test Armor", 3, 3, 3, 3);

        var resized = (ArmorDef)def.Resize(5);

        resized.ReductionTier.Size.Should().Be(5);
    }

    [Fact]
    public void PlatingDef_Resize_ScalesMitigationTier()
    {
        var def = new PlatingDef('P', 3, "Test Plating", 3, 3, 3, 3);

        var resized = (PlatingDef)def.Resize(5);

        resized.MitigationTier.Size.Should().Be(5);
    }

    [Fact]
    public void ShieldDef_Resize_ScalesShieldTiers()
    {
        var def = new ShieldDef('S', 3, "Test Shield", 3, 3, 3, 3, 3, 3);

        var resized = (ShieldDef)def.Resize(5);

        resized.CapacityTier.Size.Should().Be(5);
        resized.ChargeTier.Size.Should().Be(5);
    }
}
