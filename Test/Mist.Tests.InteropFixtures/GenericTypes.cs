namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A generic class with a class constraint for testing generic projection.
/// </summary>
public class GenericClass<T> where T : class
{
    public T? Item { get; set; }

    public GenericClass() { }
    public GenericClass(T item) => Item = item;

    public T? GetItem() => Item;
    public void SetItem(T item) => Item = item;
    public bool HasItem() => Item is not null;
}

/// <summary>
/// A generic class with multiple constraints for testing constraint translation.
/// </summary>
public class MultiConstraint<T> where T : struct, IComparable<T>
{
    public T Value { get; }

    public MultiConstraint(T value) => Value = value;

    public int CompareTo(T other) => Value.CompareTo(other);
    public bool IsGreaterThan(T other) => Value.CompareTo(other) > 0;
}
