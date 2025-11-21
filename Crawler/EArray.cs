using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Crawler;

public struct EArray<ENUM, T>: IEnumerable<T>, IList<T>, ICollection<T>, IDictionary<ENUM, T>, IReadOnlyList<T>, IReadOnlyDictionary<ENUM, T> where ENUM : struct, Enum, IConvertible {
    public EArray() {
        _values = new T[_count];
        _addIndex = 0;
    }
    public EArray(params T[] items): this() {
        _values = items;
        if (items.Length != _count) {
            throw new ArgumentException("Invalid number of items");
        }
    }
    public EArray(IEnumerable<T> items) {
        _values = items.PadTo(_count).Cast<T>().ToArray();
        if (_values.Count() != _count) {
            throw new ArgumentException("Invalid number of items");
        }
    }
    public EArray(IEnumerable<(ENUM, T)> items): this() {
        foreach (var (key, value) in items) {
            this[key] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ToIndex(ENUM key) {
        if (Unsafe.SizeOf<ENUM>() == sizeof(int)) {
            return Unsafe.As<ENUM, int>(ref key);
        }
        if (Unsafe.SizeOf<ENUM>() == sizeof(byte)) {
            return Unsafe.As<ENUM, byte>(ref key);
        }
        if (Unsafe.SizeOf<ENUM>() == sizeof(short)) {
            return Unsafe.As<ENUM, short>(ref key);
        }
        return (int)(object)key;
    }

    T IDictionary<ENUM, T>.this[ENUM key] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            return _values[ToIndex(key)];
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set {
            _values[ToIndex(key)] = value;
        }
    }
    T IReadOnlyDictionary<ENUM, T>.this[ENUM key] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            return _values[ToIndex(key)];
        }
    }

    public ref T this[ENUM key] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            return ref _values[ToIndex(key)];
        }
    }
    public void Initialize() {
        _values.Initialize();
    }
    public void Initialize(T value) {
        Array.Fill(_values, value);
    }
    public void Initialize(Func<T> valueFunc) {
        for (int i = 0; i < _values.Length; ++i) {
            _values[i] = valueFunc();
        }
    }
    public int Length => _values.Length;
    public T[] Items => _values;

    public static int[] UnderlyingKeys() => _keysUnderlying;
    public void Add(T value) {
        if (_addIndex >= _values.Length) {
            throw new InvalidOperationException("Cannot add more items: array is full");
        }
        _values[_addIndex++] = value;
    }
    public EArray<ENUM, T> Clone() => new(_values.ToArray());

    public Enumerator GetEnumerator() {
        return new Enumerator(_values);
    }
    IEnumerator<T> IEnumerable<T>.GetEnumerator() {
        return GetEnumerator();
    }
    IEnumerator IEnumerable.GetEnumerator() {
        return GetEnumerator();
    }
    public IEnumerable<(int Index, T Item)> Index() {
        for (int i = 0; i < _values.Length; ++i) {
            yield return (i, _values[i]);
        }
    }
    // ( Key, Value)
    public IEnumerable<(ENUM Key, T Value)> Pairs() {
        foreach (var item in _keys) {
            yield return (item, _values[item.ToInt32(null)]);
        }
    }

    // IList<T> and ICollection<T> implementation
    public int Count => _count;
    public bool IsReadOnly => false;

    T IList<T>.this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _values[index] = value;
    }

    T IReadOnlyList<T>.this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values[index];
    }

    public int IndexOf(T item) {
        return Array.IndexOf(_values, item);
    }

    public void Insert(int index, T item) {
        throw new NotSupportedException("Insert is not supported on fixed-size enum array");
    }

    public void RemoveAt(int index) {
        throw new NotSupportedException("RemoveAt is not supported on fixed-size enum array");
    }

    public bool Contains(T item) => IndexOf(item) >= 0;

    public void CopyTo(T[] array, int arrayIndex) {
        _values.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item) {
        throw new NotSupportedException("Remove is not supported on fixed-size enum array");
    }

    public void Clear() {
        _addIndex = 0;
        Array.Clear(_values, 0, _values.Length);
    }

    // IDictionary<ENUM, T> implementation
    public ICollection<ENUM> Keys => _keys;
    public ICollection<T> Values => _values;

    IEnumerable<ENUM> IReadOnlyDictionary<ENUM, T>.Keys => _keys;
    IEnumerable<T> IReadOnlyDictionary<ENUM, T>.Values => Values;

    public void Add(ENUM key, T value) => this[key] = value;

    public bool ContainsKey(ENUM key) {
        return ToIndex(key) < _count;
    }

    public bool Remove(ENUM key) {
        // TODO: Make this bettter for nullables and use the nullability
        // to indicate absence from the array
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

    public struct Enumerator : IEnumerator<T> {
        readonly T[] _items;
        int _index;

        internal Enumerator(T[] items) {
            _items = items;
            _index = -1;
        }

        public T Current => _items[_index];
        object IEnumerator.Current => Current!;

        public bool MoveNext() => ++_index < _items.Length;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }

    T[] _values;
    static ENUM[] _keys;
    static int _count;
    static int[] _keysUnderlying;

    static EArray() {
        _keysUnderlying = Enum.GetValuesAsUnderlyingType<ENUM>().Cast<int>().ToArray();
        _keys = Enum.GetValues<ENUM>();
        _count = _keysUnderlying.Length;
    }

    [ThreadStatic]
    static int _addIndex;
}

