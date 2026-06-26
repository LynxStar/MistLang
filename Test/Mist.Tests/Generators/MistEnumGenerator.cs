// Generators for Mist enum declaration source strings.
// Produces random but structurally valid enum source text for property-based testing.
// Feature: enums, Property 1: Enum declaration round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 12.1**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated enum source string for round-trip testing.
/// Carries the source text along with a human-readable description.
/// </summary>
public sealed class EnumSourceScenario
{
    /// <summary>The complete enum declaration source string.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public EnumSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString() => Description;
}

/// <summary>
/// FsCheck generators for Mist enum declaration source strings.
/// Generates random valid enum declarations covering:
/// <list type="bullet">
///   <item>Access modifiers (public, private, none)</item>
///   <item>Optional type annotations (e.g., <c>: int</c>, <c>: string</c>)</item>
///   <item>Name-only members (no value)</item>
///   <item>Valued members with integer or string literals</item>
///   <item>Mixed members (some with values, some without)</item>
///   <item>Trailing comma variants</item>
///   <item>Empty enums (no members)</item>
/// </list>
/// </summary>
public static class MistEnumGenerator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Direction", "Color", "Status", "Priority", "Level", "Mode", "State"];

    private static readonly string[] MemberNames =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low", "Medium"];

    private static readonly string[] TypeAnnotations =
        ["int", "string", "byte", "long", "short"];

    private static readonly string[] Modifiers =
        ["", "public ", "private "];

    // ─── Value generators ───────────────────────────────────────────────────

    private static Gen<string> GenIntValue() =>
        Gen.Choose(0, 999).Select(n => n.ToString());

    private static Gen<string> GenStringValue() =>
        Gen.Elements("\"N\"", "\"S\"", "\"E\"", "\"W\"", "\"red\"", "\"green\"", "\"blue\"",
                     "\"active\"", "\"pending\"", "\"high\"", "\"low\"");

    // ─── Member generators ──────────────────────────────────────────────────

    /// <summary>Generates a name-only enum member (e.g., "North").</summary>
    private static Gen<string> GenNameOnlyMember() =>
        Gen.Elements(MemberNames);

    /// <summary>Generates a valued enum member with an integer value (e.g., "North = 0").</summary>
    private static Gen<string> GenIntValuedMember() =>
        from name in Gen.Elements(MemberNames)
        from value in GenIntValue()
        select $"{name} = {value}";

    /// <summary>Generates a valued enum member with a string value (e.g., "North = \"N\"").</summary>
    private static Gen<string> GenStringValuedMember() =>
        from name in Gen.Elements(MemberNames)
        from value in GenStringValue()
        select $"{name} = {value}";

    // ─── Enum declaration generators ────────────────────────────────────────

    /// <summary>
    /// Generates an empty enum: <c>enum Direction { }</c>
    /// </summary>
    public static Gen<EnumSourceScenario> GenEmptyEnum()
    {
        return from modifier in Gen.Elements(Modifiers)
               from name in Gen.Elements(EnumNames)
               from hasType in Gen.Elements(true, false)
               from typeAnnotation in Gen.Elements(TypeAnnotations)
               let typePart = hasType ? $" : {typeAnnotation}" : ""
               let source = $"{modifier}enum {name}{typePart} {{\n}}"
               select new EnumSourceScenario(source, $"empty: {source.Replace("\n", " ")}");
    }

    /// <summary>
    /// Generates a name-only enum (all members have no values):
    /// <c>enum Color { Red, Green, Blue }</c>
    /// </summary>
    public static Gen<EnumSourceScenario> GenNameOnlyEnum()
    {
        return from modifier in Gen.Elements(Modifiers)
               from name in Gen.Elements(EnumNames)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.ArrayOf(GenNameOnlyMember(), memberCount)
               from trailingComma in Gen.Elements(true, false)
               let distinctMembers = members.Distinct().ToArray()
               let memberList = string.Join(",\n    ", distinctMembers)
               let trailing = trailingComma ? "," : ""
               let source = $"{modifier}enum {name} {{\n    {memberList}{trailing}\n}}"
               select new EnumSourceScenario(source, $"name-only({distinctMembers.Length}): {modifier}enum {name}");
    }

    /// <summary>
    /// Generates an enum with integer-valued members:
    /// <c>enum Priority : int { High = 1, Medium = 2, Low = 3 }</c>
    /// </summary>
    public static Gen<EnumSourceScenario> GenIntValuedEnum()
    {
        return from modifier in Gen.Elements(Modifiers)
               from name in Gen.Elements(EnumNames)
               from hasType in Gen.Elements(true, false)
               from typeAnnotation in Gen.Elements("int", "byte", "long", "short")
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.ArrayOf(GenIntValuedMember(), memberCount)
               from trailingComma in Gen.Elements(true, false)
               let typePart = hasType ? $" : {typeAnnotation}" : ""
               let memberList = string.Join(",\n    ", members)
               let trailing = trailingComma ? "," : ""
               let source = $"{modifier}enum {name}{typePart} {{\n    {memberList}{trailing}\n}}"
               select new EnumSourceScenario(source, $"int-valued({memberCount}): {modifier}enum {name}{typePart}");
    }

    /// <summary>
    /// Generates an enum with string-valued members:
    /// <c>enum Direction : string { North = "N", South = "S" }</c>
    /// </summary>
    public static Gen<EnumSourceScenario> GenStringValuedEnum()
    {
        return from modifier in Gen.Elements(Modifiers)
               from name in Gen.Elements(EnumNames)
               from hasType in Gen.Elements(true, false)
               from memberCount in Gen.Choose(1, 4)
               from members in Gen.ArrayOf(GenStringValuedMember(), memberCount)
               from trailingComma in Gen.Elements(true, false)
               let typePart = hasType ? " : string" : ""
               let memberList = string.Join(",\n    ", members)
               let trailing = trailingComma ? "," : ""
               let source = $"{modifier}enum {name}{typePart} {{\n    {memberList}{trailing}\n}}"
               select new EnumSourceScenario(source, $"string-valued({memberCount}): {modifier}enum {name}{typePart}");
    }

    /// <summary>
    /// Generates an enum with mixed members (some with values, some without):
    /// <c>enum Status { Active: 1, Pending, Inactive: 3 }</c>
    /// </summary>
    public static Gen<EnumSourceScenario> GenMixedMemberEnum()
    {
        var mixedMember = Gen.OneOf(GenNameOnlyMember(), GenIntValuedMember());

        return from modifier in Gen.Elements(Modifiers)
               from name in Gen.Elements(EnumNames)
               from hasType in Gen.Elements(true, false)
               from typeAnnotation in Gen.Elements("int", "byte", "long")
               from memberCount in Gen.Choose(2, 5)
               from members in Gen.ArrayOf(mixedMember, memberCount)
               from trailingComma in Gen.Elements(true, false)
               let typePart = hasType ? $" : {typeAnnotation}" : ""
               let memberList = string.Join(",\n    ", members)
               let trailing = trailingComma ? "," : ""
               let source = $"{modifier}enum {name}{typePart} {{\n    {memberList}{trailing}\n}}"
               select new EnumSourceScenario(source, $"mixed({memberCount}): {modifier}enum {name}{typePart}");
    }

    // ─── Combined generator ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a random enum source scenario covering all variants.
    /// </summary>
    public static Gen<EnumSourceScenario> GenEnumSource()
    {
        return Gen.OneOf(
            GenEmptyEnum(),
            GenNameOnlyEnum(),
            GenIntValuedEnum(),
            GenStringValuedEnum(),
            GenMixedMemberEnum());
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────────

    /// <summary>
    /// FsCheck Arbitrary for <see cref="EnumSourceScenario"/>.
    /// Register with <c>[Property(Arbitrary = new[] { typeof(MistEnumGenerator) })]</c>.
    /// </summary>
    public static Arbitrary<EnumSourceScenario> ArbitraryEnumSourceScenario() =>
        GenEnumSource().ToArbitrary();
}