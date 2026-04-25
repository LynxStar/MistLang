using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Mist.Common.Collections;

/// <summary>
/// A deterministic, immutable ordered dictionary that preserves insertion order.
/// </summary>
public sealed class ImmutableOrderedMap<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private readonly ImmutableArray<KeyValuePair<TKey, TValue>> _entries;
    private readonly ImmutableDictionary<TKey, TValue> _lookup;

    private ImmutableOrderedMap(
        ImmutableArray<KeyValuePair<TKey, TValue>> entries,
        ImmutableDictionary<TKey, TValue> lookup)
    {
        _entries = entries;
        _lookup = lookup;
    }

    public static readonly ImmutableOrderedMap<TKey, TValue> Empty = new(
        ImmutableArray<KeyValuePair<TKey, TValue>>.Empty,
        ImmutableDictionary<TKey, TValue>.Empty);

    public int Count => _entries.Length;

    public IEnumerable<TKey> Keys => _entries.Select(e => e.Key);

    public IEnumerable<TValue> Values => _entries.Select(e => e.Value);

    public TValue this[TKey key] => _lookup[key];

    public bool ContainsKey(TKey key) => _lookup.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        => _lookup.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        => ((IEnumerable<KeyValuePair<TKey, TValue>>)_entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns a new map with the given key-value pair appended.
    /// Throws if the key already exists.
    /// </summary>
    public ImmutableOrderedMap<TKey, TValue> Add(TKey key, TValue value)
    {
        return new ImmutableOrderedMap<TKey, TValue>(
            _entries.Add(new KeyValuePair<TKey, TValue>(key, value)),
            _lookup.Add(key, value));
    }

    /// <summary>
    /// Builder for constructing an <see cref="ImmutableOrderedMap{TKey, TValue}"/> incrementally.
    /// </summary>
    public sealed class Builder
    {
        private readonly List<KeyValuePair<TKey, TValue>> _entries = [];
        private readonly Dictionary<TKey, TValue> _lookup = [];

        public void Add(TKey key, TValue value)
        {
            _lookup.Add(key, value); // throws on duplicate
            _entries.Add(new KeyValuePair<TKey, TValue>(key, value));
        }

        public bool ContainsKey(TKey key) => _lookup.ContainsKey(key);

        public ImmutableOrderedMap<TKey, TValue> ToImmutable()
        {
            return new ImmutableOrderedMap<TKey, TValue>(
                [.. _entries],
                _lookup.ToImmutableDictionary());
        }
    }
}
