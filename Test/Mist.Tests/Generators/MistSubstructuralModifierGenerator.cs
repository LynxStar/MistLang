// Generators for Mist substructural-modifier-annotated type declaration source strings.
// Produces random but structurally valid source text exercising the affine/linear/relevant
// modifiers for property-based round-trip testing.
// Feature: drop-semantics, Property 21: Parsing round-trip
// **Validates: Requirements 1.1, 1.2**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated substructural-modifier-annotated source string for
/// round-trip testing. Carries the source text, the discipline keyword that must be
/// preserved through the parse/print cycle, and a human-readable description.
/// </summary>
public sealed class SubstructuralModifierSourceScenario
{
    /// <summary>The complete type-declaration source string.</summary>
    public string Source { get; }

    /// <summary>The substructural modifier keyword (affine/linear/relevant) that must survive round-trip.</summary>
    public string Modifier { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
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

/// <summary>
/// FsCheck generators for Mist type declarations carrying a substructural modifier
/// (<c>affine</c>, <c>linear</c>, <c>relevant</c>) on a <c>class</c> or <c>struct</c>,
/// optionally combined with an access/storage modifier (<c>public</c>, <c>static</c>).
/// </summary>
public static class MistSubstructuralModifierGenerator
{
    private static readonly string[] TypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Node", "Item", "Token", "Resource"];

    private static readonly string[] Disciplines =
        ["affine", "linear", "relevant"];

    private static readonly string[] Kinds =
        ["class", "struct"];

    // Leading modifiers placed before the discipline keyword. Empty means the
    // discipline modifier appears bare. "static" is only meaningful in some
    // positions, but the parser accepts it in the same modifier list position.
    private static readonly string[] LeadingModifiers =
        ["", "public ", "static ", "public static "];

    /// <summary>
    /// Generates a type declaration with a single substructural modifier, optionally
    /// preceded by access/storage modifiers, e.g. <c>public linear class ValueTask { }</c>.
    /// </summary>
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

    /// <summary>
    /// FsCheck Arbitrary for <see cref="SubstructuralModifierSourceScenario"/>.
    /// </summary>
    public static Arbitrary<SubstructuralModifierSourceScenario> ArbitrarySubstructuralModifierSourceScenario()
    {
        return GenSubstructuralTypeDeclaration().ToArbitrary();
    }
}