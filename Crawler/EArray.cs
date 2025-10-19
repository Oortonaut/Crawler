using System.Collections;
using System.Linq;

namespace Crawler;

public struct EArray<ENUM, T>: IEnumerable<T>, IList<T>, ICollection<T>, IDictionary<ENUM, T>, IReadOnlyList<T>, IReadOnlyDictionary<ENUM, T> where ENUM : struct, Enum, IConvertible {
    public EArray() {
        _items = new T[_count];
        _addIndex.Value = 0;
    }
    public EArray(params T[] items): this() {
        _items = items;
        if (items.Length != _count) {
            throw new ArgumentException("Invalid number of items");
        }
    }
    public EArray(IEnumerable<T> items) {
        _items = items.ToArray();
        if (_items.Count() != _count) {
            throw new ArgumentException("Invalid number of items");
        }
    }
    public EArray(IEnumerable<(ENUM, T)> items): this() {
        foreach (var (key, value) in items) {
            this[key] = value;
        }
    }
    T IDictionary<ENUM, T>.this[ENUM key] {
        get {
            return _items[(int)(object)key];
        }
        set {
            _items[(int)(object)key] = value;
        }
    }
    T IReadOnlyDictionary<ENUM, T>.this[ENUM key] {
        get {
            return _items[( int ) ( object ) key];
        }
    }
    public ref T this[ENUM key] => ref _items[(int)(object)key];
    public void Initialize() {
        _items.Initialize();
    }
    public void Initialize(T value) {
        for (int i = 0; i < _items.Length; ++i) {
            _items[i] = value;
        }
    }
    public void Initialize(Func<T> valueFunc) {
        for (int i = 0; i < _items.Length; ++i) {
            _items[i] = valueFunc();
        }
    }
    public int Length => _items.Length;
    public T[] Items => _items;

    public static int[] UnderlyingKeys() => Enum.GetValuesAsUnderlyingType<ENUM>().Cast<int>().ToArray();
    public void Add(T value) {
        if (_addIndex.Value >= _items.Length) {
            throw new InvalidOperationException("Cannot add more items: array is full");
        }
        _items[_addIndex.Value++] = value;
    }
    public EArray<ENUM, T> Clone() => new(_items.ToArray());

