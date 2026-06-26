using Mist.Backends.CSharpTranspiler;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="VisibilityFilter"/> delegate handling.
/// Validates Requirements 5.1, 5.2, 5.5, 5.6.
///
/// These tests verify:
/// - Public delegates with safe Invoke signatures are not hidden (5.1)
/// - Delegates with pointer params in Invoke hide referencing overloads (5.2)
/// - System.Delegate is never excluded (5.5)
/// - Per-overload filtering: overloads referencing hidden delegates are hidden,
///   sibling overloads with safe delegates survive (5.6)
/// </summary>
public sealed class DelegateVisibilityUnitTests
{
    private readonly VisibilityFilter _filter = new();

    // ═══════════════════════════════════════════════════════════════════
    //  Requirement 5.1: Public delegate without excluded types → not hidden
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsTypeExcluded_SafePredicateDelegate_ReturnsFalse()
    {
        var result = _filter.IsTypeExcluded(typeof(SafePredicateDelegate));

        Assert.False(result,
            "SafePredicateDelegate has a safe Invoke signature (int → bool) " +
            "and should not be excluded.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Requirement 5.2: Delegate with pointer param in Invoke → hidden
    //
    //  The delegate type itself is public (IsTypeExcluded returns false for
    //  accessibility), but IsMemberExcluded hides methods that reference it
    //  because IsTypeExcludedInSignature detects the pointer in Invoke.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsMemberExcluded_MethodWithPointerDelegate_ReturnsTrue()
    {
        var method = typeof(DelegateConsumer)
            .GetMethod(nameof(DelegateConsumer.AcceptPointerDelegate))!;

        var result = _filter.IsMemberExcluded(method);

        Assert.True(result,
            "AcceptPointerDelegate takes PointerParamDelegate whose Invoke has a pointer param; " +
            "the overload should be hidden by IsMemberExcluded.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Requirement 5.5: System.Delegate → never excluded
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsTypeExcluded_SystemDelegate_ReturnsFalse()
    {
        var result = _filter.IsTypeExcluded(typeof(Delegate));

        Assert.False(result,
            "System.Delegate is the delegate hierarchy root and must never be excluded.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Requirements 5.2, 5.6: Method overload referencing hidden delegate →
    //  overload hidden, sibling overloads survive
    //
    //  DelegateConsumer has two overloads:
    //    AcceptSafeDelegate(SafePredicateDelegate)   → safe, visible
    //    AcceptPointerDelegate(PointerParamDelegate)  → excluded, hidden
    //
    //  This verifies per-overload filtering: the unsafe overload is hidden
    //  while the safe sibling survives.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsMemberExcluded_SiblingOverload_SafeDelegateSurvives()
    {
        var safeMethod = typeof(DelegateConsumer)
            .GetMethod(nameof(DelegateConsumer.AcceptSafeDelegate))!;
        var pointerMethod = typeof(DelegateConsumer)
            .GetMethod(nameof(DelegateConsumer.AcceptPointerDelegate))!;

        var safeExcluded = _filter.IsMemberExcluded(safeMethod);
        var pointerExcluded = _filter.IsMemberExcluded(pointerMethod);

        Assert.False(safeExcluded,
            "AcceptSafeDelegate references SafePredicateDelegate (safe Invoke) " +
            "and must NOT be hidden.");
        Assert.True(pointerExcluded,
            "AcceptPointerDelegate references PointerParamDelegate (pointer in Invoke) " +
            "and must be hidden.");
    }
}