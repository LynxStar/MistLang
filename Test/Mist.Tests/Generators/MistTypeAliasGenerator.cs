// Feature: type-aliases, Property 1: Parsing produces correct AST structure
// **Validates: Requirements 2.1, 2.2, 2.3**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class TypeAliasSourceScenario
{
    public string Source { get; }

    public string AliasName { get; }

    public int TypeParameterCount { get; }

    public IReadOnlyList<string> TypeParameterNames { get; }

    /// <summary>The number of union constituents (1 for non-union targets).</summary>
    public int UnionConstituentCount { get; }

    public string Description { get; }

    public TypeAliasSourceScenario(
        string source,
        string aliasName,
        int typeParameterCount,
        IReadOnlyList<string> typeParameterNames,
        int unionConstituentCount,
        string description)
    {
        Source = source;
        AliasName = aliasName;
        TypeParameterCount = typeParameterCount;
        TypeParameterNames = typeParameterNames;
        UnionConstituentCount = unionConstituentCount;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistTypeAliasGenerator
{
    private static readonly string[] AliasNames =
        ["Foo", "Bar", "Baz", "Qux", "MyType", "Result", "Value", "Item", "Alias", "Wrapper"];

    private static readonly string[] SimpleTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W", "K"];

    private static readonly string[] GenericContainerTypes =
        ["List", "Map", "Set", "Option", "Container", "Wrapper"];

    public static Gen<TypeAliasSourceScenario> GenSimpleAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from targetType in Gen.Elements(SimpleTypes)
               let source = $"type {name} = {targetType};"
               select new TypeAliasSourceScenario(
                   source: source,
                   aliasName: name,
                   typeParameterCount: 0,
                   typeParameterNames: Array.Empty<string>(),
                   unionConstituentCount: 1,
                   description: $"simple: {source}");
    }

    public static Gen<TypeAliasSourceScenario> GenGenericAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 5)
               from containerType in Gen.Elements(GenericContainerTypes)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamList = string.Join(", ", typeParams)
               let typeArgList = string.Join(", ", typeParams)
               let source = $"type {name}<{typeParamList}> = {containerType}<{typeArgList}>;"
               select new TypeAliasSourceScenario(
                   source: source,
                   aliasName: name,
                   typeParameterCount: paramCount,
                   typeParameterNames: typeParams,
                   unionConstituentCount: 1,
                   description: $"generic({paramCount}): {source}");
    }

    public static Gen<TypeAliasSourceScenario> GenUnionAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from count in Gen.Choose(2, 5)
               from types in Gen.ArrayOf(Gen.Elements(SimpleTypes), count)
               let unionExpr = string.Join(" | ", types)
               let source = $"type {name} = {unionExpr};"
               select new TypeAliasSourceScenario(
                   source: source,
                   aliasName: name,
                   typeParameterCount: 0,
                   typeParameterNames: Array.Empty<string>(),
                   unionConstituentCount: count,
                   description: $"union({count}): {source}");
    }

    public static Gen<TypeAliasSourceScenario> GenGenericWithUnionAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 5)
               from extraCount in Gen.Choose(1, 3)
               from extraTypes in Gen.ArrayOf(Gen.Elements(SimpleTypes), extraCount)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               let typeParamList = string.Join(", ", typeParams)
               let allConstituents = typeParams.Concat(extraTypes).ToArray()
               let unionExpr = string.Join(" | ", allConstituents)
               let totalConstituents = allConstituents.Length
               let source = $"type {name}<{typeParamList}> = {unionExpr};"
               select new TypeAliasSourceScenario(
                   source: source,
                   aliasName: name,
                   typeParameterCount: paramCount,
                   typeParameterNames: typeParams,
                   unionConstituentCount: totalConstituents,
                   description: $"generic+union({paramCount},{totalConstituents}): {source}");
    }

    public static Gen<TypeAliasSourceScenario> GenTypeAliasSource()
    {
        return Gen.OneOf(
            GenSimpleAlias(),
            GenGenericAlias(),
            GenUnionAlias(),
            GenGenericWithUnionAlias());
    }

    public static Arbitrary<TypeAliasSourceScenario> ArbitraryTypeAliasSourceScenario()
    {
        return GenTypeAliasSource().ToArbitrary();
    }
}