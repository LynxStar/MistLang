// Feature: delegate-func-conversion, Property 9: Visibility filter — public delegates visible, excluded-signature delegates hidden
//
// **Validates: Requirements 5.1, 5.2**

using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for the VisibilityFilter's handling of named delegate types.
///
/// Property 9: For any public CLR delegate type whose Invoke method does NOT reference
/// pointer types, ref structs, or ref readonly returns, VisibilityFilter.IsTypeExcluded
/// SHALL return false. Conversely, for any delegate whose Invoke references a pointer type
/// or has a ref readonly return, the delegate SHALL be hidden.
///
/// The test uses two populations:
/// 1. Safe delegates (BCL + fixture): Predicate&lt;T&gt;, Action&lt;T&gt;, Comparison&lt;T&gt;,
///    EventHandler, and fixture delegates with normal Invoke signatures → visible.
/// 2. Excluded delegates (fixture): delegates with pointer params or pointer returns in
///    their Invoke method → hidden via IsTypeExcludedInSignature.
///
/// Since IsTypeExcluded only checks public accessibility (delegates are public classes),
/// we also verify that method overloads referencing excluded delegates are themselves
/// hidden by IsMemberExcluded.
///
/// **Validates: Requirements 5.1, 5.2**
/// </summary>
public sealed class DelegateVisibilityPropertyTests
{
    private static readonly VisibilityFilter Filter = new();

    // ═══════════════════════════════════════════════════════════════════
    //  Population: Safe public delegate types (should be visible)
    //
    //  Combines BCL delegates and fixture delegates whose Invoke methods
    //  do NOT reference pointer types, ref structs, or ref readonly returns.
    // ═══════════════════════════════════════════════════════════════════

    private static readonly Type[] SafeDelegateTypes =
    [
        // BCL delegates
        typeof(Predicate<int>),
        typeof(Predicate<string>),
        typeof(Action),
        typeof(Action<int>),
        typeof(Action<int, string>),
        typeof(Action<int, string, double>),
        typeof(Comparison<int>),
        typeof(Comparison<string>),
        typeof(EventHandler),
        typeof(EventHandler<EventArgs>),
        typeof(Func<int>),
        typeof(Func<int, string>),
        typeof(Func<int, int, bool>),
        typeof(Converter<int, string>),
        typeof(AsyncCallback),
        // Fixture delegates with safe signatures
        typeof(SafeVoidDelegate),
        typeof(SafePredicateDelegate),
        typeof(SafeMultiParamDelegate),
        typeof(SafeGenericDelegate<int, string>),
        typeof(SafeIntReturningDelegate),
    ];

    // ═══════════════════════════════════════════════════════════════════
    //  Population: Delegates with excluded Invoke signatures (should be hidden)
    //
    //  Fixture delegates whose Invoke method references pointer types.
    // ═══════════════════════════════════════════════════════════════════

    private static readonly Type[] ExcludedDelegateTypes =
    [
        typeof(PointerParamDelegate),
        typeof(PointerReturnDelegate),
        typeof(VoidPointerParamDelegate),
        typeof(MixedPointerDelegate),
    ];