    public IEnumerator<T> GetEnumerator() {
        return _items!.Cast<T>().GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
    public IEnumerable<(int Index, T Item)> Index() {
        for (int i = 0; i < _items.Length; ++i) {
            yield return (i, _items[i]);
        }
    }
    // ( Key, Value)
    public IEnumerable<(ENUM Key, T Value)> Pairs() {
        foreach (var item in Enum.GetValues<ENUM>()) {
            if (Enum.IsDefined(item)) {
                yield return (item, _items[item.ToInt32(null)]);
            }
        }
    }

    // IList<T> and ICollection<T> implementation
    public int Count => _items.Length;
    public bool IsReadOnly => false;

    public T this[int index] {
        get => _items[index];
        set => _items[index] = value;
    }

    public int IndexOf(T item) {
        for (int i = 0; i < _items.Length; i++) {
            if (EqualityComparer<T>.Default.Equals(_items[i], item)) {
                return i;
            }
        }
        return -1;
    }

    public void Insert(int index, T item) {
        throw new NotSupportedException("Insert is not supported on fixed-size enum array");
    }

    public void RemoveAt(int index) {
        throw new NotSupportedException("RemoveAt is not supported on fixed-size enum array");
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public void CopyTo(T[] array, int arrayIndex) {
        _items.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item) {
        throw new NotSupportedException("Remove is not supported on fixed-size enum array");
    }

    public void Clear() {
        _addIndex.Value = 0;
        Array.Clear(_items, 0, _items.Length);
    }

    // IDictionary<ENUM, T> implementation
    public ICollection<ENUM> Keys => Enum.GetValues<ENUM>();
    public ICollection<T> Values => _items;

    IEnumerable<ENUM> IReadOnlyDictionary<ENUM, T>.Keys => Keys;
    IEnumerable<T> IReadOnlyDictionary<ENUM, T>.Values => Values;

    public void Add(ENUM key, T value) => this[key] = value;

    public bool ContainsKey(ENUM key) {
        return Enum.IsDefined(key);
    }

    public bool Remove(ENUM key) {
        throw new NotSupportedException("Remove is not supported on fixed-size enum array");
    }

    public bool TryGetValue(ENUM key, out T value) {
        if (ContainsKey(key)) {
            value = this[key];
            return true;
        }
        value = default(T)!;
        return false;
    }

    public void Add(KeyValuePair<ENUM, T> item) {
        this[item.Key] = item.Value;
    }

    public bool Contains(KeyValuePair<ENUM, T> item) {
        return ContainsKey(item.Key) && EqualityComparer<T>.Default.Equals(this[item.Key], item.Value);
    }

    public void CopyTo(KeyValuePair<ENUM, T>[] array, int arrayIndex) {
        var pairs = Pairs().ToArray();
        Array.Copy(pairs.Select(p => new KeyValuePair<ENUM, T>(p.Key, p.Value)).ToArray(), 0, array, arrayIndex, pairs.Length);
    }

    public bool Remove(KeyValuePair<ENUM, T> item) {
        throw new NotSupportedException("Remove is not supported on fixed-size enum array");
    }

    IEnumerator<KeyValuePair<ENUM, T>> IEnumerable<KeyValuePair<ENUM, T>>.GetEnumerator() {
        return Pairs().Select(p => new KeyValuePair<ENUM, T>(p.Key, p.Value)).GetEnumerator();
    }

    T[] _items;

    static EArray() {
        _count = 0;
        foreach (var objValue in Enum.GetValuesAsUnderlyingType<ENUM>()) {
            if (objValue is IConvertible i) {
                _count = Math.Max(_count, i.ToInt32(null) + 1);
            }
        }
    }
    static int _count;
    static ThreadLocal<int> _addIndex = new ThreadLocal<int>(() => 0);
}

public static partial class EArrayEx {
    public static EArray<ENUM, T> ToEArray<ENUM, T>(this IEnumerable<T> items) where ENUM : struct, Enum, IConvertible => new(items);
    public static EArray<ENUM, T> ToEArray<ENUM, T>(this T[] items) where ENUM : struct, Enum, IConvertible => new(items);
    public static EArray<ENUM, T> ToEArray<ENUM, T>(this IEnumerable<(ENUM e, T t)> items) where ENUM : struct, Enum, IConvertible => new EArray<ENUM, T>(items);

    // LINQ extension methods for EArray
    public static bool All<ENUM, T>(this EArray<ENUM, T> array, Func<T, bool> predicate) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<T>)array).All(predicate);

    public static IEnumerable<TResult> Select<ENUM, T, TResult>(this EArray<ENUM, T> array, Func<T, TResult> selector) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<T>)array).Select(selector);

    public static IEnumerable<TResult> Zip<ENUM, T, TOther, TResult>(this EArray<ENUM, T> array, IEnumerable<TOther> other, Func<T, TOther, TResult> resultSelector) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<T>)array).Zip(other, resultSelector);

    public static IEnumerable<(T First, TOther Second)> Zip<ENUM, T, TOther>(this EArray<ENUM, T> array, IEnumerable<TOther> other) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<T>)array).Zip(other);

    // Overload specifically for EArray to EArray zip
    public static IEnumerable<(T First, T Second)> Zip<ENUM, T>(this EArray<ENUM, T> array, EArray<ENUM, T> other) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<T>)array).Zip((IEnumerable<T>)other);

    // Overload specifically for EArray to EArray zip with result selector
    public static IEnumerable<TResult> Zip<ENUM, T, TResult>(this EArray<ENUM, T> array, EArray<ENUM, T> other, Func<T, T, TResult> resultSelector) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<T>)array).Zip((IEnumerable<T>)other, resultSelector);

    public static float Sum<ENUM>(this EArray<ENUM, float> array) where ENUM : struct, Enum, IConvertible
        => ((IEnumerable<float>)array).Sum();
}