/// <summary>
/// A variation of EArray where default values (null, 0, false) indicate a missing key.
/// </summary>
public struct EArraySparse<ENUM, T> : IEnumerable<T>, IList<T>, ICollection<T>, IDictionary<ENUM, T>, IReadOnlyList<T>, IReadOnlyDictionary<ENUM, T> where ENUM : struct, Enum, IConvertible {
    public EArraySparse() {
        _values = new T[_count];
        _addIndex = 0;
    }
    public EArraySparse(params T[] items) : this() {
        _values = items;
        if (items.Length != _count) {
            throw new ArgumentException("Invalid number of items");
        }
    }
    public EArraySparse(IEnumerable<T> items) {
        _values = items.PadTo(_count).Cast<T>().ToArray();
        if (_values.Count() != _count) {
            throw new ArgumentException("Invalid number of items");
        }
    }
    // Initialize from pairs, leaving unmentioned keys as null
    public EArraySparse(IEnumerable<(ENUM, T)> items) : this() {
        foreach (var (key, value) in items) {
            this[key] = value;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static int ToIndex(ENUM key) {
        if (Unsafe.SizeOf<ENUM>() == sizeof(int)) return Unsafe.As<ENUM, int>(ref key);
        if (Unsafe.SizeOf<ENUM>() == sizeof(byte)) return Unsafe.As<ENUM, byte>(ref key);
        if (Unsafe.SizeOf<ENUM>() == sizeof(short)) return Unsafe.As<ENUM, short>(ref key);
        return (int)(object)key;
    }
        static int _count;

        static EArraySparse() {
            _keys = Enum.GetValues<ENUM>();
            _count = Enum.GetValuesAsUnderlyingType<ENUM>().Length;
            // Removed the runtime check for nullable types
        }

        // Helper to check for "missing" (default) values generically
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsPresent(T value) {
            // This works for both reference types (!= null) and value types (!= 0)
            return !EqualityComparer<T>.Default.Equals(value, default);
        }

        T IDictionary<ENUM, T>.this[ENUM key] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var val = _values[ToIndex(key)];
                if (!IsPresent(val)) throw new KeyNotFoundException($"Key {key} is missing (value is default)");
                return val;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set {
                _values[ToIndex(key)] = value;
            }
        }

        T IReadOnlyDictionary<ENUM, T>.this[ENUM key] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {
                var val = _values[ToIndex(key)];
                if (!IsPresent(val)) throw new KeyNotFoundException($"Key {key} is missing (value is default)");
                return val;
            }
        }

