namespace Mist.Tests.InteropFixtures;

/// <summary>
/// Fixture type with members at all six CLR accessibility levels.
/// Used by <c>ClrAccessibilityProjectionPropertyTests</c> to exercise
/// <c>ClrAccessibilityMap</c> against real reflection metadata.
/// </summary>
public class AllAccessibilityLevels
{
    // ─── Fields at all six CLR accessibility levels ───────────────────────

    public int PublicField = 1;

    // IDE0051 suppressed: field exists to test CLR accessibility projection via reflection.
#pragma warning disable IDE0051
    private int privateField = 2;
#pragma warning restore IDE0051

    protected int ProtectedField = 3;
    internal int internalField = 4;
    protected internal int ProtectedInternalField = 5;
    private protected int privateProtectedField = 6;

    // ─── Methods at all six CLR accessibility levels ─────────────────────

    public int PublicMethod()
    {
        return PublicField;
    }

    // IDE0051 suppressed: method exists to test CLR accessibility projection via reflection.
#pragma warning disable IDE0051
    private int PrivateMethod()
    {
        return privateField;
    }
#pragma warning restore IDE0051

    protected int ProtectedMethod()
    {
        return ProtectedField;
    }

    internal int InternalMethod()
    {
        return internalField;
    }

    protected internal int ProtectedInternalMethod()
    {
        return ProtectedInternalField;
    }

    private protected int PrivateProtectedMethod()
    {
        return privateProtectedField;
    }

    // ─── Properties at all six CLR accessibility levels ──────────────────

    public int PublicProp { get; set; }

    // IDE0051 suppressed: property exists to test CLR accessibility projection via reflection.
#pragma warning disable IDE0051
    private int PrivateProp { get; set; }
#pragma warning restore IDE0051

    protected int ProtectedProp { get; set; }
    internal int InternalProp { get; set; }
    protected internal int ProtectedInternalProp { get; set; }
    private protected int PrivateProtectedProp { get; set; }

    // ─── Nested types at all applicable accessibility levels ─────────────

    public class NestedPublic { }
    private class NestedPrivate { }
    protected class NestedProtected { }
    internal class NestedInternal { }
    protected internal class NestedProtectedInternal { }
    private protected class NestedPrivateProtected { }
}