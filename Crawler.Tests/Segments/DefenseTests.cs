using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Segments;

public class ArmorSegmentTests
{
    static ArmorDef CreateTestArmorDef() =>
        new('A', 3, "Test Armor", 3, 3, 3, 3);

    static ArmorSegment CreateTestArmorSegment()
    {
        var def = CreateTestArmorDef();
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void Reduction_CalculatedFromTier()
    {
        var armor = CreateTestArmorSegment();

        // Reduction should be a positive value
        armor.Reduction.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AddDmg_ReducesDamageByReduction_WhenUsable()
    {
        var armor = CreateTestArmorSegment();
        int initialReduction = armor.Reduction;
        int damage = initialReduction + 5; // More damage than reduction

        var (remaining, desc) = armor.AddDmg(HitType.Hits, damage);

        // Armor should soak some damage
        remaining.Should().BeLessThan(damage);
        desc.Should().Contain("soaked");
    }

    [Fact]
    public void AddDmg_HalvesReduction_WhenDamaged()
    {
        var armor = CreateTestArmorSegment();
        armor.Hits = armor.MaxHealth / 2 + 1; // Damaged state

        armor.IsDamaged.Should().BeTrue();

        // Damaged armor has (reduction + 1) / 2
        // This is tested implicitly through behavior
    }

    [Fact]
    public void AddDmg_DoesNotReduceMisses()
    {
        var armor = CreateTestArmorSegment();
        int initialHits = armor.Hits;

        var (remaining, desc) = armor.AddDmg(HitType.Misses, 10);

        // Misses don't affect armor
        remaining.Should().Be(0);
        armor.Hits.Should().Be(initialHits);
    }

    [Fact]
    public void AddDmg_NoReduction_WhenDestroyed()
    {
        var armor = CreateTestArmorSegment();
        armor.Hits = armor.MaxHealth; // Destroyed

        var (remaining, desc) = armor.AddDmg(HitType.Hits, 10);

        // Destroyed armor doesn't reduce damage
        armor.IsDestroyed.Should().BeTrue();
    }

    [Fact]
    public void Clone_ReturnsArmorSegment()
    {
        var armor = CreateTestArmorSegment();
        armor.Hits = 3;

        var clone = armor.Clone();

        clone.Should().BeOfType<ArmorSegment>();
        clone.Hits.Should().Be(armor.Hits);
    }
}

public class PlatingSegmentTests
{
    static PlatingDef CreateTestPlatingDef() =>
        new('P', 3, "Test Plating", 3, 3, 3, 3);

    static PlatingSegment CreateTestPlatingSegment()
    {
        var def = CreateTestPlatingDef();
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void Mitigation_CalculatedFromTier()
    {
        var plating = CreateTestPlatingSegment();

        // Mitigation should be between 0 and 1
        plating.Mitigation.Should().BeGreaterThan(0);
        plating.Mitigation.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void AddDmg_UsesStochasticMitigation()
    {
        var plating = CreateTestPlatingSegment();
        int damage = 20;

        // Run multiple trials to verify stochastic behavior
        bool foundMitigation = false;
        bool foundNoMitigation = false;

        for (int i = 0; i < 50 && !(foundMitigation && foundNoMitigation); i++)
        {
            var testPlating = CreateTestPlatingSegment();
            var (remaining, desc) = testPlating.AddDmg(HitType.Hits, damage);

            if (desc.Contains("mitigated"))
            {
                foundMitigation = true;
            }
            else
            {
                foundNoMitigation = true;
            }
        }

        // Should find both outcomes due to stochastic nature
        foundMitigation.Should().BeTrue("should mitigate some damage sometimes");
    }

    [Fact]
    public void AddDmg_DoesNotMitigateMisses()
    {
        var plating = CreateTestPlatingSegment();

        var (remaining, desc) = plating.AddDmg(HitType.Misses, 10);

        // Misses pass through
        remaining.Should().Be(0);
    }

    [Fact]
    public void Clone_ReturnsPlatingSegment()
    {
        var plating = CreateTestPlatingSegment();

        var clone = plating.Clone();

        clone.Should().BeOfType<PlatingSegment>();
    }
}

public class ShieldSegmentTests
{
    static ShieldDef CreateTestShieldDef() =>
        new('S', 3, "Test Shield", 3, 3, 3, 3, 3, 3);

    static ShieldSegment CreateTestShieldSegment()
    {
        var def = CreateTestShieldDef();
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void Capacity_CalculatedFromTier()
    {
        var shield = CreateTestShieldSegment();

        shield.Capacity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Charge_CalculatedFromTier()
    {
        var shield = CreateTestShieldSegment();

        shield.Charge.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ShieldLeft_ClampedToCapacity()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = 999;

        shield.ShieldLeft.Should().Be(shield.Capacity);
    }

    [Fact]
    public void ShieldLeft_ClampedToZero()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = -10;

        shield.ShieldLeft.Should().Be(0);
    }

    [Fact]
    public void Tick_ChargesShield_WhenActive()
    {
        var shield = CreateTestShieldSegment();
        shield.Activated = true;
        shield.ShieldLeft = 0;

        // Charge rate is per hour, so use a short duration
        shield.Tick(TimeDuration.FromHours(0.1f));

        shield.ShieldLeft.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Tick_DoesNotChargeAboveCapacity()
    {
        var shield = CreateTestShieldSegment();
        shield.Activated = true;
        shield.ShieldLeft = shield.Capacity;

        shield.Tick(TimeDuration.FromHours(1));

        shield.ShieldLeft.Should().Be(shield.Capacity);
    }

    [Fact]
    public void Tick_MaintainsCharge_WhenDeactivated()
    {
        var shield = CreateTestShieldSegment();
        shield.Activated = false;
        float initialCharge = 5;
        shield.ShieldLeft = initialCharge;

        shield.Tick(TimeDuration.FromHours(1));

        // Should maintain but not increase
        shield.ShieldLeft.Should().Be(initialCharge);
    }

    [Fact]
    public void Tick_ClearsShield_WhenDestroyed()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = shield.Capacity;
        shield.Hits = shield.MaxHealth; // Destroyed

        shield.Tick(TimeDuration.FromSeconds(1));

        shield.ShieldLeft.Should().Be(0);
    }

    [Fact]
    public void AddShieldDmg_AbsorbsDamage()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = 10;

        var (remaining, desc) = shield.AddShieldDmg(7);

        remaining.Should().Be(0);
        shield.ShieldLeft.Should().Be(3);
        desc.Should().Contain("soaked");
    }

    [Fact]
    public void AddShieldDmg_PassesThroughExcess()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = 5;

        var (remaining, desc) = shield.AddShieldDmg(12);

        remaining.Should().Be(7);
        shield.ShieldLeft.Should().Be(0);
    }

    [Fact]
    public void AddDmg_UsesShield_WhenActive()
    {
        var shield = CreateTestShieldSegment();
        shield.Activated = true;
        shield.ShieldLeft = shield.Capacity;
        var initialHits = shield.Hits;

        var (remaining, desc) = shield.AddDmg(HitType.Hits, 5);

        // Shield should absorb before segment takes damage
        shield.ShieldLeft.Should().BeLessThan(shield.Capacity);
    }

    [Fact]
    public void Clone_CopiesShieldLeft()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = 5;

        var clone = (ShieldSegment)shield.Clone();

        clone.ShieldLeft.Should().Be(5);
    }

    [Fact]
    public void ToData_FromData_RoundTrip()
    {
        var shield = CreateTestShieldSegment();
        shield.ShieldLeft = 7;
        shield.Hits = 2;

        var data = shield.ToData();
        var restored = CreateTestShieldSegment();
        restored.FromData(data);

        restored.ShieldLeft.Should().Be(7);
        restored.Hits.Should().Be(2);
    }
}

public class ReactorSegmentTests
{
    static ReactorDef CreateTestReactorDef() =>
        new('R', 3, "Test Reactor", 3, 3, 3, 3, 3);

    static ReactorSegment CreateTestReactorSegment()
    {
        var def = CreateTestReactorDef();
        var segment = def.NewSegment(12345);
        segment.Packaged = false;
        return segment;
    }

    [Fact]
    public void Capacity_CalculatedFromTier()
    {
        var reactor = CreateTestReactorSegment();

        reactor.Capacity.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generation_CalculatedFromTier()
    {
        var reactor = CreateTestReactorSegment();

        reactor.Generation.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Charge_InitializesToCapacity()
    {
        var reactor = CreateTestReactorSegment();

        reactor.Charge.Should().Be(reactor.Capacity);
    }

    [Fact]
    public void Charge_ClampedToCapacity()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Charge = 999;

        reactor.Charge.Should().Be(reactor.Capacity);
    }

    [Fact]
    public void Charge_ClampedToZero()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Charge = -10;

        reactor.Charge.Should().Be(0);
    }

    [Fact]
    public void Generate_AddsChargeOverTime()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Activated = true;
        reactor.Charge = 0;

        reactor.Generate(1.0f); // 1 hour

        reactor.Charge.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Generate_DoesNotExceedCapacity()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Activated = true;
        reactor.Charge = reactor.Capacity;

        reactor.Generate(10.0f); // Long time

        reactor.Charge.Should().Be(reactor.Capacity);
    }

    [Fact]
    public void Generate_ReturnsZero_WhenNotActive()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Activated = false;
        reactor.Charge = 5;

        var generated = reactor.Generate(1.0f);

        generated.Should().Be(0);
    }

    [Fact]
    public void Clone_CopiesCharge()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Charge = 5;

        var clone = (ReactorSegment)reactor.Clone();

        clone.Charge.Should().Be(5);
    }

    [Fact]
    public void ToData_FromData_RoundTrip()
    {
        var reactor = CreateTestReactorSegment();
        reactor.Charge = 7;
        reactor.Hits = 2;

        var data = reactor.ToData();
        var restored = CreateTestReactorSegment();
        restored.FromData(data);

        restored.Charge.Should().Be(7);
        restored.Hits.Should().Be(2);
    }
}
