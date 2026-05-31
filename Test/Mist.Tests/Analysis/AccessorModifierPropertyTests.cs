// Feature: properties, Property 3: Accessor Modifier Restrictiveness Validation
// **Validates: Requirements 5.2, 5.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for accessor modifier restrictiveness validation.
///
/// **Validates: Requirements 5.2, 5.3**
///
/// For any property with accessor modifier, type checker accepts iff modifier is
/// strictly more restrictive than property access level.
/// </summary>
public class AccessorModifierPropertyTests
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ─── Test data model ─────────────────────────────────────────────────

    /// <summary>
    /// Represents a test case with a property modifier and an accessor modifier.
    /// </summary>
#pragma warning disable CA1034
    public record AccessorModifierTestCase(
        string Description,
        ImmutableArray<ModifierNode> PropertyModifiers,
        ModifierNode? AccessorModifier,
        bool ExpectValid)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates valid accessor modifier combinations (accessor more restrictive than property).
    /// e.g., public property with private set.
    /// </summary>
    private static Gen<AccessorModifierTestCase> GenValidModifierCombination()
    {
        // public property + private accessor = valid
        return Gen.Constant(new AccessorModifierTestCase(
            "public property + private set",
            ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            new PrivateModifierModifierNode(),
            ExpectValid: true));
    }

    /// <summary>
    /// Generates cases where accessor modifier is NOT more restrictive (same level or less).
    /// e.g., private property with public set, or same-level modifier.
    /// </summary>
    private static Gen<AccessorModifierTestCase> GenInvalidModifierCombination()
    {
        return Gen.Elements(
            // private property + public accessor = invalid (less restrictive)
            new AccessorModifierTestCase(
                "private property + public set",
                ImmutableArray.Create<ModifierNode>(new PrivateModifierModifierNode()),
                new PublicModifierModifierNode(),
                ExpectValid: false),
            // public property + public accessor = invalid (same level)
            new AccessorModifierTestCase(
                "public property + public set",
                ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
                new PublicModifierModifierNode(),
                ExpectValid: false));
    }

    private static Gen<AccessorModifierTestCase> GenModifierTestCase()
    {
        return Gen.OneOf(GenValidModifierCombination(), GenInvalidModifierCombination());
    }

    public static Arbitrary<AccessorModifierTestCase> ArbitraryAccessorModifierTestCase() =>
        GenModifierTestCase().ToArbitrary();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static (ISemanticModel model, DiagnosticBag diagnostics) BuildAndAnalyze(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);
        var source = new SourceText("test.mist", printed);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(source)));
        var semanticDiagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        var model = builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            semanticDiagnostics);

        // Run the type checker analyzer to get MIST_TC_* diagnostics
        var analyzerDiagnostics = new DiagnosticBag();
        var typeChecker = new TypeCheckerAnalyzer();
        typeChecker.Analyze(model, analyzerDiagnostics);

        // Merge all diagnostics
        foreach (var d in analyzerDiagnostics.ToImmutableArray())
            semanticDiagnostics.Add(d);

        return (model, semanticDiagnostics);
    }

    private static CompilationUnitNode MakePropertyWithModifiers(
        ImmutableArray<ModifierNode> propertyModifiers,
        ModifierNode? accessorModifier)
    {
        var accessors = ImmutableArray.Create<AccessorDeclarationNode>(
            new AutoGetAccessorAccessorDeclarationNode(),
            new AutoSetAccessorAccessorDeclarationNode
            {
                AccessorModifier = accessorModifier,
            });

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = propertyModifiers,
            Type = new TypeReferenceNode { First = new PrimitiveSingleTypeRefSingleTypeRefNode
            {
                PrimitiveType = new IntTypePrimitiveTypeNode(),
            } },
            Name = Tok("Value"),
            Accessors = accessors,
        };

        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Accessor Modifier Restrictiveness Validation
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AccessorModifierPropertyTests) })]
    public Property AccessorModifier_ValidCombinationsProduceNoDiagnostic(AccessorModifierTestCase testCase)
    {
        if (!testCase.ExpectValid)
            return true.ToProperty(); // Skip invalid cases in this test

        var ast = MakePropertyWithModifiers(testCase.PropertyModifiers, testCase.AccessorModifier);
        var (model, diagnostics) = BuildAndAnalyze(ast);

        var restrictivenessDiags = diagnostics.ToImmutableArray()
            .Where(d => d.Id.Value == "MIST_TC_0022")
            .ToList();

        return (restrictivenessDiags.Count == 0).ToProperty()
            .Label($"Expected no MIST_TC_0022 for valid combination '{testCase.Description}', " +
                   $"but got {restrictivenessDiags.Count} diagnostics");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(AccessorModifierPropertyTests) })]
    public Property AccessorModifier_InvalidCombinationsProduceDiagnostic(AccessorModifierTestCase testCase)
    {
        if (testCase.ExpectValid)
            return true.ToProperty(); // Skip valid cases in this test

        var ast = MakePropertyWithModifiers(testCase.PropertyModifiers, testCase.AccessorModifier);
        var (model, diagnostics) = BuildAndAnalyze(ast);

        var restrictivenessDiags = diagnostics.ToImmutableArray()
            .Where(d => d.Id.Value == "MIST_TC_0022")
            .ToList();

        return (restrictivenessDiags.Count > 0).ToProperty()
            .Label($"Expected MIST_TC_0022 for invalid combination '{testCase.Description}', " +
                   $"but got no diagnostics");
    }
}
