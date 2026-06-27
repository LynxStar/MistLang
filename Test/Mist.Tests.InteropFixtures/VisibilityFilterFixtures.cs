namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Type with a public event — events should be hidden from projection.
/// </summary>
public class HasEvent
{
    public event EventHandler? SomethingHappened;

    public void RaiseEvent()
    {
        SomethingHappened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A normal method that should remain visible.</summary>
    public int GetValue()
    {
        return 42;
    }
}

/// <summary>
/// Type with unsafe pointer members — should be excluded by VisibilityFilter.
/// </summary>
public unsafe class HasPointerMembers
{
    public int* GetPointer(int* source)
    {
        return source;
    }

    // IDE0060 suppressed: parameter exists to test visibility filter on pointer-typed signatures.
#pragma warning disable IDE0060
    public void ProcessPointer(int* data) { }
#pragma warning restore IDE0060

    /// <summary>A normal method that should remain visible.</summary>
    public int SafeMethod()
    {
        return 1;
    }
}

/// <summary>
/// Type with a ref return — should be excluded by VisibilityFilter.
/// </summary>
public class HasRefReturn
{
    private int value = 42;

    public ref int GetRef()
    {
        return ref value;
    }

    /// <summary>A normal method that should remain visible.</summary>
    public int GetValue()
    {
        return value;
    }
}

/// <summary>
/// Type with a ref-returning property — should be excluded by VisibilityFilter.
/// </summary>
public class HasRefReturnProperty
{
    private int backing = 10;

    public ref int RefProp => ref backing;

    /// <summary>A normal property that should remain visible.</summary>
    public int NormalProp => backing;
}

/// <summary>
/// Type with non-public members — only public members should be projected.
/// </summary>
public class HasNonPublicMembers
{
    public int PublicField = 1;
    internal int internalField = 2;
    protected int ProtectedField = 3;

    public int PublicMethod()
    {
        return PublicField;
    }

    internal int InternalMethod()
    {
        return internalField;
    }

    protected int ProtectedMethod()
    {
        return ProtectedField;
    }

    public int PublicProp { get; set; }
    internal int InternalProp { get; set; }
    protected int ProtectedProp { get; set; }
}

/// <summary>
/// Type whose method signature references a ref struct (excluded type).
/// The method should be excluded because its parameter type is excluded.
/// </summary>
public class ReferencesExcludedType
{
    // IDE0060 suppressed: parameter exists to test visibility filter on ref-struct-typed signatures.
#pragma warning disable IDE0060
    public void AcceptRefStruct(RefStructType value) { }
#pragma warning restore IDE0060

    public RefStructType ReturnRefStruct()
    {
        return new(42);
    }

    /// <summary>A normal method that should remain visible.</summary>
    public int SafeMethod()
    {
        return 1;
    }
}