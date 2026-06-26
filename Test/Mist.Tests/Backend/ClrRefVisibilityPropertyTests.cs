// Feature: clr-ref-interop, Property 1: Ref and in parameters pass through the visibility filter
// Feature: clr-ref-interop, Property 2: Out parameters pass through the visibility filter
// Feature: clr-ref-interop, Property 5: Mutable ref returns pass through the visibility filter
// Feature: clr-ref-interop, Property 6: Ref readonly returns remain filtered
//
// **Validates: Requirements 1.1, 1.2, 1.3, 3.1, 4.1, 4.2, 4.3, 6.1, 6.2, 6.3**

using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for the VisibilityFilter's handling of CLR ref/in/out parameters
/// and ref/ref-readonly returns.
///
/// Uses the real fixture assembly types as input — no mocked metadata.
/// Tests operate entirely in-process using VisibilityFilter directly (no process spawning).
/// </summary>
public sealed class ClrRefVisibilityPropertyTests
{
    private static readonly VisibilityFilter Filter = new();

    // ─── Pre-computed member sets from fixture types ─────────────────────

    /// <summary>
    /// All public methods from RefParamFixture and InParamFixture that have
    /// ref or in parameters (and no out parameters). These should pass through the filter.
    /// </summary>
    private static readonly MethodInfo[] RefAndInParamMethods = BuildRefAndInParamMethods();

    /// <summary>
    /// All public methods from OutParamFixture and MixedRefOutFixture that have
    /// at least one out parameter. These should now pass through the filter.
    /// </summary>
    private static readonly MethodInfo[] OutParamMethods = BuildOutParamMethods();

    /// <summary>
    /// All public methods from MutableRefReturnFixture that have mutable ref returns
    /// (IsByRef without IsReadOnlyAttribute). These should pass through the filter.
    /// </summary>
    private static readonly MethodInfo[] MutableRefReturnMethods = BuildMutableRefReturnMethods();

    /// <summary>
    /// All public methods from RefReadonlyReturnFixture that have ref readonly returns
    /// (IsByRef with IsReadOnlyAttribute). These should remain filtered.
    /// </summary>
    private static readonly MethodInfo[] RefReadonlyReturnMethods = BuildRefReadonlyReturnMethods();

    // ─── Builder methods ─────────────────────────────────────────────────

    private static MethodInfo[] BuildRefAndInParamMethods()
    {
        var result = new List<MethodInfo>();

        foreach(var type in new[] { typeof(RefParamFixture), typeof(InParamFixture) })
        {
            foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if(method.IsSpecialName)
                    continue;

                var parameters = method.GetParameters();
                var hasRefOrIn = parameters.Any(p => p.ParameterType.IsByRef && !p.IsOut);
                var hasOut = parameters.Any(p => p.IsOut);

                if(hasRefOrIn && !hasOut)
                    result.Add(method);
            }
        }

