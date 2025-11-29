using System.Numerics;

namespace Crawler;

public struct XorShift {
    public XorShift(XorShift other) {
        _state = other._state;
    }
    public XorShift(ulong Seed) {
        Seed = Seed * M32 + 1337;
        _state = MixState(Math.Max(Seed, 1));
    }
    public XorShift(int Seed)
        : this((ulong)Seed) { }

    // Returns [0, int32.endValue]
    public int Next() => (int)(_Sample() & System.Int32.MaxValue);
    // Returns [0, int32.endValue]
    public ulong NextUint64() => _Sample();
    // Returns [0, int32.endValue]
    public ulong NextInt64() => _Sample() & System.Int64.MaxValue;
    // Returns a seed for a new generator
    public ulong Seed() => _Sample() * 2517221091 + 23;
    // returns a new generator from Seed
    public XorShift Branch() => new XorShift(Seed());
    // Returns [0, endValue)
    public int NextInt(int endValue) {
        if (endValue <= 0) throw new ArgumentOutOfRangeException(nameof(endValue), "endValue must be positive");
        return (int)(_Sample() % (ulong)Math.Max(endValue, 1));
    }
    public int NextInt(int startValue, int endValue) => startValue + NextInt(endValue - startValue);
    // Returns [0, endValue).
    public long NextInt64(long endValue) => (long)(_Sample() % (ulong)Math.Max(endValue, 1));
    // Returns [0, endValue).
    public ulong NextUint64(ulong endValue) => _Sample() % Math.Max(endValue, 1);
    // Returns [0, endValue)
    public double NextDouble(double endValue) => NextDouble() * endValue;
    // Returns [startValue, endValue)
    public double NextDouble(double startValue, double endValue) => startValue + NextDouble(endValue - startValue);
    // Fill a random array of bytes
    public void NextBytes(byte[] buffer) {
        ulong data = 0;
        for (int b = 0; b < buffer.Length; ++b) {
            if ((b & 7) == 0) {
                data = _Sample();
            }
            buffer[b] = (byte)(data & 0xFF);
            data >>= 8;
        }
    }
    // Returns [0, 1)
    public double NextDouble() {
        // -1 remaps (0, 1] to [0, 1)
        ulong t = _Sample() - 1;
        // Mask out denormals
        t &= 0xFFFFFFFFFFFFF000UL;
        return (double)t / ulong.MaxValue;
    }
    public float NextSingle() {
        // -1 remaps (0, 1] to [0, 1)
        ulong t = _Sample() - 1;
        // Mask out denormals
        t &= 0xFFFFFE0000000000UL;
        return (float)t / ulong.MaxValue;
    }

    // Cycles the state. The period is 2^64 - 1.  xorshift8* algorithm with m32.
    // http://vigna.di.unimi.it/ftp/papers/xorshift.pdf via Wikipedia XorShift

    ulong _Sample() {
        var result = _state;
        _state = MixState(_state);
        return result;
    }
    public static ulong MixState(ulong state) {
        //                   62     56     50     44     38     32     26     20     14      8      2
        // var primeBits = 0b0000_101000_001000_001000_101000_100000_101000_001000_101000_101000_101011
        const ulong primeBits1 = 0b0000_100000_000000_001000_000000_100000_000000_001000_000000_100000_001000ul;
        const ulong primeBits2 = 0b0000_001000_000000_000000_100000_000000_100000_000000_100000_001000_000010ul;
        const ulong primeBits3 = 0b0000_000000_001000_000000_001000_000000_001000_000000_001000_000000_100001ul;
        state ^= BitOperations.RotateRight(state * primeBits1, 12); // 52 left
        state ^= BitOperations.RotateLeft(state * primeBits2, 25); // 39 right
        state ^= BitOperations.RotateRight(state * primeBits3, 17); // 47 left
        return state;
    }
    const ulong M32 = 0x2545F4914F6CDD1DUL;
    public static XorShift operator /(XorShift a, XorShift b) => new XorShift(a._state * 701 + b._state * M32);
    public static XorShift operator /(XorShift a, string b) => new XorShift(a._state * 701 + (ulong)b.GetHashCode() * M32);
    public static XorShift operator /(XorShift a, ulong b) => new XorShift(a._state * 701 + b * M32);
    public static XorShift operator /(XorShift a, long b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, uint b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, int b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, ushort b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, short  b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, char b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, byte  b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, sbyte b) => new XorShift(a._state * 701 + (ulong)b * M32);
    public static XorShift operator /(XorShift a, object b) => new XorShift(a._state * 701 + (ulong)b.GetHashCode() * M32);

