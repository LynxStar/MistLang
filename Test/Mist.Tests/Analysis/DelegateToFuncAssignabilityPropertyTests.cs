// Feature: delegate-func-conversion, Property 4: delegate-to-func assignability
// **Validates: Requirements 1.4, 6.3, 8.4**

using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for delegate-to-func assignability.
///
/// Property 4: For any DelegateType D, IsAssignable(D, D.FuncSignature) SHALL return true.
/// That is, a named delegate value is always assignable to a variable of its equivalent
/// func&lt;...&gt; type.
///
/// **Validates: Requirements 1.4, 6.3, 8.4**
/// </summary>
public sealed class DelegateToFuncAssignabilityPropertyTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Generator: Random DelegateType instances
    //  Produces DelegateType with:
    //    - 1-3 namespace segments (e.g., "System", "Microsoft.AspNetCore.Http")
    //    - A random simple name (PascalCase identifier)
    //    - 1-6 FuncSignature type arguments (0-5 params + return type)
    //    - 0-3 generic type arguments for generic delegates
    // ══════════════════════════════════════════════════════════════════

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
        // Produce simple struct types as stand-ins for type arguments
        var typeNames = new[] { "Int32", "String", "Boolean", "Double", "Object" };
        return Gen.Elements(typeNames)
            .Select(name => (IType)new StructType(name, new Scope(null), "Standard"));
    }

    private static ConstructedType BuildFuncSignature(IType[] typeArgs)
    {
        var funcDef = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
        var tps = new IType[typeArgs.Length];
        for(var i = 0; i < typeArgs.Length; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        funcDef.TypeParameters = tps;

        return new ConstructedType(funcDef, typeArgs);
    }

    private record DelegateTypeInput(
        string ClrNamespace,
        string ClrSimpleName,
        int FuncArity,
        IType[] FuncTypeArgs,
        IType[] TypeArguments);

    private static Gen<DelegateTypeInput> GenDelegateTypeInput()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().SelectMany(simpleName =>
                Gen.Choose(1, 6).SelectMany(funcArity =>
                    Gen.ArrayOf(GenSimpleType(), funcArity).SelectMany(funcTypeArgs =>
                        Gen.Choose(0, 3).SelectMany(typeArgCount =>
                            Gen.ArrayOf(GenSimpleType(), typeArgCount).Select(typeArgs =>
                                new DelegateTypeInput(ns, simpleName, funcArity, funcTypeArgs, typeArgs)))))));
    }

    private static DelegateType BuildDelegateType(DelegateTypeInput input)
    {
        var funcSignature = BuildFuncSignature(input.FuncTypeArgs);

        return new DelegateType(
            clrFullName: $"{input.ClrNamespace}.{input.ClrSimpleName}",
            clrNamespace: input.ClrNamespace,
            clrSimpleName: input.ClrSimpleName,
            funcSignature: funcSignature,
            typeParameters: Array.Empty<IType>(),
            typeArguments: input.TypeArguments,
            memberScope: new Scope(null));
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 4: delegate-to-func assignability
    //
    //  For any DelegateType D, IsAssignable(D, D.FuncSignature) SHALL
    //  return true. A named delegate value is always assignable to a
    //  variable of its equivalent func<...> type.
    //
    //  **Validates: Requirements 1.4, 6.3, 8.4**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P4_DelegateToFunc_IsAlwaysAssignable()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var delegateType = BuildDelegateType(input);

            // The delegate's FuncSignature is a ConstructedType wrapping Standard.Func.
            // IsAssignable(delegateType, delegateType.FuncSignature) must always return true.
            var result = TypeCheckerAnalyzer.IsAssignable(delegateType, delegateType.FuncSignature);

            Assert.True(result,
                $"IsAssignable({delegateType.DisplayName}, {delegateType.FuncSignature.DisplayName}) " +
                $"should return true. CLR name: {delegateType.ClrFullName}, " +
                $"FuncSignature arity: {delegateType.FuncSignature.TypeArguments.Count}");
        }
    }
}