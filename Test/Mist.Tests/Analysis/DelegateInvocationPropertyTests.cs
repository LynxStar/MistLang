// Feature: delegate-func-conversion, Property 11: Delegate invocation type-checks against FuncSignature
// **Validates: Requirements 8.1, 8.3**

using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for delegate invocation type-checking against FuncSignature.
///
/// Property 11: For any DelegateType D with FuncSignature = func&lt;P1, ..., PN, R&gt;,
/// invoking a value of type D with arguments (a1, ..., aN) SHALL type-check successfully
/// if each ai is assignable to Pi, and the result type SHALL be R. Invoking with wrong
/// argument count or incompatible argument types SHALL produce an error diagnostic.
///
/// This test validates the structural properties of DelegateType that enable invocation:
/// 1. FuncSignature.TypeArguments.Count &gt;= 1 (at least a return type)
/// 2. Parameter types are the first N-1 type arguments, return type is the last
/// 3. IsAssignable(delegateType, delegateType.FuncSignature) = true (delegate invocable as its func signature)
/// 4. Wrong argument count (different arity func) is not assignable
///
/// **Validates: Requirements 8.1, 8.3**
/// </summary>
public sealed class DelegateInvocationPropertyTests
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
    /// Generates a random DelegateType with varying CLR names and func signature arities.
    /// Arity is the total number of FuncSignature type arguments (params + return type),
    /// so arity >= 1 means at least a return type.
    /// </summary>
    private record DelegateTypeInput(
        string ClrNamespace,
        string ClrSimpleName,
        IType[] FuncTypeArgs);

    private static Gen<DelegateTypeInput> GenDelegateTypeInput()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().SelectMany(simpleName =>
                Gen.Choose(1, 6).SelectMany(funcArity =>
                    Gen.ArrayOf(GenPoolType(), funcArity).Select(funcTypeArgs =>
                        new DelegateTypeInput(ns, simpleName, funcTypeArgs)))));
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
            typeArguments: Array.Empty<IType>(),
            memberScope: new Scope(null));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 11: Delegate invocation type-checks against FuncSignature
    //
    //  Test 1: FuncSignature.TypeArguments.Count >= 1 (at least a return type)
    //  Test 2: Parameter types are the first N-1 type args, return type is the last
    //  Test 3: IsAssignable(delegateType, delegateType.FuncSignature) = true
    //          (delegate is invocable as its func signature)
    //  Test 4: Wrong argument count (different arity func) is not assignable
    //
    //  **Validates: Requirements 8.1, 8.3**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P11_FuncSignature_HasAtLeastOneTypeArgument()
    {
        // For any DelegateType D, D.FuncSignature.TypeArguments.Count >= 1.
        // This guarantees at least a return type is present, enabling invocation.
        var samples = GenDelegateTypeInput().Sample(100);
        foreach (var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            Assert.True(delegateType.FuncSignature.TypeArguments.Count >= 1,
                $"FuncSignature for {delegateType.DisplayName} must have at least 1 type argument " +
                $"(the return type). Got: {delegateType.FuncSignature.TypeArguments.Count}");
        }
    }

    [Fact]
    public void P11_FuncSignature_ParameterTypesAreFirstNMinus1_ReturnTypeIsLast()
    {
        // For any DelegateType D with FuncSignature = func<P1, ..., PN, R>,
        // the parameter types are TypeArguments[0..N-2] and the return type is TypeArguments[N-1].
        var samples = GenDelegateTypeInput().Sample(100);
        foreach (var input in samples)
        {
            var delegateType = BuildDelegateType(input);
            var typeArgs = delegateType.FuncSignature.TypeArguments;
            var totalArgs = typeArgs.Count;

            // The return type is the last type argument
            var returnType = typeArgs[totalArgs - 1];
            Assert.NotNull(returnType);

            // The parameter types are the first N-1 type arguments
            var paramCount = totalArgs - 1;
            for (var i = 0; i < paramCount; i++)
            {
                Assert.NotNull(typeArgs[i]);
                // Each parameter type should be from our type pool
                Assert.Contains(typeArgs[i].DisplayName,
                    TypePool.Select(t => t.DisplayName));
            }

            // Return type should also be from the type pool
            Assert.Contains(returnType.DisplayName,
                TypePool.Select(t => t.DisplayName));
        }
    }

    [Fact]
    public void P11_DelegateType_IsAssignableToItsFuncSignature()
    {
        // For any DelegateType D, IsAssignable(D, D.FuncSignature) = true.
        // This proves the delegate is invocable: the type system accepts the delegate
        // value in any position requiring its equivalent func<...> signature,
        // which is the mechanism enabling invocation type-checking.
        var samples = GenDelegateTypeInput().Sample(100);
        foreach (var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            var result = TypeCheckerAnalyzer.IsAssignable(delegateType, delegateType.FuncSignature);

            Assert.True(result,
                $"IsAssignable({delegateType.DisplayName}, {delegateType.FuncSignature.DisplayName}) " +
                $"should return true. A DelegateType must be assignable to its FuncSignature " +
                $"to enable invocation type-checking. " +
                $"CLR name: {delegateType.ClrFullName}, " +
                $"FuncSignature arity: {delegateType.FuncSignature.TypeArguments.Count}");
        }
    }

    [Fact]
    public void P11_WrongArgumentCount_IsNotAssignable()
    {
        // For any DelegateType D with FuncSignature of arity N, a func type with a
        // different arity M (M != N) is NOT assignable to the DelegateType.
        // This proves that wrong argument count invocations are rejected.
        var samples = GenWrongArityPair().Sample(100);
        foreach (var pair in samples)
        {
            var result = TypeCheckerAnalyzer.IsAssignable(pair.WrongArityFunc, pair.Delegate);

            Assert.False(result,
                $"Expected IsAssignable(func<arity={pair.WrongArity}>, " +
                $"{pair.Delegate.DisplayName}<arity={pair.CorrectArity}>) to return false. " +
                $"Wrong argument count must be rejected for invocation safety.");
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Wrong-arity pair generator
    // ══════════════════════════════════════════════════════════════════

    private record WrongArityPair(DelegateType Delegate, ConstructedType WrongArityFunc, int CorrectArity, int WrongArity);

    private static Gen<WrongArityPair> GenWrongArityPair()
    {
        return Gen.Choose(1, 5).SelectMany(delegateArity =>
        {
            var validWrongArities = Enumerable.Range(1, 5).Where(a => a != delegateArity).ToArray();
            return Gen.Elements(validWrongArities).SelectMany(wrongArity =>
                GenNamespace().SelectMany(ns =>
                    GenIdentifier().SelectMany(simpleName =>
                        Gen.ArrayOf(GenPoolType(), delegateArity).SelectMany(delegateTypeArgs =>
                            Gen.ArrayOf(GenPoolType(), wrongArity).Select(wrongTypeArgs =>
                            {
                                var delegateFuncDef = MakeFuncDefinition(delegateArity);
                                var funcSignature = new ConstructedType(delegateFuncDef, delegateTypeArgs);

                                var delegateType = new DelegateType(
                                    clrFullName: $"{ns}.{simpleName}",
                                    clrNamespace: ns,
                                    clrSimpleName: simpleName,
                                    funcSignature: funcSignature,
                                    typeParameters: Array.Empty<IType>(),
                                    typeArguments: Array.Empty<IType>(),
                                    memberScope: new Scope(null));

                                var wrongFuncDef = MakeFuncDefinition(wrongArity);
                                var wrongFunc = new ConstructedType(wrongFuncDef, wrongTypeArgs);

                                return new WrongArityPair(delegateType, wrongFunc, delegateArity, wrongArity);
                            })))));
        });
    }
}