    ulong _state; /* The state must be seeded with a nonzero value. */

    // Data structure for serialization
    public record class Data {
        public ulong State { get; set; }
    }

    // State management for save/load
    public ulong GetState() => _state;
    public void SetState(ulong state) => _state = state;

    public Data ToData() => new Data { State = _state };
    public void FromData(Data data) => _state = data.State;
    public XorShift(Data data) : this(1) { FromData(data); }

    // Extension methods for choosing from collections
    public T? Next<T>(IEnumerable<T> choices) {
        return choices.ChooseAt(this.NextSingle());
    }
    public T? Next<T>(IReadOnlyList<T> choices) {
        return choices.ChooseAt(this.NextSingle());
    }
    public T? Next<T>(IReadOnlyCollection<T> choices) {
        return choices.ChooseAt(this.NextSingle());
    }
    public T? NextWeighted<T>(IEnumerable<(T Item, float Weight)> choices) {
        return choices.ChooseWeightedAt(this.NextSingle());
    }
    public override string ToString() => $"{_state:X16} {(double)(_state-1)/ulong.MaxValue}";
}

public struct GaussianSampler(ulong seed) {
    XorShift _rng = new(seed);
    bool _primed = false;
    double _zSin = 0;

    // Data structure for serialization
    public record class Data {
        public XorShift.Data Rng { get; set; } = null!;
        public bool Primed { get; set; }
        public double ZSin { get; set; }
    }

    public double NextDouble() {
        if (_primed) {
            _primed = false;
            return _zSin;
        } else {
            double u1 = _rng.NextDouble();
            double u2 = _rng.NextDouble();

            double z = Math.Sqrt(-2.0 * Math.Log(u1));
            var (Sin, Cos) = Math.SinCos(Math.Tau * u2);
            double zCos = z * Cos;
            _zSin = z * Sin;
            _primed = true;

            return zCos;
        }
    }
    public float NextSingle() => (float)NextDouble();
    public double NextDouble(double mean, double stdDev) => NextDouble() * stdDev + mean;
    public float NextSingle(float mean, float stdDev) => NextSingle() * stdDev + mean;
    public static double Erf(double x) => MathNet.Numerics.SpecialFunctions.Erf(x);
    public static double CDF(double x) => MathNet.Numerics.Distributions.Normal.CDF(x, 1, 1);
    public static double Quantile(double t) => MathNet.Numerics.Distributions.Normal.InvCDF(t, 1, 1);

    // State management for save/load
    public ulong GetRngState() => _rng.GetState();
    public void SetRngState(ulong state) => _rng.SetState(state);
    public bool GetPrimed() => _primed;
    public void SetPrimed(bool primed) => _primed = primed;
    public double GetZSin() => _zSin;
    public void SetZSin(double zSin) => _zSin = zSin;

    public Data ToData() => new Data {
        Rng = _rng.ToData(),
        Primed = _primed,
        ZSin = _zSin
    };

    public void FromData(Data data) {
        _rng.FromData(data.Rng);
        _primed = data.Primed;
        _zSin = data.ZSin;
    }

    public GaussianSampler(Data data) : this(1) { FromData(data); }
}

// Converts a float to an integer but integrates error over time.
public record ResidueSampler() {
    double _residue = 0;
    public int Next(float value) {
        _residue += value;
        var result = Math.Round(_residue);
        _residue -= result;
        return (int)result;
    }
    public long Next(double value) {
        _residue += value;
        var result = Math.Round(_residue);
        _residue -= result;
        return (long) result;
    }
}
