// Feature: enums, Property 1: Enum declaration round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 12.1**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class EnumSourceScenario
{
    public string Source { get; }

    public string Description { get; }

    public EnumSourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistEnumGenerator
{
    private static readonly string[] EnumNames =
        ["Direction", "Color", "Status", "Priority", "Level", "Mode", "State"];

    private static readonly string[] MemberNames =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low", "Medium"];

    private static readonly string[] TypeAnnotations =
        ["int", "string", "byte", "long", "short"];

    private static readonly string[] Modifiers =
        ["", "public ", "private "];

    private static Gen<string> GenIntValue()
    {
        return Gen.Choose(0, 999).Select(n => n.ToString());
    }

    private static Gen<string> GenStringValue()
    {
        return Gen.Elements("\"N\"", "\"S\"", "\"E\"", "\"W\"", "\"red\"", "\"green\"", "\"blue\"",
                     "\"active\"", "\"pending\"", "\"high\"", "\"low\"");
    }

    private static Gen<string> GenNameOnlyMember()
    {
        return Gen.Elements(MemberNames);
    }

    private static Gen<string> GenIntValuedMember()
    {
        return from name in Gen.Elements(MemberNames)
               from value in GenIntValue()
               select $"{name} = {value}";
    }

    private static Gen<string> GenStringValuedMember()
    {
        return from name in Gen.Elements(MemberNames)
               from value in GenStringValue()
               select $"{name} = {value}";
    }

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

    public static Gen<EnumSourceScenario> GenEnumSource()
    {
        return Gen.OneOf(
            GenEmptyEnum(),
            GenNameOnlyEnum(),
            GenIntValuedEnum(),
            GenStringValuedEnum(),
            GenMixedMemberEnum());
    }

    public static Arbitrary<EnumSourceScenario> ArbitraryEnumSourceScenario()
    {
        return GenEnumSource().ToArbitrary();
    }
}