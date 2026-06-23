// Feature: abstract-type-unification, Property 1: Abstract declaration parse round-trip
// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 1.7**

using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Generated;

namespace Mist.Tests.AbstractTypeUnification;

/// <summary>
/// Property-based tests for abstract type declaration parse round-trip.
///
/// Property 1: For any valid abstract type declaration (varying identifier names,
/// generic parameters, constraint clauses, and member combinations of slots,
/// defaults, and property slots), parsing the source text SHALL produce an
/// AbstractDeclarationNode with the correct name, type parameters, and member
/// classifications (slots vs defaults vs property slots).
///
/// **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 1.7**
/// </summary>
public class ParseRoundTripPropertyTests
{
    private static readonly Mist.Lexing.MistLexer Lexer = new();
    private static readonly Mist.Parsing.MistParser Parser = new();

    // ─── Name pools ─────────────────────────────────────────────────────

    private static readonly string[] AbstractTypeNames =
        ["IFoo", "IBar", "IService", "IHandler", "IProvider", "IFactory", "IValidator"];

    private static readonly string[] TypeParamNames =
        ["T", "U", "V", "W"];

    private static readonly string[] ConstraintTypes =
        ["class", "struct"];

    private static readonly string[] ReturnTypes =
        ["int", "string", "bool", "void"];

    private static readonly string[] MethodNames =
        ["DoWork", "Process", "Execute", "Handle", "Validate", "Transform"];

    private static readonly string[] PropertyNames =
        ["Name", "Value", "Count", "Label", "Status"];

    private static readonly string[] PropertyTypes =
        ["int", "string", "bool"];

    // ─── Scenario record ────────────────────────────────────────────────

    /// <summary>
    /// Describes a generated abstract type declaration for round-trip testing.
    /// </summary>
    internal sealed class AbstractTypeScenario
    {
        public string Source { get; }
        public string ExpectedName { get; }
        public int ExpectedTypeParamCount { get; }
        public int ExpectedSlotCount { get; }
        public int ExpectedDefaultCount { get; }
        public int ExpectedPropertySlotCount { get; }
        public string Description { get; }

        public AbstractTypeScenario(
            string source,
            string expectedName,
            int expectedTypeParamCount,
            int expectedSlotCount,
            int expectedDefaultCount,
            int expectedPropertySlotCount,
            string description)
        {
            Source = source;
            ExpectedName = expectedName;
            ExpectedTypeParamCount = expectedTypeParamCount;
            ExpectedSlotCount = expectedSlotCount;
            ExpectedDefaultCount = expectedDefaultCount;
            ExpectedPropertySlotCount = expectedPropertySlotCount;
            Description = description;
        }

        public override string ToString() => Description;
    }

    // ─── Generators ─────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random abstract type scenario with varying combinations
    /// of type parameters, constraints, slots, defaults, and property slots.
    /// </summary>
    private static Gen<AbstractTypeScenario> GenScenario()
    {
        return from name in Gen.Elements(AbstractTypeNames)
               from typeParamCount in Gen.Choose(0, 3)
               from constraintCount in Gen.Choose(0, Math.Min(typeParamCount, 2))
               from slotCount in Gen.Choose(0, 3)
               from defaultCount in Gen.Choose(0, 2)
               from propertySlotCount in Gen.Choose(0, 2)
               from slots in GenSlots(slotCount)
               from defaults in GenDefaults(defaultCount)
               from propertySlots in GenPropertySlots(propertySlotCount)
               let typeParams = GenTypeParamString(typeParamCount)
               let constraints = GenConstraintString(typeParamCount, constraintCount)
               let members = string.Join("\n", slots.Concat(defaults).Concat(propertySlots))
               let source = BuildSource(name, typeParams, constraints, members)
               let description = $"{name}{typeParams} slots={slotCount} defaults={defaultCount} props={propertySlotCount} constraints={constraintCount}"
               select new AbstractTypeScenario(
                   source, name, typeParamCount,
                   slotCount, defaultCount, propertySlotCount,
                   description);
    }

