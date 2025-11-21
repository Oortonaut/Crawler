namespace Crawler;

public static partial class CollectionEx {
    // Choose/Selection methods
    public static T? ChooseAt<T>(this IEnumerable<T> seq, float at) {
        return ChooseAt(seq.ToList().AsReadOnly(), at);
    }

    public static T? ChooseAt<T>(this IReadOnlyList<T> seq, float at) {
        return seq.Any() ? seq[(int)(at * seq.Count)] : default;
    }

    public static T? ChooseAt<T>(this IReadOnlyCollection<T> seq, float at) {
        int index = (int)(at * seq.Count);
        return seq.Skip(index).FirstOrDefault();
    }

    public static T? ChooseRandom<T>(ref this XorShift rng, IReadOnlyList<T> seq) {
        return ChooseAt(seq, rng.NextSingle());
    }

    public static T? ChooseRandom<T>(ref this XorShift rng, IReadOnlyCollection<T> seq) {
        return ChooseAt(seq, rng.NextSingle());
    }

    public static T? ChooseRandom<T>(ref this XorShift rng, IEnumerable<T> seq) {
        return ChooseAt(seq, rng.NextSingle());
    }

    public static T? ChooseWeightedAt<T>(this IEnumerable<(T Item, float Weight)> inSeq, float at) {
        var seq = inSeq.ToArray();
        var weights = new List<float>();
        float totalWeight = 0;
        foreach (var (item, weight) in seq) {
            totalWeight += weight;
            weights.Add(totalWeight);
        }
        if (totalWeight == 0) return default;
        int selected = weights.BinarySearch(at * totalWeight);
        if (selected < 0) {
            selected = ~selected;
        }
        return seq[selected].Item;
    }

    public static T? ChooseWeightedRandom<T>(this IEnumerable<(T Item, float Weight)> seq, ref XorShift rng) {
        return ChooseWeightedAt(seq, rng.NextSingle());
    }

    public static IReadOnlyList<T> ChooseRandomK<T>(this IEnumerable<T> seq, int k, XorShift rng) {
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), "k must be non-negative");
        if (k == 0) return Array.Empty<T>();

        // Reservoir sampling for k items (Vitter's algorithm R)
        List<T> reservoir = new(k);
        int count = 0;
        foreach (var item in seq) {
            if (count < k) {
                reservoir.Add(item);
            } else {
                // pick a random index in [0, count] and replace if < k
                int j = rng.NextInt(0, count + 1);
                if (j < k) reservoir[j] = item;
            }
            count++;
        }

        // If fewer than k items were present, return only what we have.
        if (count <= k) return reservoir.AsReadOnly();

        return reservoir.AsReadOnly();
    }

    public static IReadOnlyList<T> ChooseWeightedRandomK<T>(this IEnumerable<(T Item, float Weight)> seq, int k, XorShift rng) {
        if (k < 0) throw new ArgumentOutOfRangeException(nameof(k), "k must be non-negative");
        if (k == 0) return Array.Empty<T>();

        // Efraimidis–Spirakis weighted sampling without replacement.
        // For each item with weight w > 0, generate key = log(U) / w (U ~ Uniform(0,1))
        // and pick the top-k by key (larger is better; keys are <= 0).
        var keys = new List<(T item, double key)>();
        foreach (var (item, weight) in seq) {
            if (weight > 0f && !float.IsNaN(weight) && !float.IsInfinity(weight)) {
                float u = rng.NextSingle();
                // Avoid log(0). Using a tiny floor ensures numerical stability.
                if (u <= 0f) u = float.Epsilon;
                double key = Math.Log(u) / weight;
                keys.Add((item, key));
            }
        }

        if (keys.Count == 0) {
            return Array.Empty<T>();
        }

        if (k >= keys.Count) {
            var all = new List<T>(keys.Count);
            foreach (var kv in keys) all.Add(kv.item);
            return all.AsReadOnly();
        }

        keys.Sort((a, b) => b.key.CompareTo(a.key)); // descending by key
        var result = new List<T>(k);
        for (int i = 0; i < k; i++) {
            result.Add(keys[i].item);
        }
        return result.AsReadOnly();
    }

    // Shuffle
    public static void Shuffle<T>(ref this XorShift rng, IList<T> list) {
        int n = list.Count;
        while (n > 1) {
            int k = rng.NextInt(n);
            n--;
            (list[k], list[n]) = (list[n], list[k]);
        }
    }

    // Dictionary extensions
    public static TValue GetOrAddNew<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) where TValue: new() {
        return dict.GetOrAddNew(key, () => new TValue());
    }

    public static TValue GetOrAddNew<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, Func<TValue> gen) {
        if (!dict.TryGetValue(key, out var value)) {
            value = gen();
            dict[key] = value;
        }
        return value;
    }

    // Sequence transformations
    public static IEnumerable<T?> PadTo<T>(this IEnumerable<T> source, int width) {
        foreach (var i in source) {
            --width;
            yield return i;
        }
        while (width-- > 0) {
            yield return default;
        }
    }

    public static IEnumerable<(T a, T b)> Pairwise<T>(this IEnumerable<T> source) {
        using var enumerator = source.GetEnumerator();
        if (!enumerator.MoveNext()) {
            yield break;
        }
        T previous = enumerator.Current;
        while (enumerator.MoveNext()) {
            yield return (previous, enumerator.Current);
            previous = enumerator.Current;
        }
    }

    public static IEnumerable<string> ZipColumns(this IList<string> left, IList<string> right) {
        var leftWidth = left.Max(x => x.Length);
        var rightWidth = right.Max(x => x.Length);
        int count = Math.Max(left.Count, right.Count);
        for (int i = 0; i < count; i++) {
            string leftString = i < left.Count ? left[i] : string.Empty;
            string rightString = i < right.Count ? right[i] : string.Empty;
            leftString = leftString.PadRight(leftWidth);
            yield return $"{leftString} {rightString}";
        }
    }

    // Iteration helpers
    public static void Do<T>(this IEnumerable<T> e) {
        foreach (var item in e) {
        }
    }

    public static void Do<T>(this IEnumerable<T> e, Action<T> action) {
        foreach (var item in e) {
            action(item);
        }
    }

    public static void Do<T, U>(this IEnumerable<T> e, Action<T, U> action, U arg) {
        foreach (var item in e) {
            action(item, arg);
        }
    }

    // 2D Array extensions
    public static IEnumerable<(int X, int Y)> Index<T>(this T[,] array) {
        int H = array.GetLength(0);
        int W = array.GetLength(1);
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                yield return (x, y);
            }
        }
    }

    public static void Fill<T>(this T[,] array, T value) {
        int H = array.GetLength(0);
        int W = array.GetLength(1);
        for (int y = 0; y < H; y++) {
            for (int x = 0; x < W; x++) {
                array[y, x] = value;
            }
        }
    }

    // Normalization
    public static IEnumerable<float> Normalize(this IEnumerable<float> e) {
        var sum = e.Sum();
        var recip = 1.0f / sum;
        return e.Select(item => item * recip);
    }

    // Structure construction
    public static Stack<T> ToStack<T>(this IEnumerable<T> e) => new Stack<T>(e);
}
