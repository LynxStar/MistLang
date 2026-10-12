namespace Mist.Tests.InteropFixtures;

// The shapes List<T> and Dictionary<TKey, TValue> show a projection -- an interface chain, a nested type
// carrying the enclosing parameter, an explicit interface member beside a public one -- declared here so a
// claim about them projects this assembly alone rather than the runtime's core library.
public interface IReadableBag<T>
{
    int Count { get; }
}

public interface IBag<T> : IReadableBag<T>
{
    void Add(T item);

    void Add(T item, int copies);
}

public interface IKeyed
{
    int this[string key] { get; }
}

public class Bag<T> : IBag<T>, IEnumerable<T>, IKeyed
{
    private readonly List<T> items = [];

    public int Count => items.Count;

    public T this[int index]
    {
        get => items[index];
        set => items[index] = value;
    }

    int IKeyed.this[string key] => key.Length;

    public void Add(T item)
    {
        items.Add(item);
    }

    void IBag<T>.Add(T item, int copies)
    {
        for(var i = 0; i < copies; i++)
        {
            items.Add(item);
        }
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(items);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return items.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return items.GetEnumerator();
    }

    public struct Enumerator
    {
        private readonly List<T> items;
        private int position;

        internal Enumerator(List<T> items)
        {
            this.items = items;
            position = -1;
        }

        public T Current => items[position];

        public bool MoveNext()
        {
            position++;
            return position < items.Count;
        }
    }
}

public class Table<TKey, TValue> : IKeyed
    where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> entries = [];

    public TValue this[TKey key]
    {
        get => entries[key];
        set => entries[key] = value;
    }

    int IKeyed.this[string key] => key.Length;

    public void Add(TKey key, TValue value)
    {
        entries.Add(key, value);
    }
}