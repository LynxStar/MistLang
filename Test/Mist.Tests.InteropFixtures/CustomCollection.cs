using System.Collections;

namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A custom collection implementing IEnumerable&lt;int&gt; with a concrete enumerator type.
/// Used for testing IEnumerable/IEnumerator interop projection.
/// </summary>
public class CustomCollection : IEnumerable<int>
{
    private readonly int[] items;

    public CustomCollection(params int[] items) => this.items = items;

    public int Count => items.Length;

    public Enumerator GetEnumerator()
    {
        return new(items);
    }

    IEnumerator<int> IEnumerable<int>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Concrete enumerator struct for CustomCollection.
    /// </summary>
    public struct Enumerator : IEnumerator<int>
    {
        private readonly int[] items;
        private int index;

        internal Enumerator(int[] items)
        {
            this.items = items;
            index = -1;
        }

        public int Current => items[index];

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            index++;
            return index < items.Length;
        }

        public void Reset()
        {
            index = -1;
        }

        public void Dispose() { }
    }
}