        return result.ToArray();
    }

    private static MethodInfo[] BuildOutParamMethods()
    {
        var result = new List<MethodInfo>();

        foreach(var type in new[] { typeof(OutParamFixture), typeof(MixedRefOutFixture) })
        {
            foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if(method.IsSpecialName)
                    continue;

                var parameters = method.GetParameters();
                if(parameters.Any(p => p.IsOut))
                    result.Add(method);
            }
        }

        return result.ToArray();
    }

    private static MethodInfo[] BuildMutableRefReturnMethods()
    {
        var result = new List<MethodInfo>();

        foreach(var method in typeof(MutableRefReturnFixture).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if(method.IsSpecialName)
                continue;

            if(method.ReturnType.IsByRef && !IsRefReadonlyReturn(method))
                result.Add(method);
        }

        return result.ToArray();
    }

    private static MethodInfo[] BuildRefReadonlyReturnMethods()
    {
        var result = new List<MethodInfo>();

        foreach(var method in typeof(RefReadonlyReturnFixture).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if(method.IsSpecialName)
                continue;

            if(method.ReturnType.IsByRef && IsRefReadonlyReturn(method))
                result.Add(method);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Checks whether a method has a ref readonly return by looking for IsReadOnlyAttribute
    /// on the return parameter.
    /// </summary>
    private static bool IsRefReadonlyReturn(MethodInfo method)
    {
        return method.ReturnParameter
            .GetCustomAttributesData()
            .Any(a => a.AttributeType.FullName ==
                "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    // ─── Helper ──────────────────────────────────────────────────────────

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ─── Property 1: Ref and in parameters pass through the visibility filter ────

    /// <summary>
    /// Property 1: For any CLR method whose parameters include ref or in params
    /// (and no out params), VisibilityFilter.IsMemberExcluded SHALL return false.
    ///
    /// **Validates: Requirements 1.1, 1.2, 3.1**
    /// </summary>
    [Fact]
    public void Property1_RefAndInParams_PassThroughVisibilityFilter()
    {
        Assert.NotEmpty(RefAndInParamMethods);

        RunProperty(Gen.Choose(0, RefAndInParamMethods.Length - 1), index =>
        {
            var method = RefAndInParamMethods[index];
            var excluded = Filter.IsMemberExcluded(method);

            Assert.False(excluded,
                $"Method {method.DeclaringType!.Name}.{method.Name} has ref/in params " +
                $"(no out) but was excluded by the visibility filter.");
        });
    }

    // ─── Property 2: Out parameters pass through the visibility filter ─────────

    /// <summary>
    /// Property 2: For any CLR method that has at least one out parameter,
    /// VisibilityFilter.IsMemberExcluded SHALL return false (out params are now
    /// projected as ref with ClrCallingConvention.Out).
    ///
    /// **Validates: Requirements 1.3, 6.1, 6.2**
    /// </summary>
    [Fact]
    public void Property2_OutParams_PassThroughVisibilityFilter()
    {
        Assert.NotEmpty(OutParamMethods);

        RunProperty(Gen.Choose(0, OutParamMethods.Length - 1), index =>
        {
            var method = OutParamMethods[index];
            var excluded = Filter.IsMemberExcluded(method);

            Assert.False(excluded,
                $"Method {method.DeclaringType!.Name}.{method.Name} has out params " +
                $"but was excluded by the visibility filter.");
        });
    }

    // ─── Property 5: Mutable ref returns pass through the visibility filter ──────

    /// <summary>
    /// Property 5: For any CLR method with a mutable ref return (IsByRef without
    /// IsReadOnlyAttribute), VisibilityFilter.IsMemberExcluded SHALL return false.
    ///
    /// **Validates: Requirements 4.1, 4.2**
    /// </summary>
    [Fact]
    public void Property5_MutableRefReturns_PassThroughVisibilityFilter()
    {
        Assert.NotEmpty(MutableRefReturnMethods);

        RunProperty(Gen.Choose(0, MutableRefReturnMethods.Length - 1), index =>
        {
            var method = MutableRefReturnMethods[index];
            var excluded = Filter.IsMemberExcluded(method);

            Assert.False(excluded,
                $"Method {method.DeclaringType!.Name}.{method.Name} has a mutable ref return " +
                $"but was excluded by the visibility filter.");
        });
    }

    // ─── Property 6: Ref readonly returns remain filtered ────────────────────────

    /// <summary>
    /// Property 6: For any CLR method with a ref readonly return (IsByRef with
    /// IsReadOnlyAttribute), VisibilityFilter.IsMemberExcluded SHALL return true.
    ///
    /// **Validates: Requirements 4.3, 6.3**
    /// </summary>
    [Fact]
    public void Property6_RefReadonlyReturns_RemainFiltered()
    {
        Assert.NotEmpty(RefReadonlyReturnMethods);

        RunProperty(Gen.Choose(0, RefReadonlyReturnMethods.Length - 1), index =>
        {
            var method = RefReadonlyReturnMethods[index];
            var excluded = Filter.IsMemberExcluded(method);

            Assert.True(excluded,
                $"Method {method.DeclaringType!.Name}.{method.Name} has a ref readonly return " +
                $"but was NOT excluded by the visibility filter.");
        });
    }
}