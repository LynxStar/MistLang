// Feature: delegate-func-conversion, Property 5: Any delegate or func assignable to BaseDelegateType
// **Validates: Requirements 3.2, 3.3, 6.4, 6.5**

using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for BaseDelegateType assignability.
///
/// Property 5: For any DelegateType D, IsAssignable(D, BaseDelegateType.Instance) SHALL
/// return true. For any ConstructedType F wrapping Standard.Func&lt;...&gt;,
/// IsAssignable(F, BaseDelegateType.Instance) SHALL return true.
///
/// **Validates: Requirements 3.2, 3.3, 6.4, 6.5**
/// </summary>
public sealed class BaseDelegateTypeAssignabilityPropertyTests
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
        for(var i = 0; i < arity; i++)
        {
            tps[i] = new TypeParameterType($"T{i}", i);
        }

        funcDef.TypeParameters = tps;
        return funcDef;
    }

    /// <summary>
    /// Generates a random DelegateType with varying CLR names and func signature arities.
    /// </summary>
    private record DelegateTypeInput(
        string ClrNamespace,
        string ClrSimpleName,
        IType[] FuncTypeArgs,
        IType[] TypeArguments);

    private static Gen<DelegateTypeInput> GenDelegateTypeInput()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().SelectMany(simpleName =>
                Gen.Choose(1, 6).SelectMany(funcArity =>
                    Gen.ArrayOf(GenPoolType(), funcArity).SelectMany(funcTypeArgs =>
                        Gen.Choose(0, 3).SelectMany(typeArgCount =>
                            Gen.ArrayOf(GenPoolType(), typeArgCount).Select(typeArgs =>
                                new DelegateTypeInput(ns, simpleName, funcTypeArgs, typeArgs)))))));
    }

    private static DelegateType BuildDelegateType(DelegateTypeInput input)
    {
        var funcDef = MakeFuncDefinition(input.FuncTypeArgs.Length);
        var funcSignature = new ConstructedType(funcDef, input.FuncTypeArgs);

        return new DelegateType(
            clrFullName: $"{input.ClrNamespace}.{input.ClrSimpleName}",
            clrNamespace: input.ClrNamespace,
            clrSimpleName: input.ClrSimpleName,
            funcSignature: funcSignature,
            typeParameters: Array.Empty<IType>(),
            typeArguments: input.TypeArguments,
            memberScope: new Scope(null));
    }

    /// <summary>
    /// Generates a random ConstructedType wrapping Standard.Func with 1-6 type arguments.
    /// </summary>
    private static Gen<ConstructedType> GenFuncType()
    {
        return Gen.Choose(1, 6).SelectMany(arity =>
            Gen.ArrayOf(GenPoolType(), arity).Select(typeArgs =>
            {
                var funcDef = MakeFuncDefinition(arity);
                return new ConstructedType(funcDef, typeArgs);
            }));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 5: Any delegate or func assignable to BaseDelegateType
    //
    //  Test 1: For any DelegateType D, IsAssignable(D, BaseDelegateType.Instance)
    //          SHALL return true.
    //  Test 2: For any ConstructedType F wrapping Standard.Func<...>,
    //          IsAssignable(F, BaseDelegateType.Instance) SHALL return true.
    //
    //  **Validates: Requirements 3.2, 3.3, 6.4, 6.5**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P5_AnyDelegateType_IsAssignableToBaseDelegateType()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            var result = TypeCheckerAnalyzer.IsAssignable(delegateType, BaseDelegateType.Instance);

            Assert.True(result,
                $"Expected IsAssignable({delegateType.DisplayName}, BaseDelegateType.Instance) " +
                $"to return true. CLR name: {delegateType.ClrFullName}, " +
                $"FuncSignature arity: {delegateType.FuncSignature.TypeArguments.Count}");
        }
    }

    [Fact]
    public void P5_AnyFuncType_IsAssignableToBaseDelegateType()
    {
        var samples = GenFuncType().Sample(100);
        foreach(var funcType in samples)
        {
            var result = TypeCheckerAnalyzer.IsAssignable(funcType, BaseDelegateType.Instance);

            Assert.True(result,
                $"Expected IsAssignable(func<{FormatTypeArgs(funcType)}>, BaseDelegateType.Instance) " +
                $"to return true for func type with arity {funcType.TypeArguments.Count}");
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