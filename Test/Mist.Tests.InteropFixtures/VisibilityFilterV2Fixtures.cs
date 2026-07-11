namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Type with methods that accept/return IntPtr and UIntPtr.
/// These should NOT be excluded by the visibility filter — they are regular struct types.
/// </summary>
public class HasIntPtrMembers
{
    public IntPtr GetHandle()
    {
        return IntPtr.Zero;
    }

    // IDE0060 suppressed: parameter exists to test visibility filter on IntPtr-typed signatures.
#pragma warning disable IDE0060
    public void SetHandle(IntPtr handle) { }
#pragma warning restore IDE0060

    public UIntPtr GetSize()
    {
        return UIntPtr.Zero;
    }

    // IDE0060 suppressed: parameter exists to test visibility filter on UIntPtr-typed signatures.
#pragma warning disable IDE0060
    public void SetSize(UIntPtr size) { }
#pragma warning restore IDE0060
}

/// <summary>
/// Internal class — should be excluded by IsTypeExcluded because it is not public.
/// </summary>
internal class InternalOnlyType
{
    public int Value { get; set; }
}

/// <summary>
/// Public class with a nested private class.
/// The nested class should be excluded; the outer class should not be.
/// </summary>
public class HasNestedPrivateType
{
    public int PublicValue { get; set; }

    private class PrivateNested
    {
        public int Secret { get; set; }
    }
}

/// <summary>
/// Public class with a nested internal class.
/// The nested class should be excluded; the outer class should not be.
/// </summary>
public class HasNestedInternalType
{
    public int PublicValue { get; set; }

    internal class InternalNested
    {
        public int Hidden { get; set; }
    }
}

/// <summary>
/// Public class with a nested public class.
/// Both should NOT be excluded.
/// </summary>
public class HasNestedPublicType
{
    public int PublicValue { get; set; }

    public class PublicNested
    {
        public int Visible { get; set; }
    }
}

/// <summary>
/// Public class whose method signature references a non-public nested type.
/// Used to test cascading exclusion: public methods referencing excluded types are excluded.
/// </summary>
public class HasCascadingExclusion
{
    // The nested type is internal — it is excluded by IsTypeExcluded.
    internal class InternalDependency
    {
        public int Value { get; set; }
    }

    // IDE0060 suppressed: parameter exists to test cascading exclusion through non-public types.
#pragma warning disable IDE0060
    internal void AcceptInternalDep(InternalDependency value) { }
#pragma warning restore IDE0060

    internal InternalDependency ReturnInternalDep()
    {
        return new InternalDependency();
    }

    /// <summary>A normal public method that should remain visible.</summary>
    public int SafeMethod()
    {
        return 1;
    }
}