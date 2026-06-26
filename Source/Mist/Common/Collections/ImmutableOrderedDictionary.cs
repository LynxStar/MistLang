using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Mist.Common.Collections;

/// <summary>
/// A deterministic, immutable ordered dictionary that preserves insertion order.
/// </summary>
public sealed class ImmutableOrderedDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
    where TKey : notnull
{
    private ImmutableArray<KeyValuePair<TKey, TValue>> entries;
    private ImmutableDictionary<TKey, TValue> lookup;

    private ImmutableOrderedDictionary(
        ImmutableArray<KeyValuePair<TKey, TValue>> entries,
        ImmutableDictionary<TKey, TValue> lookup)
    {
        this.entries = entries;
        this.lookup = lookup;
    }

    public static ImmutableOrderedDictionary<TKey, TValue> Empty { get; } = new(
        ImmutableArray<KeyValuePair<TKey, TValue>>.Empty,
        ImmutableDictionary<TKey, TValue>.Empty);

    public int Count => entries.Length;

    public IEnumerable<TKey> Keys => entries.Select(e => e.Key);

    public IEnumerable<TValue> Values => entries.Select(e => e.Value);

    public TValue this[TKey key] => lookup[key];

    public bool ContainsKey(TKey key) => lookup.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        => lookup.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        => ((IEnumerable<KeyValuePair<TKey, TValue>>)entries).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns a new map with the given key-value pair appended.
    /// Throws if the key already exists.
    /// </summary>
    public ImmutableOrderedDictionary<TKey, TValue> Add(TKey key, TValue value)
    {
        return new ImmutableOrderedDictionary<TKey, TValue>(
            entries.Add(new KeyValuePair<TKey, TValue>(key, value)),
            lookup.Add(key, value));
    }

    /// <summary>
    /// Builder for constructing an <see cref="ImmutableOrderedDictionary{TKey, TValue}"/> incrementally.
    /// </summary>
    public sealed class Builder
    {
        private List<KeyValuePair<TKey, TValue>> entries = [];
        private Dictionary<TKey, TValue> lookup = [];

        public void Add(TKey key, TValue value)
        {
            lookup.Add(key, value); // throws on duplicate
            entries.Add(new KeyValuePair<TKey, TValue>(key, value));
        }

        public bool ContainsKey(TKey key) => lookup.ContainsKey(key);

        public ImmutableOrderedDictionary<TKey, TValue> ToImmutable()
        {
            return new ImmutableOrderedDictionary<TKey, TValue>(
                [.. entries],
                lookup.ToImmutableDictionary());
        }
    }
}