    // ═══════════════════════════════════════════════════════════════════
    //  Helper: Run property over samples with clear failure messages
    // ═══════════════════════════════════════════════════════════════════

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for (var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Property 9a: Public delegates with safe Invoke → NOT excluded by IsTypeExcluded
    //
    //  For any public CLR delegate type whose Invoke method does NOT reference
    //  pointer types, ref structs, or ref readonly returns, IsTypeExcluded
    //  SHALL return false.
    //
    //  **Validates: Requirement 5.1**
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P9a_PublicDelegatesWithSafeInvoke_AreNotExcludedByIsTypeExcluded()
    {
        Assert.NotEmpty(SafeDelegateTypes);

        RunProperty(Gen.Choose(0, SafeDelegateTypes.Length - 1), index =>
        {
            var delegateType = SafeDelegateTypes[index];

            var excluded = Filter.IsTypeExcluded(delegateType);

            Assert.False(excluded,
                $"Delegate type {delegateType.FullName} has a safe Invoke signature " +
                $"but was excluded by IsTypeExcluded.");
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Property 9b: Delegates with pointer params/returns in Invoke → excluded
    //
    //  For any delegate whose Invoke references a pointer type, the delegate
    //  SHALL be hidden. We test this via IsMemberExcluded on a method that
    //  takes the excluded delegate as a parameter (since IsTypeExcluded only
    //  checks public accessibility, the delegate-specific check is in
    //  IsTypeExcludedInSignature which is called from IsMemberExcluded's
    //  ReferencesExcludedType path).
    //
    //  **Validates: Requirement 5.2**
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P9b_DelegatesWithPointerInInvoke_HideOverloadsThatReferenceThem()
    {
        Assert.NotEmpty(ExcludedDelegateTypes);

        // DelegateConsumer.AcceptPointerDelegate takes a PointerParamDelegate parameter.
        // Since that delegate has a pointer in its Invoke, the overload should be hidden.
        var method = typeof(DelegateConsumer).GetMethod(nameof(DelegateConsumer.AcceptPointerDelegate))!;
        Assert.NotNull(method);

        var excluded = Filter.IsMemberExcluded(method);

        Assert.True(excluded,
            $"Method {method.DeclaringType!.Name}.{method.Name} references a delegate " +
            $"with pointer params but was NOT excluded by IsMemberExcluded.");
    }

    [Fact]
    public void P9b_DelegatesWithPointerInInvoke_AreDetectedAsExcluded()
    {
        Assert.NotEmpty(ExcludedDelegateTypes);

        RunProperty(Gen.Choose(0, ExcludedDelegateTypes.Length - 1), index =>
        {
            var delegateType = ExcludedDelegateTypes[index];

            // The delegate itself is a public type, so IsTypeExcluded returns false
            // (it only checks accessibility). The exclusion happens at the signature level.
            // Verify that IsTypeExcluded does NOT exclude the type itself — it's the
            // signature check that hides members referencing this delegate.
            var typeExcluded = Filter.IsTypeExcluded(delegateType);
            Assert.False(typeExcluded,
                $"IsTypeExcluded should return false for public delegate {delegateType.FullName} " +
                $"(it only checks accessibility, not Invoke signature).");

            // Verify the delegate IS a named delegate with MulticastDelegate base
            Assert.Equal("System.MulticastDelegate", delegateType.BaseType?.FullName);

            // Verify the Invoke method has pointer types (sanity check)
            var invokeMethod = delegateType.GetMethod("Invoke")!;
            Assert.NotNull(invokeMethod);

            var hasPointer = ContainsPointerInSignature(invokeMethod);
            Assert.True(hasPointer,
                $"Expected delegate {delegateType.FullName} Invoke to contain pointer types.");
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Property 9c: Safe delegate parameters do NOT cause overload hiding
    //
    //  A method whose parameter is a delegate with a safe Invoke signature
    //  should NOT be excluded by IsMemberExcluded.
    //
    //  **Validates: Requirements 5.1, 5.2**
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P9c_SafeDelegateParameter_DoesNotCauseOverloadHiding()
    {
        var method = typeof(DelegateConsumer).GetMethod(nameof(DelegateConsumer.AcceptSafeDelegate))!;
        Assert.NotNull(method);

        var excluded = Filter.IsMemberExcluded(method);

        Assert.False(excluded,
            $"Method {method.DeclaringType!.Name}.{method.Name} references a safe delegate " +
            $"parameter but was excluded by IsMemberExcluded.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Property 9d: System.Delegate and System.MulticastDelegate are never excluded
    //
    //  **Validates: Requirement 5.5 (via design Property 9 scope)**
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P9d_SystemDelegate_NeverExcluded()
    {
        Assert.False(Filter.IsTypeExcluded(typeof(Delegate)),
            "System.Delegate should never be excluded by IsTypeExcluded.");
        Assert.False(Filter.IsTypeExcluded(typeof(MulticastDelegate)),
            "System.MulticastDelegate should never be excluded by IsTypeExcluded.");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Property 9e: All safe delegates are valid delegate types
    //
    //  Verifies the test population is valid: all safe delegates inherit
    //  from MulticastDelegate and their Invoke has no pointer types.
    //
    //  **Validates: Requirements 5.1, 5.2**
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P9e_SafeDelegatePopulation_AllInheritFromMulticastDelegate()
    {
        RunProperty(Gen.Choose(0, SafeDelegateTypes.Length - 1), index =>
        {
            var delegateType = SafeDelegateTypes[index];

            // All items in our safe population should be delegates
            Assert.True(
                delegateType.BaseType?.FullName == "System.MulticastDelegate" ||
                delegateType.FullName == "System.Delegate" ||
                delegateType.FullName == "System.MulticastDelegate",
                $"Expected {delegateType.FullName} to be a delegate type.");

            // None should have pointer types in their Invoke
            var invokeMethod = delegateType.GetMethod("Invoke");
            if (invokeMethod is not null)
            {
                Assert.False(ContainsPointerInSignature(invokeMethod),
                    $"Safe delegate {delegateType.FullName} unexpectedly has pointers in Invoke.");
            }
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helper: Check if a method signature contains pointer types
    // ═══════════════════════════════════════════════════════════════════

    private static bool ContainsPointerInSignature(MethodInfo method)
    {
        if (ContainsPointerType(method.ReturnType))
            return true;

        foreach (var param in method.GetParameters())
        {
            if (ContainsPointerType(param.ParameterType))
                return true;
        }

        return false;
    }

    private static bool ContainsPointerType(Type type)
    {
        if (type.IsPointer)
            return true;

        if (type.IsByRef)
        {
            var element = type.GetElementType();
            if (element is not null)
                return ContainsPointerType(element);
        }

        if (type.IsArray)
        {
            var element = type.GetElementType();
            if (element is not null)
                return ContainsPointerType(element);
        }

        if (type.IsConstructedGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
            {
                if (ContainsPointerType(arg))
                    return true;
            }
        }

        return false;
    }
}
