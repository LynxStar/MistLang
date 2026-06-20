// Feature: delegate-func-conversion, Property 7: Distinct named delegates are nominally non-convertible
// **Validates: Requirements 6.1, 6.6**

using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for delegate nominal distinctness.
///
/// Property 7: For any two DelegateType instances D1 and D2 where
/// D1.ClrFullName ≠ D2.ClrFullName, IsAssignable(D1, D2) SHALL return false
/// even when D1.FuncSignature and D2.FuncSignature are structurally identical.
///
/// **Validates: Requirements 6.1, 6.6**
/// </summary>
public sealed class DelegateNominalDistinctnessPropertyTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Shared type pool — simple concrete types used as type arguments.
    //  These are distinct struct types; none is assignable to another.
    // ══════════════════════════════════════════════════════════════════

    private static readonly StructType Int32Type = new("Int32", new Scope(null), "Standard");
    private static readonly StructType StringType = new("String", new Scope(null), "Standard");
    private static readonly StructType BooleanType = new("Boolean", new Scope(null), "Standard");
    private static readonly StructType DoubleType = new("Double", new Scope(null), "Standard");
    private static readonly StructType ObjectType = new("Object", new Scope(null), "Standard");

    private static readonly IType[] TypePool = [Int32Type, StringType, BooleanType, DoubleType, ObjectType];

    // ══════════════════════════════════════════════════════════════════
    //  Generators
    // ══════════════════════════════════════════════════════════════════

    private static Gen<IType> GenPoolType()
    {
        return Gen.Elements(TypePool);
    }

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

    /// <summary>
    /// Creates a Standard.Func ClassType definition with N type parameters.
    /// </summary>
    private static ClassType MakeFuncDefinition(int arity)
    {
        var funcDef = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
        var tps = new IType[arity];
        for (var i = 0; i < arity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        funcDef.TypeParameters = tps;
        return funcDef;
    }

    /// <summary>
    /// Input data for generating a pair of DelegateTypes that share the same FuncSignature
    /// but have different CLR names.
    /// </summary>
    private record DistinctDelegatePairInput(
        string Namespace1,
        string SimpleName1,
        string Namespace2,
        string SimpleName2,
        IType[] SharedFuncTypeArgs,
        IType[] TypeArguments);

    /// <summary>
    /// Generates pairs of DelegateType inputs that share the SAME func signature (same arity,
    /// same type arguments, same ClassType definition) but with DIFFERENT ClrFullName values.
    /// </summary>
    private static Gen<DistinctDelegatePairInput> GenDistinctDelegatePair()
    {
        return GenNamespace().SelectMany(ns1 =>
            GenIdentifier().SelectMany(name1 =>
                GenNamespace().SelectMany(ns2 =>
                    GenIdentifier().SelectMany(name2 =>
                        Gen.Choose(1, 6).SelectMany(funcArity =>
                            Gen.ArrayOf(GenPoolType(), funcArity).SelectMany(funcTypeArgs =>
                                Gen.Choose(0, 3).SelectMany(typeArgCount =>
                                    Gen.ArrayOf(GenPoolType(), typeArgCount).Select(typeArgs =>
                                        new DistinctDelegatePairInput(
                                            ns1, name1,
                                            ns2, name2,
                                            funcTypeArgs, typeArgs)))))))));
    }

    private static (DelegateType D1, DelegateType D2) BuildDistinctPair(DistinctDelegatePairInput input)
    {
        // Use the SAME func definition for both — ensuring structural identity
        var funcDef = MakeFuncDefinition(input.SharedFuncTypeArgs.Length);
        var funcSignature1 = new ConstructedType(funcDef, input.SharedFuncTypeArgs);
        var funcSignature2 = new ConstructedType(funcDef, input.SharedFuncTypeArgs);

        var fullName1 = $"{input.Namespace1}.{input.SimpleName1}";
        var fullName2 = $"{input.Namespace2}.{input.SimpleName2}";

        // Ensure the CLR full names are actually different
        // If by chance they collide, tweak the second name
        if (fullName1 == fullName2)
            fullName2 = $"{input.Namespace2}.{input.SimpleName2}X";

        var d1 = new DelegateType(
            clrFullName: fullName1,
            clrNamespace: input.Namespace1,
            clrSimpleName: input.SimpleName1,
            funcSignature: funcSignature1,
            typeParameters: Array.Empty<IType>(),
            typeArguments: input.TypeArguments,
            memberScope: new Scope(null));

        var d2 = new DelegateType(
            clrFullName: fullName2,
            clrNamespace: input.Namespace2,
            clrSimpleName: input.SimpleName2,
            funcSignature: funcSignature2,
            typeParameters: Array.Empty<IType>(),
            typeArguments: input.TypeArguments,
            memberScope: new Scope(null));

        return (d1, d2);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 7: Distinct named delegates are nominally non-convertible
    //
    //  Test 1: For any D1, D2 where D1.ClrFullName ≠ D2.ClrFullName,
    //          IsAssignable(D1, D2) SHALL return false.
    //  Test 2: For any D1, D2 where D1.ClrFullName ≠ D2.ClrFullName,
    //          IsAssignable(D2, D1) SHALL return false (both directions).
    //
    //  **Validates: Requirements 6.1, 6.6**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P7_DistinctNamedDelegates_NotAssignable_D1ToD2()
    {
        var samples = GenDistinctDelegatePair().Sample(100);
        foreach (var input in samples)
        {
            var (d1, d2) = BuildDistinctPair(input);

            var result = TypeCheckerAnalyzer.IsAssignable(d1, d2);

            Assert.False(result,
                $"Expected IsAssignable({d1.ClrFullName}, {d2.ClrFullName}) to return false. " +
                $"Named delegates are nominally distinct even when signatures match. " +
                $"Both share FuncSignature arity: {d1.FuncSignature.TypeArguments.Count}, " +
                $"FuncSignature type args: [{FormatTypeArgs(d1.FuncSignature)}]");
        }
    }

    [Fact]
    public void P7_DistinctNamedDelegates_NotAssignable_D2ToD1()
    {
        var samples = GenDistinctDelegatePair().Sample(100);
        foreach (var input in samples)
        {
            var (d1, d2) = BuildDistinctPair(input);

            var result = TypeCheckerAnalyzer.IsAssignable(d2, d1);

            Assert.False(result,
                $"Expected IsAssignable({d2.ClrFullName}, {d1.ClrFullName}) to return false. " +
                $"Named delegates are nominally distinct even when signatures match. " +
                $"Both share FuncSignature arity: {d2.FuncSignature.TypeArguments.Count}, " +
                $"FuncSignature type args: [{FormatTypeArgs(d2.FuncSignature)}]");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════

    private static string FormatTypeArgs(ConstructedType ct)
    {
        return string.Join(", ", ct.TypeArguments.Select(t => t.DisplayName));
    }
}
