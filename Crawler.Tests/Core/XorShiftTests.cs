using Crawler;
using FluentAssertions;

namespace Crawler.Tests.Core;

public class XorShiftTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var rng1 = new XorShift(12345);
        var rng2 = new XorShift(12345);

        for (int i = 0; i < 100; i++)
        {
            rng1.Next().Should().Be(rng2.Next());
        }
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var rng1 = new XorShift(12345);
        var rng2 = new XorShift(54321);

        var matches = 0;
        for (int i = 0; i < 100; i++)
        {
            if (rng1.Next() == rng2.Next()) matches++;
        }

        matches.Should().BeLessThan(10, "different seeds should produce different sequences");
    }

    [Fact]
    public void Branch_ProducesDeterministicChild()
    {
        var rng1 = new XorShift(12345);
        var rng2 = new XorShift(12345);

        var branch1 = rng1.Branch();
        var branch2 = rng2.Branch();

        for (int i = 0; i < 100; i++)
        {
            branch1.Next().Should().Be(branch2.Next());
        }
    }

    [Fact]
    public void Branch_DiffersFromParent()
    {
        var rng = new XorShift(12345);
        var parentState = rng.GetState();
        var branch = rng.Branch();

        branch.GetState().Should().NotBe(parentState);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(999)]
    public void PathOperator_ProducesDeterministicBranches(int pathValue)
    {
        var rng1 = new XorShift(12345);
        var rng2 = new XorShift(12345);

        var derived1 = rng1 / pathValue;
        var derived2 = rng2 / pathValue;

        for (int i = 0; i < 50; i++)
        {
            derived1.Next().Should().Be(derived2.Next());
        }
    }

    [Fact]
    public void PathOperator_DifferentPaths_ProduceDifferentSequences()
    {
        var rng = new XorShift(12345);

        var path1 = rng / 1;
        var path2 = rng / 2;

        path1.GetState().Should().NotBe(path2.GetState());
    }

    [Fact]
    public void PathOperator_WithString_IsDeterministic()
    {
        var rng1 = new XorShift(12345);
        var rng2 = new XorShift(12345);

        var derived1 = rng1 / "test";
        var derived2 = rng2 / "test";

        derived1.GetState().Should().Be(derived2.GetState());
    }

    [Fact]
    public void Next_ReturnsNonNegativeValues()
    {
        var rng = new XorShift(12345);

        for (int i = 0; i < 1000; i++)
        {
            rng.Next().Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void NextInt_ReturnsValuesInRange(int endValue)
    {
        var rng = new XorShift(12345);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextInt(endValue);
            value.Should().BeGreaterThanOrEqualTo(0);
            value.Should().BeLessThan(endValue);
        }
    }

    [Fact]
    public void NextInt_WithRange_ReturnsValuesInRange()
    {
        var rng = new XorShift(12345);
        int start = 10;
        int end = 20;

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextInt(start, end);
            value.Should().BeGreaterThanOrEqualTo(start);
            value.Should().BeLessThan(end);
        }
    }

    [Fact]
    public void NextInt_ThrowsOnNonPositiveEndValue()
    {
        var rng = new XorShift(12345);

        var act = () => rng.NextInt(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NextDouble_ReturnsValuesInZeroToOneRange()
    {
        var rng = new XorShift(12345);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextDouble();
            value.Should().BeGreaterThanOrEqualTo(0.0);
            value.Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void NextSingle_ReturnsValuesInZeroToOneRange()
    {
        var rng = new XorShift(12345);

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextSingle();
            value.Should().BeGreaterThanOrEqualTo(0.0f);
            value.Should().BeLessThan(1.0f);
        }
    }

    [Fact]
    public void NextDouble_WithEndValue_ScalesCorrectly()
    {
        var rng = new XorShift(12345);
        double endValue = 100.0;

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextDouble(endValue);
            value.Should().BeGreaterThanOrEqualTo(0.0);
            value.Should().BeLessThan(endValue);
        }
    }

    [Fact]
    public void NextDouble_WithRange_ReturnsValuesInRange()
    {
        var rng = new XorShift(12345);
        double start = 50.0;
        double end = 100.0;

        for (int i = 0; i < 1000; i++)
        {
            var value = rng.NextDouble(start, end);
            value.Should().BeGreaterThanOrEqualTo(start);
            value.Should().BeLessThan(end);
        }
    }

    [Fact]
    public void NextBool_ReturnsBothTrueAndFalse()
    {
        var rng = new XorShift(12345);
        int trueCount = 0;
        int falseCount = 0;

        for (int i = 0; i < 1000; i++)
        {
            if (rng.NextBool()) trueCount++;
            else falseCount++;
        }

        trueCount.Should().BeGreaterThan(100, "should return true sometimes");
        falseCount.Should().BeGreaterThan(100, "should return false sometimes");
    }

    [Fact]
    public void NextBytes_FillsBuffer()
    {
        var rng = new XorShift(12345);
        var buffer = new byte[100];

        rng.NextBytes(buffer);

        buffer.Should().Contain(b => b != 0, "buffer should have non-zero values");
    }

    [Fact]
    public void NextBytes_IsDeterministic()
    {
        var rng1 = new XorShift(12345);
        var rng2 = new XorShift(12345);
        var buffer1 = new byte[100];
        var buffer2 = new byte[100];

        rng1.NextBytes(buffer1);
        rng2.NextBytes(buffer2);

        buffer1.Should().Equal(buffer2);
    }

    [Fact]
    public void Seed_ProducesDifferentSeeds()
    {
        var rng = new XorShift(12345);
        var seeds = new HashSet<ulong>();

        for (int i = 0; i < 100; i++)
        {
            seeds.Add(rng.Seed());
        }

        seeds.Count.Should().Be(100, "all generated seeds should be unique");
    }

    [Fact]
    public void StateManagement_SaveAndRestore()
    {
        var rng = new XorShift(12345);

        // Generate some values
        for (int i = 0; i < 10; i++) rng.Next();

        var state = rng.GetState();
        var expectedNext = rng.Next();

        // Restore state
        var rng2 = new XorShift(1);
        rng2.SetState(state);

        rng2.Next().Should().Be(expectedNext);
    }

    [Fact]
    public void ToData_FromData_RoundTrip()
    {
        var rng = new XorShift(12345);
        for (int i = 0; i < 10; i++) rng.Next();

        var data = rng.ToData();
        var rng2 = new XorShift(data);

        // Both should produce same sequence from here
        for (int i = 0; i < 100; i++)
        {
            rng.Next().Should().Be(rng2.Next());
        }
    }

    [Fact]
    public void Distribution_IsReasonablyUniform()
    {
        var rng = new XorShift(12345);
        var buckets = new int[10];
        int samples = 10000;

        for (int i = 0; i < samples; i++)
        {
            int bucket = rng.NextInt(10);
            buckets[bucket]++;
        }

        // Each bucket should have roughly 10% of samples (allow 20% deviation)
        int expected = samples / 10;
        foreach (var count in buckets)
        {
            count.Should().BeGreaterThan(expected * 8 / 10);
            count.Should().BeLessThan(expected * 12 / 10);
        }
    }
}

