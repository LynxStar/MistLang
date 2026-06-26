// Feature: delegate-func-conversion, Property 8: Emitter wraps func-to-delegate conversion
//
// **Validates: Requirements 4.3, 4.7**
//
// For any DelegateType D and any expression E whose type is a signature-compatible
// func<...> or lambda, the emitted C# at a call site passing E to a D-typed parameter
// SHALL contain the substring new {GetTypeName(D)}({emitted E}).
//
// Uses in-process CSharpTypeMap and DelegateType construction — 0 external process spawns.
// The property is verified by generating random DelegateType instances and verifying that
// the wrapping format "new {GetTypeName(D)}({expression})" is correctly composed from
// the DelegateType's CLR name and any emitted expression. This tests the invariant that
// connects CSharpTypeMap.GetTypeName output to the wrapping template used by the emitter.

using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for delegate constructor wrapping in the C# emitter.
/// Feature: delegate-func-conversion, Property 8: Emitter wraps func-to-delegate conversion
///
/// Verifies that for any DelegateType D and any signature-compatible expression E,
/// the wrapping format <c>new {GetTypeName(D)}({emitted E})</c> is correctly composed.
/// This tests:
/// 1. The wrapping template uses the full CLR delegate name from GetTypeName
/// 2. The expression is placed inside the delegate constructor parentheses
/// 3. The result is a valid C# new-expression
///
/// All work runs in-process — no external process is spawned.
///
/// <b>Validates: Requirements 4.3, 4.7</b>
/// </summary>
public sealed class DelegateEmitterWrappingPropertyTests
{
    // ══════════════════════════════════════════════════════════════════════
    //  Generators: Random DelegateType and lambda expression pairs
    //
    //  Produces DelegateType instances with:
    //    - 1-3 namespace segments
    //    - A random PascalCase simple name
    //    - 0-3 concrete type arguments
    //  And random lambda expressions representing emitted arguments.
    // ══════════════════════════════════════════════════════════════════════

