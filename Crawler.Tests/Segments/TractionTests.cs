using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Segments;

public class TractionSegmentTests
{
    static TractionDef CreateTestTractionDef(TerrainType limit = TerrainType.Rough) =>
        new('T', 3, "Test Traction", 3, 3, 3, 3, 3, 3, limit);

    static TractionSegment CreateTestTractionSegment(TerrainType limit = TerrainType.Rough)
    {
        var def = CreateTestTractionDef(limit);
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void Lift_CalculatedFromTier()
    {
        var traction = CreateTestTractionSegment();

        traction.Lift.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Speed_CalculatedFromTier()
    {
        var traction = CreateTestTractionSegment();

        traction.Speed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TerrainLimit_SetFromDef()
    {
        var traction = CreateTestTractionSegment(TerrainType.Broken);

        traction.TerrainLimit.Should().Be(TerrainType.Broken);
    }

    // LiftOn tests
    [Fact]
    public void LiftOn_FullLift_BelowLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Broken);

        // Terrain below limit (Flat < Rough < Broken)
        var liftOnFlat = traction.LiftOn(TerrainType.Flat);
        var liftOnRough = traction.LiftOn(TerrainType.Rough);

        liftOnFlat.Should().Be(traction.Lift);
        liftOnRough.Should().Be(traction.Lift);
    }

    [Fact]
    public void LiftOn_ReducedLift_AtLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Rough);

        var liftAtLimit = traction.LiftOn(TerrainType.Rough);

        // At terrain limit, lift is reduced by TerrainLiftPenalty (0.8)
        liftAtLimit.Should().BeApproximately(traction.Lift * 0.8f, 0.01f);
    }

    [Fact]
    public void LiftOn_ZeroLift_AboveLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Rough);

        var liftAboveLimit = traction.LiftOn(TerrainType.Broken);

        liftAboveLimit.Should().Be(0);
    }

    // SpeedOn tests
    [Fact]
    public void SpeedOn_FullSpeed_BelowLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Broken);

        var speedOnFlat = traction.SpeedOn(TerrainType.Flat);

        speedOnFlat.Should().Be(traction.Speed);
    }

    [Fact]
    public void SpeedOn_ReducedSpeed_AtLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Rough);

        var speedAtLimit = traction.SpeedOn(TerrainType.Rough);

        // At terrain limit, speed is reduced by TerrainSpeedPenalty (0.7)
        speedAtLimit.Should().BeApproximately(traction.Speed * 0.7f, 0.01f);
    }

    [Fact]
    public void SpeedOn_ZeroSpeed_AboveLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Rough);

        var speedAboveLimit = traction.SpeedOn(TerrainType.Broken);

        speedAboveLimit.Should().Be(0);
    }

    // DrainOn tests
    [Fact]
    public void DrainOn_NormalDrain_BelowLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Broken);

        var drainOnFlat = traction.DrainOn(TerrainType.Flat);

        drainOnFlat.Should().Be(traction.Drain);
    }

    [Fact]
    public void DrainOn_IncreasedDrain_AtLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Rough);

        var drainAtLimit = traction.DrainOn(TerrainType.Rough);

        // At terrain limit, drain is increased by TerrainPowerPenalty (1.3)
        drainAtLimit.Should().BeApproximately(traction.Drain * 1.3f, 0.01f);
    }

    [Fact]
    public void DrainOn_ZeroDrain_AboveLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Rough);

        var drainAboveLimit = traction.DrainOn(TerrainType.Broken);

        // Above limit, vehicle can't move, so drain is 0
        drainAboveLimit.Should().Be(0);
    }

    // Terrain type ordering tests
    [Fact]
    public void TerrainTypes_AreOrderedByDifficulty()
    {
        // Verify terrain enum ordering (cast to int for comparison)
        ((int)TerrainType.Flat).Should().BeLessThan((int)TerrainType.Rough);
        ((int)TerrainType.Rough).Should().BeLessThan((int)TerrainType.Broken);
        ((int)TerrainType.Broken).Should().BeLessThan((int)TerrainType.Shattered);
        ((int)TerrainType.Shattered).Should().BeLessThan((int)TerrainType.Ruined);
    }

    [Theory]
    [InlineData(TerrainType.Flat)]
    [InlineData(TerrainType.Rough)]
    [InlineData(TerrainType.Broken)]
    [InlineData(TerrainType.Shattered)]
    public void TerrainLimit_ControlsOperatingRange(TerrainType limit)
    {
        var traction = CreateTestTractionSegment(limit);

        // All terrains below limit should work
        foreach (TerrainType terrain in Enum.GetValues<TerrainType>())
        {
            if (terrain < limit)
            {
                traction.LiftOn(terrain).Should().Be(traction.Lift,
                    $"terrain {terrain} is below limit {limit}");
            }
            else if (terrain == limit)
            {
                traction.LiftOn(terrain).Should().BeApproximately(traction.Lift * 0.8f, 0.01f,
                    $"terrain {terrain} is at limit {limit}");
            }
            else
            {
                traction.LiftOn(terrain).Should().Be(0,
                    $"terrain {terrain} is above limit {limit}");
            }
        }
    }

    [Fact]
    public void Clone_CreatesTractionSegment()
    {
        var traction = CreateTestTractionSegment();
        traction.Hits = 3;

        var clone = traction.Clone();

        clone.Should().BeOfType<TractionSegment>();
        clone.Hits.Should().Be(3);
    }

    [Fact]
    public void Clone_PreservesTerrainLimit()
    {
        var traction = CreateTestTractionSegment(TerrainType.Shattered);

        var clone = (TractionSegment)traction.Clone();

        clone.TerrainLimit.Should().Be(TerrainType.Shattered);
    }
}

public class ChargerSegmentTests
{
    static ChargerDef CreateTestChargerDef() =>
        new('C', 3, "Test Charger", 3, 3, 3, 3);

    static ChargerSegment CreateTestChargerSegment()
    {
        var def = CreateTestChargerDef();
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void Generation_CalculatedFromTier()
    {
        var charger = CreateTestChargerSegment();

        charger.Generation.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generate_ReturnsPowerPerHour_WhenActive()
    {
        var charger = CreateTestChargerSegment();
        charger.Activated = true;

        var generated = charger.Generate(1.0f); // 1 hour

        generated.Should().BeApproximately(charger.Generation, 0.01f);
    }

    [Fact]
    public void Generate_ScalesWithDuration()
    {
        var charger = CreateTestChargerSegment();
        charger.Activated = true;

        var generated = charger.Generate(0.5f); // Half hour

        generated.Should().BeApproximately(charger.Generation * 0.5f, 0.01f);
    }

    [Fact]
    public void Generate_ReturnsZero_WhenNotActive()
    {
        var charger = CreateTestChargerSegment();
        charger.Activated = false;

        var generated = charger.Generate(1.0f);

        generated.Should().Be(0);
    }

    [Fact]
    public void Clone_ReturnsChargerSegment()
    {
        var charger = CreateTestChargerSegment();

        var clone = charger.Clone();

        clone.Should().BeOfType<ChargerSegment>();
    }
}