    // Direct ref access (Advanced): Returns the slot directly.
    // Caller sees null if missing. Useful for "Get or Add" patterns via ref.
    public ref T this[ENUM key] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get {
            return ref _values[ToIndex(key)];
        }
    }

    public void Initialize() => _values.Initialize();
    // Caution: Initialize(value) might fill "missing" slots if value is not null
    public void Initialize(T value) => Array.Fill(_values, value);

    public int Length => _values.Length;
    public T[] Items => _values; // Raw array access (includes nulls)

    public void Add(T value) {
        if (_addIndex >= _values.Length) throw new InvalidOperationException("Cannot add more items: array is full");
        _values[_addIndex++] = value;
    }

    // Custom Enumerator that skips nulls
    public Enumerator GetEnumerator() => new Enumerator(_values);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Pairs iterator only returns present items
    public IEnumerable<(ENUM Key, T Value)> Pairs() {
        for (int i = 0; i < _keys.Length; i++) {
            var val = _values[i];
            if (IsPresent(val)) {
                yield return (_keys[i], val);
            }
        }
    }

    // Count is O(N) because we must scan for non-nulls
    public int Count {
        get {
            int c = 0;
            foreach (var v in _values) if (IsPresent(v)) c++;
            return c;
        }
    }

    public bool IsReadOnly => false;

    T IList<T>.this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _values[index] = value;
    }

    T IReadOnlyList<T>.this[int index] {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _values[index];
    }

    public int IndexOf(T item) => Array.IndexOf(_values, item);
    public bool Contains(T item) => IsPresent(item) && IndexOf(item) >= 0;
    public void CopyTo(T[] array, int arrayIndex) {
        // Copy only present items? Or copy raw array?
        // ICollection.CopyTo usually copies elements. Since this looks like a collection of "present" items:
        int c = 0;
        foreach(var item in this) {
            array[arrayIndex + c++] = item;
        }
    }

    public void Insert(int index, T item) => throw new NotSupportedException("Fixed-size enum array");
    public void RemoveAt(int index) => throw new NotSupportedException("Fixed-size enum array");

    public bool Remove(T item) {
        int idx = IndexOf(item);
        if (idx >= 0) {
            _values[idx] = default!; // Reset to 0/null
            return true;
        }
        return false;
    }

    public void Clear() {
        _addIndex = 0;
        Array.Clear(_values, 0, _values.Length);
    }

    // IDictionary<ENUM, T> implementation
    // Keys/Values computed dynamically to skip nulls
    public ICollection<ENUM> Keys {
        get {
            var the = this;
            return _keys.Where(k => the.ContainsKey(k)).ToList();
        }
    }
    public ICollection<T> Values => _values.Where(v => IsPresent(v)).ToList();

    IEnumerable<ENUM> IReadOnlyDictionary<ENUM, T>.Keys => Keys;
    IEnumerable<T> IReadOnlyDictionary<ENUM, T>.Values => Values;

    public void Add(ENUM key, T value) => this[key] = value;

    public bool ContainsKey(ENUM key) {
        int idx = ToIndex(key);
        return idx < _count && IsPresent(_values[idx]);
    }

    public bool Remove(ENUM key) {
        int idx = ToIndex(key);
        if (idx < _count && IsPresent(_values[idx])) {
            _values[idx] = default!;
            return true;
        }
        return false;
    }

    public bool TryGetValue(ENUM key, out T value) {
        int idx = ToIndex(key);
        if (idx < _count) {
            var v = _values[idx];
            if (IsPresent(v)) {
                value = v;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Add(KeyValuePair<ENUM, T> item) => this[item.Key] = item.Value;

    public bool Contains(KeyValuePair<ENUM, T> item) {
        return ContainsKey(item.Key) && EqualityComparer<T>.Default.Equals(this[item.Key], item.Value);
    }

    public void CopyTo(KeyValuePair<ENUM, T>[] array, int arrayIndex) {
        var pairs = Pairs().ToArray();
        Array.Copy(pairs.Select(p => new KeyValuePair<ENUM, T>(p.Key, p.Value)).ToArray(), 0, array, arrayIndex, pairs.Length);
    }

    public bool Remove(KeyValuePair<ENUM, T> item) {
        if (Contains(item)) {
            return Remove(item.Key);
        }
        return false;
    }

    IEnumerator<KeyValuePair<ENUM, T>> IEnumerable<KeyValuePair<ENUM, T>>.GetEnumerator() {
        return Pairs().Select(p => new KeyValuePair<ENUM, T>(p.Key, p.Value)).GetEnumerator();
    }

    // Enumerator skips nulls
    public struct Enumerator : IEnumerator<T> {
        readonly T[] _items;
        int _index;

        internal Enumerator(T[] items) {
            _items = items;
            _index = -1;
        }

        public T Current => _items[_index];
        object IEnumerator.Current => Current!;

        public bool MoveNext() {
            while (++_index < _items.Length) {
                // Check against default using the default equality comparer
                if (!EqualityComparer<T>.Default.Equals(_items[_index], default)) return true;
            }
            return false;
        }
        public void Reset() => _index = -1;
        public void Dispose() { }
    }

    T[] _values;
    static ENUM[] _keys;

    [ThreadStatic]
    static int _addIndex;
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
