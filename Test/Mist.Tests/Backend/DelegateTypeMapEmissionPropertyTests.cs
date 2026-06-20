using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for CSharpTypeMap delegate type name emission.
/// Feature: delegate-func-conversion, Property 2: Type map emits correct CLR delegate name
///
/// Generates random DelegateType instances with varying CLR namespaces, simple names,
/// and type arguments. Verifies that GetTypeName output starts with ClrNamespace,
/// contains ClrSimpleName, includes type args in angle brackets when present, and
/// never contains "System.Func" or "System.Action".
///
/// **Validates: Requirements 1.8, 4.1, 4.2, 4.6**
/// </summary>
public sealed class DelegateTypeMapEmissionPropertyTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Generator: Random DelegateType instances for type map testing
    //  Produces DelegateType with:
    //    - 1-3 namespace segments (non-empty)
    //    - A random PascalCase simple name (non-empty)
    //    - 0-3 concrete type arguments (StructTypes with known C# names)
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

    /// <summary>
    /// Generates simple types that resolve to known C# type names through
    /// CSharpTypeMap.GetTypeName (backend-backed Standard types).
    /// </summary>
    private static Gen<IType> GenSimpleType()
    {
        // Use backend-backed Standard struct types that map to C# keywords
        return Gen.Elements(
            (IType)new StructType("Int32", new Scope(null), "Standard", isBackendBacked: true),
            new StructType("String", new Scope(null), "Standard", isBackendBacked: true),
            new StructType("Boolean", new Scope(null), "Standard", isBackendBacked: true),
            new StructType("Double", new Scope(null), "Standard", isBackendBacked: true));
    }

    /// <summary>
    /// Map of generated type argument display names to their expected C# emission.
    /// CSharpTypeMap maps Standard backend-backed types to C# keywords.
    /// </summary>
    private static string GetExpectedCSharpName(IType type)
    {
        return type switch
        {
            StructType { DisplayName: "Int32", ContainingNamespace: "Standard", IsBackendBacked: true } => "int",
            StructType { DisplayName: "String", ContainingNamespace: "Standard", IsBackendBacked: true } => "string",
            StructType { DisplayName: "Boolean", ContainingNamespace: "Standard", IsBackendBacked: true } => "bool",
            StructType { DisplayName: "Double", ContainingNamespace: "Standard", IsBackendBacked: true } => "double",
            _ => type.DisplayName,
        };
    }

    private record DelegateTypeMapInput(
        string ClrNamespace,
        string ClrSimpleName,
        IType[] TypeArguments);

    private static Gen<DelegateTypeMapInput> GenDelegateTypeMapInput()
    {
        return GenNamespace().SelectMany(ns =>
            GenIdentifier().SelectMany(simpleName =>
                Gen.Choose(0, 3).SelectMany(typeArgCount =>
                    Gen.ArrayOf(GenSimpleType(), typeArgCount).Select(typeArgs =>
                        new DelegateTypeMapInput(ns, simpleName, typeArgs)))));
    }

    private static DelegateType BuildDelegateType(DelegateTypeMapInput input)
    {
        // Build a minimal FuncSignature (1 type arg = void return, just enough to be valid)
        var totalArity = input.TypeArguments.Length + 1; // at minimum 1 for return type
        var funcDef = new ClassType("Func", new Scope(null), "Standard", isBackendBacked: true);
        var tps = new IType[totalArity];
        for (var i = 0; i < totalArity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        funcDef.TypeParameters = tps;

        var funcTypeArgs = new IType[totalArity];
        for (var i = 0; i < input.TypeArguments.Length; i++)
            funcTypeArgs[i] = input.TypeArguments[i];
        // Return type (last position)
        funcTypeArgs[totalArity - 1] = new StructType("Boolean", new Scope(null), "Standard", isBackendBacked: true);

        var funcSignature = new ConstructedType(funcDef, funcTypeArgs);

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
    //  Property 2: Type map emits correct CLR delegate name
    //
    //  For any DelegateType with a non-empty ClrNamespace and ClrSimpleName,
    //  calling CSharpTypeMap.GetTypeName SHALL produce a string that:
    //  1. Starts with ClrNamespace
    //  2. Contains ClrSimpleName
    //  3. When TypeArguments is non-empty: includes C# names of all type
    //     arguments in angle brackets
    //  4. Never contains "System.Func" or "System.Action"
    //
    //  **Validates: Requirements 1.8, 4.1, 4.2, 4.6**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P2_TypeMapEmission_StartsWithClrNamespace()
    {
        var samples = GenDelegateTypeMapInput().Sample(100);
        foreach (var input in samples)
        {
            var dt = BuildDelegateType(input);
            var result = CSharpTypeMap.GetTypeName(dt);

            Assert.StartsWith(input.ClrNamespace, result,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P2_TypeMapEmission_ContainsClrSimpleName()
    {
        var samples = GenDelegateTypeMapInput().Sample(100);
        foreach (var input in samples)
        {
            var dt = BuildDelegateType(input);
            var result = CSharpTypeMap.GetTypeName(dt);

            Assert.Contains(input.ClrSimpleName, result,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P2_TypeMapEmission_IncludesTypeArgsInAngleBracketsWhenPresent()
    {
        var samples = GenDelegateTypeMapInput().Sample(100);
        foreach (var input in samples)
        {
            var dt = BuildDelegateType(input);
            var result = CSharpTypeMap.GetTypeName(dt);

            if (input.TypeArguments.Length > 0)
            {
                // Must contain angle brackets
                Assert.Contains("<", result, StringComparison.Ordinal);
                Assert.Contains(">", result, StringComparison.Ordinal);

                // Each type argument's C# name must appear inside the brackets
                var bracketStart = result.IndexOf('<');
                var bracketEnd = result.LastIndexOf('>');
                var bracketContent = result.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);

                foreach (var typeArg in input.TypeArguments)
                {
                    var expectedName = GetExpectedCSharpName(typeArg);
                    Assert.Contains(expectedName, bracketContent, StringComparison.Ordinal);
                }
            }
            else
            {
                // Non-generic: no angle brackets
                Assert.DoesNotContain("<", result, StringComparison.Ordinal);
                Assert.DoesNotContain(">", result, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void P2_TypeMapEmission_NeverContainsSystemFuncOrSystemAction()
    {
        var samples = GenDelegateTypeMapInput().Sample(100);
        foreach (var input in samples)
        {
            var dt = BuildDelegateType(input);
            var result = CSharpTypeMap.GetTypeName(dt);

            Assert.DoesNotContain("System.Func", result, StringComparison.Ordinal);
            Assert.DoesNotContain("System.Action", result, StringComparison.Ordinal);
        }
    }
}