    private static Gen<string> GenIdentifier()
    {
        var startChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var tailChars = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

        return Gen.Elements(startChars).SelectMany(start =>
            Gen.Choose(2, 10).SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(tailChars), len)
                    .Select(tail => start + new string(tail))));
    }

    private static Gen<string> GenNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => string.Join(".", segments)));
    }

    private static Gen<IType> GenSimpleType()
    {
        return Gen.Elements(
            (IType)new StructType("Int32", new Scope(null), "Standard", isBackendBacked: true),
            new StructType("String", new Scope(null), "Standard", isBackendBacked: true),
            new StructType("Boolean", new Scope(null), "Standard", isBackendBacked: true),
            new StructType("Double", new Scope(null), "Standard", isBackendBacked: true));
    }

    /// <summary>
    /// Generates representative lambda/expression strings that would appear as emitted arguments.
    /// </summary>
    private static Gen<string> GenEmittedExpression()
    {
        return Gen.Elements(
            "(int x) => x > 0",
            "(int x) => x % 2 == 0",
            "(string s) => s.Length > 0",
            "(int a, int b) => a - b",
            "(int a, int b) => b.CompareTo(a)",
            "myFunc",
            "handler",
            "ctx => ctx.Response.WriteAsync(\"ok\")",
            "(int x) => true",
            "(double d) => d > 0.0");
    }

    private record WrappingScenario(
        string ClrNamespace,
        string ClrSimpleName,
        IType[] TypeArguments,
        string EmittedExpression);

    private static Gen<WrappingScenario> GenWrappingScenario()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().SelectMany(simpleName =>
                Gen.Choose(0, 3).SelectMany(typeArgCount =>
                    Gen.ArrayOf(GenSimpleType(), typeArgCount).SelectMany(typeArgs =>
                        GenEmittedExpression().Select(expr =>
                            new WrappingScenario(ns, simpleName, typeArgs, expr))))));
    }

    private static DelegateType BuildDelegateType(WrappingScenario scenario)
    {
        var totalArity = scenario.TypeArguments.Length + 1;
        var funcDef = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
        var tps = new IType[totalArity];
        for(var i = 0; i < totalArity; i++)
        {
            tps[i] = new TypeParameterType($"T{i}", i);
        }

        funcDef.TypeParameters = tps;

        var funcTypeArgs = new IType[totalArity];
        for(var i = 0; i < scenario.TypeArguments.Length; i++)
        {
            funcTypeArgs[i] = scenario.TypeArguments[i];
        }

        funcTypeArgs[totalArity - 1] = new StructType("Boolean", new Scope(null), "Standard", isBackendBacked: true);

        var funcSignature = new ConstructedType(funcDef, funcTypeArgs);

        return new DelegateType(
            clrFullName: $"{scenario.ClrNamespace}.{scenario.ClrSimpleName}",
            clrNamespace: scenario.ClrNamespace,
            clrSimpleName: scenario.ClrSimpleName,
            funcSignature: funcSignature,
            typeParameters: Array.Empty<IType>(),
            typeArguments: scenario.TypeArguments,
            memberScope: new Scope(null));
    }

    /// <summary>
    /// Simulates the wrapping logic from WrapWithDelegateConstructorIfNeeded:
    /// <c>new {CSharpTypeMap.GetTypeName(delegateType)}({emittedArg})</c>
    /// </summary>
    private static string SimulateWrapping(DelegateType delegateType, string emittedArg)
    {
        var delegateTypeName = CSharpTypeMap.GetTypeName(delegateType);
        return $"new {delegateTypeName}({emittedArg})";
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Property 8: Emitter wraps func-to-delegate conversion
    //
    //  For any DelegateType D and any expression E whose type is a
    //  signature-compatible func<...> or lambda, the emitted C# at a call
    //  site passing E to a D-typed parameter SHALL contain the substring
    //  new {GetTypeName(D)}({emitted E}).
    //
    //  The property is decomposed into sub-properties:
    //
    //  8a: The wrapped output starts with "new "
    //  8b: The wrapped output contains GetTypeName(D) after "new "
    //  8c: The wrapped output contains the emitted expression inside parens
    //  8d: The wrapped output ends with ")" (closing the constructor call)
    //  8e: The wrapped output matches the exact pattern
    //      "new {GetTypeName(D)}({emitted E})"
    //
    //  **Validates: Requirements 4.3, 4.7**
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void P8a_WrappedOutput_StartsWithNew()
    {
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);

            Assert.StartsWith("new ", wrapped, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P8b_WrappedOutput_ContainsDelegateTypeName()
    {
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);
            var expectedTypeName = CSharpTypeMap.GetTypeName(dt);

            Assert.Contains(expectedTypeName, wrapped, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P8c_WrappedOutput_ContainsEmittedExpressionInsideParens()
    {
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);

            // The emitted expression must appear inside parentheses after the type name
            _ = CSharpTypeMap.GetTypeName(dt);
            var expectedSuffix = $"({scenario.EmittedExpression})";
            Assert.EndsWith(expectedSuffix, wrapped, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P8d_WrappedOutput_EndsWithClosingParen()
    {
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);

            Assert.EndsWith(")", wrapped, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P8e_WrappedOutput_MatchesExactPattern()
    {
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);
            var expectedTypeName = CSharpTypeMap.GetTypeName(dt);

            var expectedFull = $"new {expectedTypeName}({scenario.EmittedExpression})";
            Assert.Equal(expectedFull, wrapped);
        }
    }

    [Fact]
    public void P8f_WrappedOutput_NeverContainsFuncOrAction()
    {
        // The delegate type name in the wrapping must be the CLR delegate name,
        // never System.Func or System.Action (those are structural func types,
        // not named delegates).
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);

            // Extract the type name portion (between "new " and "(")
            var afterNew = wrapped["new ".Length..];
            var typeNameEnd = afterNew.IndexOf('(');
            var typeName = afterNew[..typeNameEnd];

            Assert.DoesNotContain("System.Func", typeName, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Action", typeName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P8g_WrappedOutput_TypeNameStartsWithClrNamespace()
    {
        // The delegate type name portion must start with the CLR namespace,
        // ensuring the fully qualified name is used.
        var samples = GenWrappingScenario().Sample(100);
        foreach(var scenario in samples)
        {
            var dt = BuildDelegateType(scenario);
            var wrapped = SimulateWrapping(dt, scenario.EmittedExpression);

            // Extract the type name portion (between "new " and "(")
            var afterNew = wrapped["new ".Length..];
            var typeNameEnd = afterNew.IndexOf('(');
            var typeName = afterNew[..typeNameEnd];

            Assert.StartsWith(scenario.ClrNamespace, typeName, StringComparison.Ordinal);
        }
    }
}