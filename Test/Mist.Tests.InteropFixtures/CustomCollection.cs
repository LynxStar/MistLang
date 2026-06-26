using System.Collections;
using System.Collections.Generic;

namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A custom collection implementing IEnumerable&lt;int&gt; with a concrete enumerator type.
/// Used for testing IEnumerable/IEnumerator interop projection.
/// </summary>
public class CustomCollection : IEnumerable<int>
{
    private readonly int[] _items;

    public CustomCollection(params int[] items) => _items = items;

    public int Count => _items.Length;

    public Enumerator GetEnumerator() => new(_items);

    IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Concrete enumerator struct for CustomCollection.
    /// </summary>
    public struct Enumerator : IEnumerator<int>
    {
        private readonly int[] _items;
        private int _index;

        internal Enumerator(int[] items)
        {
            _items = items;
            _index = -1;
        }

        public int Current => _items[_index];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _index++;
            return _index < _items.Length;
        }

        public void Reset() => _index = -1;

        public void Dispose() { }
    }
}