// Feature: drop-semantics, Property 21: Parsing round-trip
// **Validates: Requirements 1.1, 1.2**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

public sealed class SubstructuralModifierSourceScenario
{
    public string Source { get; }

    /// <summary>The substructural modifier keyword (affine/linear/relevant) that must survive round-trip.</summary>
    public string Modifier { get; }

    public string Description { get; }

    public SubstructuralModifierSourceScenario(string source, string modifier, string description)
    {
        Source = source;
        Modifier = modifier;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

public static class MistSubstructuralModifierGenerator
{
    private static readonly string[] TypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Node", "Item", "Token", "Resource"];

    private static readonly string[] Disciplines =
        ["affine", "linear", "relevant"];

    private static readonly string[] Kinds =
        ["class", "struct"];

    // The parser accepts "static" in this position even where it has no meaning.
    private static readonly string[] LeadingModifiers =
        ["", "public ", "static ", "public static "];

    public static Gen<SubstructuralModifierSourceScenario> GenSubstructuralTypeDeclaration()
    {
        return from discipline in Gen.Elements(Disciplines)
               from kind in Gen.Elements(Kinds)
               from leading in Gen.Elements(LeadingModifiers)
               from typeName in Gen.Elements(TypeNames)
               let source = $"{leading}{discipline} {kind} {typeName}\n{{\n}}"
               select new SubstructuralModifierSourceScenario(
                   source,
                   discipline,
                   $"{leading}{discipline} {kind} {typeName}");
    }

    public static Arbitrary<SubstructuralModifierSourceScenario> ArbitrarySubstructuralModifierSourceScenario()
    {
        return GenSubstructuralTypeDeclaration().ToArbitrary();
    }
}