    /// <summary>Generates method slot declarations (signature with semicolon, no body).</summary>
    private static Gen<string[]> GenSlots(int count)
    {
        if (count == 0)
            return Gen.Constant(Array.Empty<string>());

        var slotGen =
            from returnType in Gen.Elements(ReturnTypes)
            from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
            select $"    {returnType} Slot{methodIdx}();";

        return Gen.ArrayOf(slotGen, count);
    }

    /// <summary>Generates default method declarations (method with body).</summary>
    private static Gen<string[]> GenDefaults(int count)
    {
        if (count == 0)
            return Gen.Constant(Array.Empty<string>());

        var defaultGen =
            from returnType in Gen.Elements(new[] { "int", "string", "bool" })
            from methodIdx in Gen.Choose(0, MethodNames.Length - 1)
            let body = returnType switch
            {
                "int" => "return 42;",
                "bool" => "return true;",
                _ => "return \"default\";"
            }
            select $"    {returnType} Default{methodIdx}() {{\n        {body}\n    }}";

        return Gen.ArrayOf(defaultGen, count);
    }

    /// <summary>Generates property slot declarations.</summary>
    private static Gen<string[]> GenPropertySlots(int count)
    {
        if (count == 0)
            return Gen.Constant(Array.Empty<string>());

        var propGen =
            from propType in Gen.Elements(PropertyTypes)
            from propIdx in Gen.Choose(0, PropertyNames.Length - 1)
            from hasSet in Gen.Elements(true, false)
            let accessors = hasSet ? "get; set;" : "get;"
            select $"    {propType} Prop{propIdx} {{ {accessors} }}";

        return Gen.ArrayOf(propGen, count);
    }

    // ─── Source builders ────────────────────────────────────────────────

    private static string GenTypeParamString(int count)
    {
        if (count == 0) return "";
        var names = TypeParamNames.Take(count);
        return $"<{string.Join(", ", names)}>";
    }

    private static string GenConstraintString(int typeParamCount, int constraintCount)
    {
        if (constraintCount == 0 || typeParamCount == 0) return "";

        var parts = new List<string>();
        for (var i = 0; i < constraintCount && i < typeParamCount; i++)
        {
            var constraintType = ConstraintTypes[i % ConstraintTypes.Length];
            parts.Add($"where {TypeParamNames[i]} : {constraintType}");
        }

        return " " + string.Join(" ", parts);
    }

    private static string BuildSource(string name, string typeParams, string constraints, string members)
    {
        var body = string.IsNullOrWhiteSpace(members) ? "" : $"\n{members}\n";
        return $"abstract {name}{typeParams}{constraints}\n{{{body}}}";
    }

    // ─── Parse helper ───────────────────────────────────────────────────

    private static (CompilationUnitNode? Root, bool HasErrors) ParseSource(string source)
    {
        var sourceText = new SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var hasErrors = bag.ToImmutableArray()
            .Any(d => d.Severity == DiagnosticSeverity.Error);

        return (result.Root as CompilationUnitNode, hasErrors);
    }

    /// <summary>
    /// Navigates the AST to find the AbstractDeclarationNode.
    /// </summary>
    private static AbstractDeclarationNode? FindAbstractDeclaration(CompilationUnitNode cu)
    {
        foreach (var item in cu.Items)
        {
            if (item is TypeItemTopLevelItemNode typeItem &&
                typeItem.TypeDeclaration is AbstractTypeTypeDeclarationNode abstractTypeNode)
            {
                return abstractTypeNode.AbstractDeclaration;
            }
        }

        return null;
    }

    /// <summary>
    /// Counts type parameters from a TypeParameterListNode.
    /// The first param is in TypeParameter, additional ones are in TypeParameters array.
    /// </summary>
    private static int CountTypeParams(TypeParameterListNode? typeParams)
    {
        if (typeParams is null) return 0;
        return 1 + typeParams.TypeParameters.Length;
    }