public class GaussianSamplerTests
{
    [Fact]
    public void NextDouble_ProducesValues()
    {
        var sampler = new GaussianSampler(12345);
        var values = new List<double>();

        for (int i = 0; i < 1000; i++)
        {
            values.Add(sampler.NextDouble());
        }

        // Gaussian should have mean ~0 and stddev ~1
        var mean = values.Average();
        mean.Should().BeApproximately(0, 0.1, "mean should be close to 0");
    }

    [Fact]
    public void NextDouble_MeanAndStdDev_ScalesCorrectly()
    {
        var sampler = new GaussianSampler(12345);
        double targetMean = 100;
        double targetStdDev = 10;
        var values = new List<double>();

        for (int i = 0; i < 1000; i++)
        {
            values.Add(sampler.NextDouble(targetMean, targetStdDev));
        }

        var mean = values.Average();
        mean.Should().BeApproximately(targetMean, targetStdDev * 0.3, "mean should be close to target");
    }

    [Fact]
    public void IsDeterministic()
    {
        var sampler1 = new GaussianSampler(12345);
        var sampler2 = new GaussianSampler(12345);

        for (int i = 0; i < 100; i++)
        {
            sampler1.NextDouble().Should().Be(sampler2.NextDouble());
        }
    }
}

public class ResidueSamplerTests
{
    [Fact]
    public void Next_AccumulatesResidue()
    {
        var sampler = new ResidueSampler();

        // 0.3 + 0.3 + 0.3 = 0.9, should round to 1
        var result1 = sampler.Next(0.3f);
        var result2 = sampler.Next(0.3f);
        var result3 = sampler.Next(0.3f);

        (result1 + result2 + result3).Should().Be(1);
    }

    [Fact]
    public void Next_IntegerValues_PassThrough()
    {
        var sampler = new ResidueSampler();

        sampler.Next(5.0f).Should().Be(5);
        sampler.Next(3.0f).Should().Be(3);
    }

    [Fact]
    public void Next_PreservesTotalOverTime()
    {
        var sampler = new ResidueSampler();
        float input = 1.7f;
        int totalOutput = 0;

        for (int i = 0; i < 100; i++)
        {
            totalOutput += sampler.Next(input);
        }

        // Total should be very close to 100 * 1.7 = 170
        totalOutput.Should().BeInRange(169, 171);
    }

    [Fact]
    public void Next_Double_PreservesTotalOverTime()
    {
        var sampler = new ResidueSampler();
        double input = 2.3;
        long totalOutput = 0;

        for (int i = 0; i < 100; i++)
        {
            totalOutput += sampler.Next(input);
        }

        // Total should be very close to 100 * 2.3 = 230
        totalOutput.Should().BeInRange(229, 231);
    }
}
