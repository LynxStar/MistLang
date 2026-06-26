namespace Mist.Tests.InteropFixtures;

/// <summary>
/// A simple non-generic ref struct fixture for TypeProjector tests.
/// Verifies that arbitrary IsByRefLike types (not System.Span/ReadOnlySpan)
/// project to StructType{IsRefStruct=true}.
/// </summary>
public ref struct RefStructType
{
    public int Value;

    public RefStructType(int value)
    {
        Value = value;
    }

    public int GetValue()
    {
        return Value;
    }
}

/// <summary>
/// A generic ref struct fixture — verifies that generic IsByRefLike types
/// (other than System.Span/ReadOnlySpan) project correctly as ref structs.
/// </summary>
public ref struct GenericRefStruct<T>
{
    public T Item;

    public GenericRefStruct(T item)
    {
        Item = item;
    }
}

/// <summary>
/// A ref struct with multiple fields — verifies that field count does not
/// affect the IsByRefLike → IsRefStruct projection path.
/// </summary>
public ref struct MultiFieldRefStruct
{
    public int X;
    public int Y;
    public float Z;

    public MultiFieldRefStruct(int x, int y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}