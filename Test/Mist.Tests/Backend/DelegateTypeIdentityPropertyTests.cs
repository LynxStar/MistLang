using FsCheck;
using FsCheck.Fluent;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for DelegateType identity preservation.
/// Feature: delegate-func-conversion, Property 1: Projection preserves delegate identity
///
/// Generates random DelegateType instances with varying CLR names (1-3 namespace segments),
/// varying simple names, and varying FuncSignature arities (0-5 parameters).
/// Validates structural consistency invariants that must hold for any well-formed instance.
///
/// **Validates: Requirements 1.1, 1.3, 1.5, 1.6**
/// </summary>
public sealed class DelegateTypeIdentityPropertyTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Generator: Random DelegateType instances
    //  Produces DelegateType with:
    //    - 1-3 namespace segments (e.g., "System", "Microsoft.AspNetCore.Http")
    //    - A random simple name (PascalCase identifier)
    //    - 0-5 FuncSignature type arguments (arity = parameter count + 1 for return type)
    //    - Matching TypeArguments (0-3 generic type arguments for generic delegates)
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
        // Produce simple struct/class types as stand-ins for type arguments
        var typeNames = new[] { "Int32", "String", "Boolean", "Double", "Object" };
        return Gen.Elements(typeNames)
            .Select(name => (IType)new StructType(name, new Scope(null), "Standard"));
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
                Gen.Choose(1, 6).SelectMany(funcArity => // arity 1-6 (0-5 params + return)
                    Gen.ArrayOf(GenSimpleType(), funcArity).SelectMany(funcTypeArgs =>
                        Gen.Choose(0, 3).SelectMany(typeArgCount =>
                            Gen.ArrayOf(GenSimpleType(), typeArgCount).Select(typeArgs =>
                                new DelegateTypeInput(ns, simpleName, funcArity, funcTypeArgs, typeArgs)))))));
    }

    private static DelegateType BuildDelegateType(DelegateTypeInput input)
    {
        var funcDef = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
        var tps = new IType[input.FuncArity];
        for(var i = 0; i < input.FuncArity; i++)
        {
            tps[i] = new TypeParameterType($"T{i}", i);
        }

        funcDef.TypeParameters = tps;

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

    // ══════════════════════════════════════════════════════════════════
    //  Property 1: Projection preserves delegate identity
    //
    //  For any well-formed DelegateType instance, the following invariants hold:
    //  1. ClrFullName contains ClrNamespace and ClrSimpleName
    //  2. TypeArguments count is consistent (non-negative)
    //  3. FuncSignature arity matches expected value (parameter count + 1)
    //  4. DisplayName is constructed correctly
    //
    //  **Validates: Requirements 1.1, 1.3, 1.5, 1.6**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P1_DelegateTypeIdentity_ClrFullNameContainsNamespaceAndSimpleName()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var dt = BuildDelegateType(input);

            // ClrFullName must contain ClrNamespace
            Assert.Contains(dt.ClrNamespace, dt.ClrFullName);

            // ClrFullName must contain ClrSimpleName
            Assert.Contains(dt.ClrSimpleName, dt.ClrFullName);

            // ClrFullName should equal "{Namespace}.{SimpleName}"
            Assert.Equal($"{input.ClrNamespace}.{input.ClrSimpleName}", dt.ClrFullName);
        }
    }

    [Fact]
    public void P1_DelegateTypeIdentity_TypeArgumentsCountConsistent()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var dt = BuildDelegateType(input);

            // TypeArguments count matches what was provided
            Assert.Equal(input.TypeArguments.Length, dt.TypeArguments.Count);

            // Each type argument should be the same reference we passed in
            for(var i = 0; i < input.TypeArguments.Length; i++)
            {
                Assert.Same(input.TypeArguments[i], dt.TypeArguments[i]);
            }
        }
    }

    [Fact]
    public void P1_DelegateTypeIdentity_FuncSignatureArityMatchesParameterCountPlusOne()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var dt = BuildDelegateType(input);

            // FuncSignature is a ConstructedType wrapping Standard.Func
            var funcSig = dt.FuncSignature;
            Assert.NotNull(funcSig);

            var funcDef = Assert.IsType<ClassType>(funcSig.GenericDefinition);
            Assert.Equal("Func", funcDef.DisplayName);
            Assert.Equal("Standard", funcDef.ContainingNamespace);

            // Arity = the number of type arguments on the FuncSignature
            // This equals parameter count + 1 (for the return type)
            Assert.Equal(input.FuncArity, funcSig.TypeArguments.Count);

            // Arity must be >= 1 (at minimum a return type)
            Assert.True(funcSig.TypeArguments.Count >= 1,
                "FuncSignature arity must be >= 1 (at least a return type).");
        }
    }

    [Fact]
    public void P1_DelegateTypeIdentity_DisplayNameConstructedCorrectly()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var dt = BuildDelegateType(input);

            if(input.TypeArguments.Length > 0)
            {
                // Generic delegate: DisplayName should be "SimpleName<arg1, arg2, ...>"
                Assert.StartsWith(input.ClrSimpleName, dt.DisplayName);
                Assert.Contains("<", dt.DisplayName);
                Assert.Contains(">", dt.DisplayName);

                // All type argument display names should appear in the DisplayName
                foreach(var typeArg in input.TypeArguments)
                {
                    Assert.Contains(typeArg.DisplayName, dt.DisplayName);
                }
            }
            else
            {
                // Non-generic delegate: DisplayName should just be the simple name
                Assert.Equal(input.ClrSimpleName, dt.DisplayName);
            }
        }
    }

    [Fact]
    public void P1_DelegateTypeIdentity_KindIsClass()
    {
        var samples = GenDelegateTypeInput().Sample(100);
        foreach(var input in samples)
        {
            var dt = BuildDelegateType(input);

            // DelegateType.Kind must always be TypeKind.Class (delegates are reference types)
            Assert.Equal(TypeKind.Class, dt.Kind);
        }
    }
}