// Generators for Mist newtype declaration source strings.
// Produces random but structurally valid newtype source text for property-based testing.
// Feature: newtypes, Property 8: Pretty-printer round-trip
// **Validates: Requirements 4.1, 4.2**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated newtype declaration source string.
/// Carries the source text along with structural information about what was generated,
/// enabling property tests to verify AST fields against expected values.
/// </summary>
public sealed class NewtypeSourceScenario
{
    /// <summary>The complete newtype source string (e.g., "type Foo = new int;").</summary>
    public string Source { get; }

    /// <summary>The newtype name (e.g., "Foo").</summary>
    public string NewtypeName { get; }

    /// <summary>The source type text (e.g., "int").</summary>
    public string SourceTypeText { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public NewtypeSourceScenario(
        string source,
        string newtypeName,
        string sourceTypeText,
        string description)
    {
        Source = source;
        NewtypeName = newtypeName;
        SourceTypeText = sourceTypeText;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist newtype source strings.
/// Generates random valid newtype declarations covering:
/// <list type="bullet">
///   <item>Newtypes from primitive types (e.g., <c>type UserId = new int;</c>)</item>
///   <item>Newtypes from named types (e.g., <c>type OrderId = new EntityBase;</c>)</item>
/// </list>
/// </summary>
public static class MistNewtypeGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] NewtypeNames =
        ["UserId", "OrderId", "Email", "Amount", "Score", "Weight", "Height", "Age", "Count", "Index"];

    private static readonly string[] PrimitiveTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte", "decimal", "short"];

    private static readonly string[] NamedTypes =
        ["EntityBase", "Point", "Vector", "Color", "Config", "Settings", "Record", "Entry"];

    // ─── Primitive newtype generator ────────────────────────────────────────

    /// <summary>
    /// Generates a newtype from a primitive type: <c>type UserId = new int;</c>
    /// </summary>
    public static Gen<NewtypeSourceScenario> GenPrimitiveNewtype()
    {
        return from name in Gen.Elements(NewtypeNames)
               from sourceType in Gen.Elements(PrimitiveTypes)
               let source = $"type {name} = new {sourceType};"
               select new NewtypeSourceScenario(
                   source: source,
                   newtypeName: name,
                   sourceTypeText: sourceType,
                   description: $"primitive: {source}");
    }

    // ─── Named type newtype generator ───────────────────────────────────────

    /// <summary>
    /// Generates a newtype from a named type: <c>type OrderId = new EntityBase;</c>
    /// </summary>
    public static Gen<NewtypeSourceScenario> GenNamedNewtype()
    {
        return from name in Gen.Elements(NewtypeNames)
               from sourceType in Gen.Elements(NamedTypes)
               let source = $"type {name} = new {sourceType};"
               select new NewtypeSourceScenario(
                   source: source,
                   newtypeName: name,
                   sourceTypeText: sourceType,
                   description: $"named: {source}");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a random newtype source scenario covering all variants:
    /// primitive source types and named source types.
    /// </summary>
    public static Gen<NewtypeSourceScenario> GenNewtypeSource()
    {
        return Gen.OneOf(
            GenPrimitiveNewtype(),
            GenNamedNewtype());
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    /// <summary>
    /// FsCheck Arbitrary for <see cref="NewtypeSourceScenario"/>.
    /// Register this with <c>[Property(Arbitrary = new[] { typeof(MistNewtypeGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario() =>
        GenNewtypeSource().ToArbitrary();
}
