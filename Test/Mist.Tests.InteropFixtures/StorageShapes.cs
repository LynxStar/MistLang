using System.Runtime.CompilerServices;

namespace Mist.Tests.InteropFixtures;

// Methods keep the explicit private fields intact when auto-property formatting runs.
public struct PrimitiveStorage
{
    private int hidden;
    internal int internalValue;
    public int Visible;
    public static string Static = "static";
    public const int Constant = 1;

    public PrimitiveStorage(int value)
    {
        hidden = value;
        internalValue = value;
        Visible = value;
        Automatic = value;
    }

    public int Automatic { get; set; }

    public readonly int Sum()
    {
        return hidden + internalValue + Visible + Automatic;
    }
}

public struct HiddenReferenceStorage
{
    private string hidden;
    public int Visible;

    public HiddenReferenceStorage(string value)
    {
        hidden = value;
        Visible = value.Length;
    }

    public readonly string Read()
    {
        return hidden;
    }
}

public struct BackingReferenceStorage
{
    public string? Text { get; set; }
}

public struct GenericStorage<T>
{
    private T hidden;
    public T Visible;

    public GenericStorage(T value)
    {
        hidden = value;
        Visible = value;
    }

    public readonly T Read()
    {
        return hidden;
    }
}

public struct PrivateHelperStorage
{
    private Helper hidden;

    public PrivateHelperStorage(int value)
    {
        hidden = new Helper(value);
    }

    public readonly int Value => hidden.storedValue;

    private struct Helper(int value)
    {
        internal int storedValue = value;
    }
}

public struct PrivateReferenceHelperStorage
{
    private Helper hidden;

    public PrivateReferenceHelperStorage(string value)
    {
        hidden = new Helper(value);
    }

    public readonly string Value => hidden.storedValue;

    private struct Helper(string value)
    {
        internal string storedValue = value;
    }
}

public struct PrivateGenericHelperStorage<T>
{
    private Helper<T> hidden;

    public PrivateGenericHelperStorage(T value)
    {
        hidden = new Helper<T>(value);
    }

    public readonly T Value => hidden.storedValue;

    private struct Helper<TValue>(TValue value)
    {
        internal TValue storedValue = value;
    }
}

public unsafe struct PointerStorage
{
    private int* hidden;

    public PointerStorage(int* value)
    {
        hidden = value;
    }

    public readonly int Value => *hidden;
}

public ref struct ByRefStorage
{
    private ref int hidden;

    public ByRefStorage(ref int value)
    {
        hidden = ref value;
    }

    public readonly int Value => hidden;
}

[InlineArray(2)]
public struct InlineStorage
{
    public int First;
}

public unsafe struct FixedStorage
{
    public fixed int Values[2];
}