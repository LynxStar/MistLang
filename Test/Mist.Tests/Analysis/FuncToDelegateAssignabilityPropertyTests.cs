using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for func-to-delegate assignability.
/// Feature: delegate-func-conversion, Property 3: func-to-delegate assignability iff signature-compatible
///
/// For any DelegateType D with equivalent func&lt;A1, ..., AN, R&gt; signature and any
/// ConstructedType F wrapping Standard.Func&lt;B1, ..., BN, S&gt;, IsAssignable(F, D)
/// SHALL return true if and only if N equals the parameter count AND each Bi is
/// assignable to Ai AND S is assignable to R.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 6.2**
/// </summary>
public sealed class FuncToDelegateAssignabilityPropertyTests
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
    /// Creates a DelegateType whose FuncSignature uses the given ClassType definition.
    /// </summary>
    private static DelegateType MakeDelegate(ClassType sharedFuncDef, IType[] typeArgs, string clrName)
    {
        var funcSig = new ConstructedType(sharedFuncDef, typeArgs);
        return new DelegateType(
            clrFullName: $"TestNamespace.{clrName}",
            clrNamespace: "TestNamespace",
            clrSimpleName: clrName,
            funcSignature: funcSig,
            typeParameters: Array.Empty<IType>(),
            typeArguments: Array.Empty<IType>(),
            memberScope: new Scope(null));
    }

    /// <summary>
    /// Generates a signature-compatible pair: a DelegateType and a ConstructedType
    /// that wrap the SAME Standard.Func definition with the SAME type arguments.
    /// IsAssignable(funcType, delegateType) should return true.
    /// </summary>
    private record CompatiblePair(DelegateType Delegate, ConstructedType FuncType, int Arity);

    private static Gen<CompatiblePair> GenCompatiblePair()
    {
        return Gen.Choose(1, 6).SelectMany(arity =>
            Gen.ArrayOf(GenPoolType(), arity).Select(typeArgs =>
            {
                var sharedDef = MakeFuncDefinition(arity);
                var dt = MakeDelegate(sharedDef, typeArgs, "TestDelegate");
                var funcType = new ConstructedType(sharedDef, typeArgs);
                return new CompatiblePair(dt, funcType, arity);
            }));
    }

    /// <summary>
    /// Generates an incompatible pair where the func type has different arity than the delegate.
    /// Uses two separate arities guaranteed to differ.
    /// </summary>
    private record IncompatibleArityPair(DelegateType Delegate, ConstructedType FuncType, int DelegateArity, int FuncArity);

    private static Gen<IncompatibleArityPair> GenIncompatibleArityPair()
    {
        // Generate a delegate arity and a func arity that differ
        return Gen.Choose(1, 5).SelectMany(delegateArity =>
        {
            // Build a list of valid func arities that differ from the delegate arity
            var validFuncArities = Enumerable.Range(1, 5).Where(a => a != delegateArity).ToArray();
            return Gen.Elements(validFuncArities).SelectMany(funcArity =>
                Gen.ArrayOf(GenPoolType(), delegateArity).SelectMany(delegateArgs =>
                    Gen.ArrayOf(GenPoolType(), funcArity).Select(funcArgs =>
                    {
                        var delegateDef = MakeFuncDefinition(delegateArity);
                        var dt = MakeDelegate(delegateDef, delegateArgs, "ArityDelegate");
                        var funcDef = MakeFuncDefinition(funcArity);
                        var funcType = new ConstructedType(funcDef, funcArgs);
                        return new IncompatibleArityPair(dt, funcType, delegateArity, funcArity);
                    })));
        });
    }

    /// <summary>
    /// Generates an incompatible pair where arity matches but at least one type argument differs.
    /// </summary>
    private record IncompatibleTypePair(DelegateType Delegate, ConstructedType FuncType, int Arity, int DifferingIndex);

    private static Gen<IncompatibleTypePair> GenIncompatibleTypePair()
    {
        return Gen.Choose(1, 6).SelectMany(arity =>
            Gen.ArrayOf(GenPoolType(), arity).SelectMany(delegateArgs =>
                Gen.Choose(0, arity - 1).SelectMany(differingIndex =>
                {
                    // Pick a type for the differing position that is different from the delegate's
                    var delegateArgAtPos = delegateArgs[differingIndex];
                    var otherTypes = TypePool.Where(t => t.DisplayName != delegateArgAtPos.DisplayName).ToArray();

                    return Gen.Elements(otherTypes).Select(differentType =>
                    {
                        var funcArgs = (IType[])delegateArgs.Clone();
                        funcArgs[differingIndex] = differentType;

                        var sharedDef = MakeFuncDefinition(arity);
                        var dt = MakeDelegate(sharedDef, delegateArgs, "TypeMismatchDelegate");
                        var funcType = new ConstructedType(sharedDef, funcArgs);
                        return new IncompatibleTypePair(dt, funcType, arity, differingIndex);
                    });
                })));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 3: func-to-delegate assignability iff signature-compatible
    //
    //  Test 1: Signature-compatible func → delegate → IsAssignable returns true
    //  Test 2: Different arity → IsAssignable returns false
    //  Test 3: Same arity, different types → IsAssignable returns false
    //  Test 4: Reverse direction — DelegateType → compatible func → true
    //
    //  **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 2.5, 6.2**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P3_SignatureCompatibleFunc_IsAssignableToDelegateType()
    {
        var samples = GenCompatiblePair().Sample(100);
        foreach(var pair in samples)
        {
            var result = TypeCheckerAnalyzer.IsAssignable(pair.FuncType, pair.Delegate);

            Assert.True(result,
                $"Expected IsAssignable(func<{FormatTypeArgs(pair.FuncType)}>, " +
                $"{pair.Delegate.DisplayName}) to return true for signature-compatible pair " +
                $"with arity {pair.Arity}");
        }
    }

    [Fact]
    public void P3_DifferentArityFunc_IsNotAssignableToDelegateType()
    {
        var samples = GenIncompatibleArityPair().Sample(100);
        foreach(var pair in samples)
        {
            var result = TypeCheckerAnalyzer.IsAssignable(pair.FuncType, pair.Delegate);

            Assert.False(result,
                $"Expected IsAssignable(func<arity={pair.FuncArity}>, " +
                $"delegate<arity={pair.DelegateArity}>) to return false for arity mismatch");
        }
    }

    [Fact]
    public void P3_SameArityDifferentTypes_IsNotAssignableToDelegateType()
    {
        var samples = GenIncompatibleTypePair().Sample(100);
        foreach(var pair in samples)
        {
            var result = TypeCheckerAnalyzer.IsAssignable(pair.FuncType, pair.Delegate);

            Assert.False(result,
                $"Expected IsAssignable(func<{FormatTypeArgs(pair.FuncType)}>, " +
                $"delegate with func<{FormatTypeArgs(pair.Delegate.FuncSignature)}>) " +
                $"to return false — type mismatch at position {pair.DifferingIndex}");
        }
    }

    [Fact]
    public void P3_DelegateToSignatureCompatibleFunc_IsAssignable()
    {
        // Reverse direction: DelegateType → func<...> should also be signature-compatible
        var samples = GenCompatiblePair().Sample(100);
        foreach(var pair in samples)
        {
            var result = TypeCheckerAnalyzer.IsAssignable(pair.Delegate, pair.FuncType);

            Assert.True(result,
                $"Expected IsAssignable({pair.Delegate.DisplayName}, " +
                $"func<{FormatTypeArgs(pair.FuncType)}>) to return true for delegate-to-func conversion");
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