    /// <summary>
    /// Counts and classifies members in an abstract declaration.
    /// Returns (slotCount, defaultCount, propertySlotCount).
    /// </summary>
    private static (int Slots, int Defaults, int PropertySlots) ClassifyMembers(
        System.Collections.Immutable.ImmutableArray<AbstractMemberDeclarationNode> members)
    {
        var slots = 0;
        var defaults = 0;
        var propertySlots = 0;

        foreach (var member in members)
        {
            switch (member)
            {
                case AbstractMethodMemberAbstractMemberDeclarationNode:
                    slots++;
                    break;
                case AbstractDefaultMethodMemberAbstractMemberDeclarationNode:
                    defaults++;
                    break;
                case AbstractPropertyMemberAbstractMemberDeclarationNode:
                    propertySlots++;
                    break;
            }
        }

        return (slots, defaults, propertySlots);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 1: Abstract declaration parse round-trip
    //
    // **Validates: Requirements 1.1, 1.2, 1.3, 1.5, 1.6, 1.7**
    //
    // For any valid abstract type declaration (varying identifier names,
    // generic parameters, constraint clauses, and member combinations of
    // slots, defaults, and property slots), parsing the source SHALL produce
    // an AbstractDeclarationNode with:
    //  - the correct name
    //  - the correct number of type parameters
    //  - the correct member count
    //  - members correctly classified as slots vs defaults vs property slots
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AbstractDeclaration_ParseRoundTrip_ProducesCorrectAst()
    {
        var config = Config.Default.WithMaxTest(100);

        var property = Prop.ForAll(
            GenScenario().ToArbitrary(),
            scenario =>
            {
                var (root, hasErrors) = ParseSource(scenario.Source);

                if (root is null || hasErrors)
                    return false.ToProperty()
                        .Label($"Parse errors for source:\n{scenario.Source}");

                var abstractDecl = FindAbstractDeclaration(root);
                if (abstractDecl is null)
                    return false.ToProperty()
                        .Label($"No AbstractDeclarationNode found.\nSource:\n{scenario.Source}");

                // Verify name
                var actualName = abstractDecl.Name.Lexeme;
                if (actualName != scenario.ExpectedName)
                    return false.ToProperty()
                        .Label($"Name mismatch: expected '{scenario.ExpectedName}', got '{actualName}'.\nSource:\n{scenario.Source}");

                // Verify type parameter count
                var actualTypeParamCount = CountTypeParams(abstractDecl.TypeParams);
                if (actualTypeParamCount != scenario.ExpectedTypeParamCount)
                    return false.ToProperty()
                        .Label($"TypeParam count mismatch: expected {scenario.ExpectedTypeParamCount}, got {actualTypeParamCount}.\nSource:\n{scenario.Source}");

                // Verify member classification
                var (actualSlots, actualDefaults, actualPropertySlots) = ClassifyMembers(abstractDecl.Members);

                if (actualSlots != scenario.ExpectedSlotCount)
                    return false.ToProperty()
                        .Label($"Slot count mismatch: expected {scenario.ExpectedSlotCount}, got {actualSlots}.\nSource:\n{scenario.Source}");

                if (actualDefaults != scenario.ExpectedDefaultCount)
                    return false.ToProperty()
                        .Label($"Default count mismatch: expected {scenario.ExpectedDefaultCount}, got {actualDefaults}.\nSource:\n{scenario.Source}");

                if (actualPropertySlots != scenario.ExpectedPropertySlotCount)
                    return false.ToProperty()
                        .Label($"Property slot count mismatch: expected {scenario.ExpectedPropertySlotCount}, got {actualPropertySlots}.\nSource:\n{scenario.Source}");

                // Verify total member count
                var expectedTotalMembers = scenario.ExpectedSlotCount + scenario.ExpectedDefaultCount + scenario.ExpectedPropertySlotCount;
                var actualTotalMembers = abstractDecl.Members.Length;
                if (actualTotalMembers != expectedTotalMembers)
                    return false.ToProperty()
                        .Label($"Total member count mismatch: expected {expectedTotalMembers}, got {actualTotalMembers}.\nSource:\n{scenario.Source}");

                return true.ToProperty()
                    .Label($"OK: {scenario.Description}");
            });

        property.Check(config);
    }
}
