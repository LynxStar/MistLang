// Generators for Mist ownership-v2 declaration source strings.
// Produces random but structurally valid v2 ownership source text for round-trip property testing.
// Feature: ownership-v2, Property 11: Round-trip parsing
// **Validates: Requirements 17.1, 17.2, 17.3**

using FsCheck;
using FsCheck.Fluent;

namespace Mist.Tests.Generators;

/// <summary>
/// Metadata about a generated ownership-v2 source string for round-trip testing.
/// Carries the source text along with a human-readable description.
/// </summary>
public sealed class OwnershipV2SourceScenario
{
    /// <summary>The complete ownership-v2 source string.</summary>
    public string Source { get; }

    /// <summary>Human-readable description of the scenario kind.</summary>
    public string Description { get; }

    public OwnershipV2SourceScenario(string source, string description)
    {
        Source = source;
        Description = description;
    }

    public override string ToString()
    {
        return Description;
    }
}

/// <summary>
/// FsCheck generators for Mist ownership-v2 source strings. Focuses on the three
/// v2 constructs called out by Property 11 (Requirement 17):
/// <list type="bullet">
///   <item>Owned type declarations (<c>owned class</c> / <c>owned struct</c>) — cooperation contract</item>
///   <item>Owned bindings (<c>owned var x = ...</c>, <c>owned T x = ...</c>)</item>
///   <item><c>IClonable&lt;T&gt;</c> implementations (<c>class C : IClonable&lt;C&gt; { owned C Clone() {...} }</c>)</item>
/// </list>
/// Deliberately avoids the removed v1 <c>owned</c> field modifier (Requirement 11) so that the
/// generated source reflects valid v2 syntax.
/// </summary>
public static class MistOwnershipV2Generator
{
    // ─── Name pools ─────────────────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Foo", "Bar", "Baz", "Widget", "Node", "Item"];

    private static readonly string[] VarNames =
        ["x", "y", "z", "item", "node", "value"];

    private static readonly string[] FieldNames =
        ["Data", "Child", "Next", "Content", "Payload"];

    private static readonly string[] Modifiers =
        ["", "public ", "private "];

    // ─── Owned type declaration generators (cooperation contract) ────────────

    /// <summary>
    /// Generates an owned class declaration with plain (non-owned-modifier) fields:
    /// <c>owned class Foo { public int Data; }</c>
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnedClassDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from className in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"{modifier}owned class {className}\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"owned-class: {modifier}owned class {className}");
    }

    /// <summary>
    /// Generates an owned struct declaration with plain fields:
    /// <c>owned struct Widget { public int Data; }</c>
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnedStructDeclaration()
    {
        return from modifier in Gen.Elements(Modifiers)
               from structName in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"{modifier}owned struct {structName}\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"owned-struct: {modifier}owned struct {structName}");
    }

    // ─── Owned binding generators ────────────────────────────────────────────

    /// <summary>
    /// Generates an inferred owned binding: <c>owned var x = new Foo();</c>
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnedInferredBinding()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"owned var {varName} = new {typeName}();")
               select new OwnershipV2SourceScenario(
                   source, $"owned-binding-inferred: owned var {varName} = new {typeName}()");
    }

    /// <summary>
    /// Generates a typed owned binding: <c>owned Foo x = new Foo();</c>
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnedTypedBinding()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"owned {typeName} {varName} = new {typeName}();")
               select new OwnershipV2SourceScenario(
                   source, $"owned-binding-typed: owned {typeName} {varName} = new {typeName}()");
    }

    /// <summary>
    /// Generates a typed owned binding initialized from <c>default</c>:
    /// <c>owned Foo x = default;</c>
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnedDefaultBinding()
    {
        return from typeName in Gen.Elements(TypeNames)
               from varName in Gen.Elements(VarNames)
               let source = WrapInClassMethod($"owned {typeName} {varName} = default;")
               select new OwnershipV2SourceScenario(
                   source, $"owned-binding-default: owned {typeName} {varName} = default");
    }

    // ─── IClonable implementation generators ─────────────────────────────────

    /// <summary>
    /// Generates a class that implements <c>IClonable&lt;T&gt;</c> with an <c>owned T Clone()</c>:
    /// <code>
    /// public class Foo : IClonable&lt;Foo&gt;
    /// {
    ///     public int Code;
    ///     public owned Foo Clone()
    ///     {
    ///         return new Foo();
    ///     }
    /// }
    /// </code>
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenIClonableImplementation()
    {
        return from className in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"public class {className} : IClonable<{className}>\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   $"    public owned {className} Clone()\n" +
                   "    {\n" +
                   $"        return new {className}();\n" +
                   "    }\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"iclonable-impl: {className} : IClonable<{className}>");
    }

    /// <summary>
    /// Generates an owned class that implements <c>IClonable&lt;T&gt;</c> — the
    /// cooperation-contract + clone-bridge cross product.
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnedIClonableImplementation()
    {
        return from className in Gen.Elements(TypeNames)
               from fieldName in Gen.Elements(FieldNames)
               let source =
                   $"public owned class {className} : IClonable<{className}>\n" +
                   "{\n" +
                   $"    public int {fieldName};\n" +
                   $"    public owned {className} Clone()\n" +
                   "    {\n" +
                   $"        return new {className}();\n" +
                   "    }\n" +
                   "}\n"
               select new OwnershipV2SourceScenario(
                   source, $"owned-iclonable-impl: owned {className} : IClonable<{className}>");
    }

    // ─── Combined generator ───────────────────────────────────────────────────

    /// <summary>
    /// Generates a random ownership-v2 source scenario covering all v2 constructs
    /// in Property 11: owned type decls, owned bindings, and IClonable impls.
    /// </summary>
    public static Gen<OwnershipV2SourceScenario> GenOwnershipV2Source()
    {
        return Gen.OneOf(
            GenOwnedClassDeclaration(),
            GenOwnedStructDeclaration(),
            GenOwnedInferredBinding(),
            GenOwnedTypedBinding(),
            GenOwnedDefaultBinding(),
            GenIClonableImplementation(),
            GenOwnedIClonableImplementation());
    }

    // ─── FsCheck Arbitrary registration ───────────────────────────────────────

    /// <summary>
    /// FsCheck Arbitrary for <see cref="OwnershipV2SourceScenario"/>.
    /// </summary>
    public static Arbitrary<OwnershipV2SourceScenario> ArbitraryOwnershipV2SourceScenario()
    {
        return GenOwnershipV2Source().ToArbitrary();
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Wraps a statement inside a class with a method body, with a Standard using.</summary>
    private static string WrapInClassMethod(string statement)
    {
        return "class TestClass\n" +
        "{\n" +
        "    void Run()\n" +
        "    {\n" +
        $"        {statement}\n" +
        "    }\n" +
        "}\n";
    }
}