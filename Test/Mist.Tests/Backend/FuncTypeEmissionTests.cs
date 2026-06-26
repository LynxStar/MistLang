// Feature: first-class-functions, Property 7: C# Emission Correctness
// **Validates: Requirements 14.1, 14.2, 14.3**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Unit tests for <see cref="CSharpTypeMap"/> Func type emission mapping
/// (first-class-functions Task 13.4).
///
/// Validates that <c>Standard.Func&lt;...&gt;</c> constructed types emit the
/// correct C# delegate type name:
/// <list type="bullet">
///   <item><c>func&lt;T1, ..., TN, TResult&gt;</c> (TResult ≠ void) → <c>System.Func&lt;T1, ..., TN, TResult&gt;</c></item>
///   <item><c>func&lt;T1, ..., TN, void&gt;</c> → <c>System.Action&lt;T1, ..., TN-1&gt;</c></item>
///   <item><c>func&lt;void&gt;</c> → <c>System.Action</c> (non-generic)</item>
/// </list>
///
/// Property 7: C# Emission Correctness.
/// Validates: Requirements 14.1, 14.2, 14.3.
///
/// All tests run fully in-process via <see cref="CSharpTypeMap.GetTypeName"/>;
/// no <c>dotnet</c> process is spawned.
/// </summary>
[Trait("Feature", "first-class-functions")]
[Trait("Property", "7: C# Emission Correctness")]
public sealed class FuncTypeEmissionTests
{
    // ── Helper factories ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a stdlib Backend_Backed <c>Standard.Func</c> ClassType with the given
    /// number of type parameters (arity = parameters + return type).
    /// </summary>
    private static ClassType MakeStdlibFunc(int arity)
    {
        var func = new ClassType("Func", new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);
        var typeParams = new IType[arity];
        for(var i = 0; i < arity; i++)
        {
            typeParams[i] = new TypeParameterType($"T{i}", i);
        }

        func.TypeParameters = typeParams;
        return func;
    }

    /// <summary>Creates a stdlib Backend_Backed <c>Standard.Void</c> struct.</summary>
    private static StructType MakeVoid() =>
        new("Void", new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);

    /// <summary>Creates a stdlib Backend_Backed <c>Standard.Int32</c> struct.</summary>
    private static StructType MakeInt() =>
        new("Int32", new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);

    /// <summary>Creates a stdlib Backend_Backed <c>Standard.String</c> class.</summary>
    private static ClassType MakeString() =>
        new("String", new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);

    /// <summary>Creates a stdlib Backend_Backed <c>Standard.Boolean</c> struct.</summary>
    private static StructType MakeBool() =>
        new("Boolean", new Scope(parent: null), containingNamespace: "Standard", isBackendBacked: true);

    // ── Requirement 14.1: Non-void return → System.Func<...> ─────────────

    [Fact]
    public void FuncIntString_EmitsSystemFuncIntString()
    {
        // func<int, string> → System.Func<int, string> (one param, non-void return)
        var func = MakeStdlibFunc(2);
        var constructed = new ConstructedType(func, new IType[] { MakeInt(), MakeString() });

        Assert.Equal("System.Func<int, string>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void FuncIntIntBool_EmitsSystemFuncIntIntBool()
    {
        // func<int, int, bool> → System.Func<int, int, bool> (two params, non-void return)
        var func = MakeStdlibFunc(3);
        var constructed = new ConstructedType(func, new IType[]
        {
            MakeInt(), MakeInt(), MakeBool()
        });

        Assert.Equal("System.Func<int, int, bool>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void FuncBool_EmitsSystemFuncBool()
    {
        // func<bool> → System.Func<bool> (no params, non-void return)
        var func = MakeStdlibFunc(1);
        var constructed = new ConstructedType(func, new IType[] { MakeBool() });

        Assert.Equal("System.Func<bool>", CSharpTypeMap.GetTypeName(constructed));
    }

    // ── Requirement 14.2: Void return with params → System.Action<...> ───

    [Fact]
    public void FuncIntVoid_EmitsSystemActionInt()
    {
        // func<int, void> → System.Action<int> (one param, void return)
        var func = MakeStdlibFunc(2);
        var constructed = new ConstructedType(func, new IType[] { MakeInt(), MakeVoid() });

        Assert.Equal("System.Action<int>", CSharpTypeMap.GetTypeName(constructed));
    }

    [Fact]
    public void FuncIntStringVoid_EmitsSystemActionIntString()
    {
        // func<int, string, void> → System.Action<int, string> (two params, void return)
        var func = MakeStdlibFunc(3);
        var constructed = new ConstructedType(func, new IType[]
        {
            MakeInt(), MakeString(), MakeVoid()
        });

        Assert.Equal("System.Action<int, string>", CSharpTypeMap.GetTypeName(constructed));
    }

    // ── Requirement 14.3: Void-only → System.Action (non-generic) ────────

    [Fact]
    public void FuncVoid_EmitsNonGenericSystemAction()
    {
        // func<void> → System.Action (no params, void return — non-generic Action)
        var func = MakeStdlibFunc(1);
        var constructed = new ConstructedType(func, new IType[] { MakeVoid() });

        Assert.Equal("System.Action", CSharpTypeMap.GetTypeName(constructed));
    }

    // ── Property 7: C# Emission Correctness (FsCheck) ────────────────────
    // Generates random Func arities (0..15 parameters) with random return-type
    // choices (void vs non-void) and asserts the emission rule holds for all.

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(FuncScenarioArbitrary) })]
    public bool EmittedTypeName_MatchesDelegateFamily(FuncEmissionScenario scenario)
    {
        // Arity = parameter count + 1 (for the return type)
        var arity = scenario.ParameterCount + 1;
        var func = MakeStdlibFunc(arity);

        // Build type arguments: N parameter types + 1 return type
        var typeArgs = new IType[arity];
        for(var i = 0; i < scenario.ParameterCount; i++)
        {
            typeArgs[i] = MakeInt();
        }

        typeArgs[arity - 1] = scenario.ReturnsVoid ? MakeVoid() : MakeBool();

        var constructed = new ConstructedType(func, typeArgs);
        var emitted = CSharpTypeMap.GetTypeName(constructed);

        if(scenario.ReturnsVoid)
        {
            if(scenario.ParameterCount == 0)
            {
                return emitted == "System.Action";
            }

            var expectedParams = string.Join(", ", Enumerable.Repeat("int", scenario.ParameterCount));
            return emitted == $"System.Action<{expectedParams}>";
        }

        var allArgs = Enumerable.Repeat("int", scenario.ParameterCount).Append("bool");
        return emitted == $"System.Func<{string.Join(", ", allArgs)}>";
    }
}

/// <summary>
/// Scenario record for the Func emission property test.
/// </summary>
public sealed record FuncEmissionScenario(int ParameterCount, bool ReturnsVoid)
{
    public override string ToString() =>
        $"Func<{string.Join(", ", Enumerable.Repeat("int", ParameterCount).Append(ReturnsVoid ? "void" : "bool"))}>";
}

/// <summary>
/// FsCheck arbitrary provider for <see cref="FuncEmissionScenario"/>.
/// </summary>
public static class FuncScenarioArbitrary
{
    public static Arbitrary<FuncEmissionScenario> ArbitraryFuncEmissionScenario() =>
        (from paramCount in Gen.Choose(0, 15)
         from returnsVoid in Gen.Elements(true, false)
         select new FuncEmissionScenario(paramCount, returnsVoid))
        .ToArbitrary();
}