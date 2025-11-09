namespace Crawler;
public struct XorShift {
    public XorShift(XorShift other) {
        _state = other._state;
    }
    public XorShift(ulong Seed) {
        _state = MixState(Math.Max(Seed, 1));
    }
    public XorShift(int Seed)
        : this((ulong)Seed) { }

    // Returns [0, int32.MaxValue]
    public int Next() => (int)(_Sample() & System.Int32.MaxValue);
    // Returns [0, int32.MaxValue]
    public ulong NextUint64() => _Sample();
    // Returns a seed for a new generator
    public ulong Seed() => _Sample() * 2517221091 + 23;
    // Returns [0, maxValue)
    public int NextInt(int maxValue) {
        if (maxValue <= 0) throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be positive");
        if (maxValue == int.MaxValue) return Next();
        // fair distribution
        int endSample = (int)(((uint)int.MaxValue + 1) / maxValue) * maxValue;
        int sample;
        while ((sample = Next()) > endSample) { }
        return sample % maxValue;
    }
    public int NextInt(int minValue, int maxValue) => minValue + NextInt(maxValue - minValue);
    // Returns [0, maxValue)
    public ulong NextUint64(ulong maxValue) => _Sample() % Math.Max(maxValue, 1);
    // Returns [0, maxValue)
    public double NextDouble(double maxValue) => NextDouble() * maxValue;
    // Returns [minValue, maxValue)
    public double NextDouble(double minValue, double maxValue) => minValue + NextDouble(maxValue - minValue);
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
        ulong t = _Sample();
        // Mask out denormals
        t &= 0xFFFFFFFFFFFFF000UL;
        return (double)t / ulong.MaxValue;
    }
    public float NextSingle() {
        ulong t = _Sample();
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
        state ^= state >> 12; // a
        state ^= state << 25; // b
        state ^= state >> 27; // c
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

    // State management for save/load
    public ulong GetState() => _state;

    public void SetState(ulong state) => _state = state;

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
    public override string ToString() => $"{_state:X16}";
}

public struct GaussianSampler(ulong seed) {
    XorShift _rng = new(seed);
    bool _primed = false;
    double _zSin = 0;

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
    public double NextDouble(double mean, double stdDev) {
        return NextDouble() * stdDev + mean;
    }
    public float NextSingle(float mean, float stdDev) {
        return NextSingle() * stdDev + mean;
    }
    public static double Quantile(double t) {
        // Beasley-Springer-Moro rational approximation
        // Converts probability t ∈ [0,1] to standard normal quantile (z-score)

        if (t <= 0) return double.NegativeInfinity;
        if (t >= 1) return double.PositiveInfinity;

        // Use symmetry for t > 0.5
        bool reflect = t > 0.5;
        if (reflect) t = 1.0 - t;

        // Coefficients for the rational approximation
        const double a0 = 2.50662823884;
        const double a1 = -18.61500062529;
        const double a2 = 41.39119773534;
        const double a3 = -25.44106049637;

        const double b0 = -8.47351093090;
        const double b1 = 23.08336743743;
        const double b2 = -21.06224101826;
        const double b3 = 3.13082909833;

        double u = Math.Sqrt(-2.0 * Math.Log(t));

        double numerator = a0 + u * (a1 + u * (a2 + u * a3));
        double denominator = 1.0 + u * (b0 + u * (b1 + u * (b2 + u * b3)));

        double z = u - numerator / denominator;

        return reflect ? z : -z;
    }

    // State management for save/load
    public ulong GetRngState() => _rng.GetState();
    public void SetRngState(ulong state) => _rng.SetState(state);
    public bool GetPrimed() => _primed;
    public void SetPrimed(bool primed) => _primed = primed;
    public double GetZSin() => _zSin;
    public void SetZSin(double zSin) => _zSin = zSin;
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
