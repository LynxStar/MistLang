// Generators for Mist type alias declaration source strings.
// Produces random but structurally valid type alias source text for property-based testing.
// Feature: type-aliases, Property 1: Parsing produces correct AST structure
// **Validates: Requirements 2.1, 2.2, 2.3**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated type alias source string.
/// Carries the source text along with structural information about what was generated,
/// enabling property tests to verify AST fields against expected values.
/// </summary>
public sealed class TypeAliasSourceScenario
{
    /// <summary>The complete type alias source string (e.g., "type Foo = int;").</summary>
    public string Source { get; }

    /// <summary>The alias name (e.g., "Foo").</summary>
    public string AliasName { get; }

    /// <summary>The number of type parameters (0 for non-generic aliases).</summary>
    public int TypeParameterCount { get; }

    /// <summary>The type parameter names (empty for non-generic aliases).</summary>
    public IReadOnlyList<string> TypeParameterNames { get; }

    /// <summary>The number of union constituents (1 for non-union targets).</summary>
    public int UnionConstituentCount { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
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

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist type alias source strings.
/// Generates random valid type alias declarations covering:
/// <list type="bullet">
///   <item>Simple aliases (e.g., <c>type Foo = int;</c>)</item>
///   <item>Generic aliases with 1–5 type parameters (e.g., <c>type Bar&lt;T, U&gt; = List&lt;T&gt;;</c>)</item>
///   <item>Union target types with 2–5 constituents (e.g., <c>type Result = int | string | bool;</c>)</item>
///   <item>Combinations: generic aliases with union targets</item>
/// </list>
/// </summary>
public static class MistTypeAliasGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] AliasNames =
        ["Foo", "Bar", "Baz", "Qux", "MyType", "Result", "Value", "Item", "Alias", "Wrapper"];

    private static readonly string[] SimpleTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W", "K"];

    private static readonly string[] GenericContainerTypes =
        ["List", "Map", "Set", "Option", "Container", "Wrapper"];

    // ─── Simple alias generator ─────────────────────────────────────────────

    /// <summary>
    /// Generates a simple type alias: <c>type Foo = int;</c>
    /// </summary>
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

    // ─── Generic alias generator ────────────────────────────────────────────

    /// <summary>
    /// Generates a generic type alias with 1–5 type parameters:
    /// <c>type Bar&lt;T, U&gt; = List&lt;T, U&gt;;</c>
    /// </summary>
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

    // ─── Union target type generator ────────────────────────────────────────

    /// <summary>
    /// Generates a union type alias with 2–5 constituents:
    /// <c>type Result = int | string | bool;</c>
    /// </summary>
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

    // ─── Generic alias with union target generator ──────────────────────────

    /// <summary>
    /// Generates a generic alias with a union target type:
    /// <c>type Result&lt;T, U&gt; = T | U | string;</c>
    /// </summary>
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

    // ─── Combined generator ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a random type alias source scenario covering all variants:
    /// simple, generic (1–5 params), union (2–5 constituents), and combinations.
    /// </summary>
    public static Gen<TypeAliasSourceScenario> GenTypeAliasSource()
    {
        return Gen.OneOf(
            GenSimpleAlias(),
            GenGenericAlias(),
            GenUnionAlias(),
            GenGenericWithUnionAlias());
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    /// <summary>
    /// FsCheck Arbitrary for <see cref="TypeAliasSourceScenario"/>.
    /// Register this with <c>[Property(Arbitrary = new[] { typeof(MistTypeAliasGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<TypeAliasSourceScenario> ArbitraryTypeAliasSourceScenario() =>
        GenTypeAliasSource().ToArbitrary();
}
