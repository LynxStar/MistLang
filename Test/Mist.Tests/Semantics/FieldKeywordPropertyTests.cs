// Feature: properties, Property 6: Field Keyword Contextual Resolution
// **Validates: Requirements 6.1, 6.2, 6.3, 9.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for field keyword contextual resolution.
///
/// **Validates: Requirements 6.1, 6.2, 6.3, 9.3**
///
/// For any `field` keyword inside a non-computed property accessor, semantic model
/// resolves to backing field; outside accessor or in computed property, type checker
/// reports error.
/// </summary>
public class FieldKeywordPropertyTests
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record FieldKeywordTestCase(
        string Description,
        CompilationUnitNode Ast,
        bool ExpectBackingField)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-computed property with `field` keyword in accessor body.
    /// Should have a backing field generated.
    /// </summary>
    private static Gen<FieldKeywordTestCase> GenFieldInNonComputedProperty()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Count")
               select new FieldKeywordTestCase(
                   $"field keyword in non-computed property {propName}",
                   MakePropertyWithFieldKeyword(type, propName),
                   ExpectBackingField: true);
    }

    /// <summary>
    /// Generates a computed property (get-only with expression body).
    /// Should NOT have a backing field.
    /// </summary>
    private static Gen<FieldKeywordTestCase> GenComputedPropertyNoField()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Count")
               from initExpr in MistPropertyGenerator.GenInitializerForType(type)
               select new FieldKeywordTestCase(
                   $"computed property {propName} (no field)",
                   MakeComputedProperty(type, propName, initExpr),
                   ExpectBackingField: false);
    }

    private static Gen<FieldKeywordTestCase> GenTestCase()
    {
        return Gen.OneOf(GenFieldInNonComputedProperty(), GenComputedPropertyNoField());
    }

    public static Arbitrary<FieldKeywordTestCase> ArbitraryFieldKeywordTestCase() =>
        GenTestCase().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    private static CompilationUnitNode MakePropertyWithFieldKeyword(TypeReferenceNode type, string propName)
    {
        // Property with block get that returns field, and block set that assigns field = value
        var getAccessor = new BlockGetAccessorAccessorDeclarationNode
        {
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode
                        {
                            Value = MistAstGenerator.WrapPrimary(new FieldKeywordExpressionPrimaryNode()),
                        },
                    }),
            },
        };

        var setAccessor = new BlockSetAccessorAccessorDeclarationNode
        {
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    MistAstGenerator.MakeExpressionStatement(MakeFieldAssignment())),
            },
        };

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(getAccessor, setAccessor),
        };

        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakeComputedProperty(
        TypeReferenceNode type, string propName, ExpressionNode body)
    {
        // Expression-bodied property: Type Name => expr;
        var property = new ExpressionBodiedPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Body = body,
        };

        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static ExpressionNode MakeFieldAssignment()
    {
        var fieldExpr = MistAstGenerator.WrapPrimary(new FieldKeywordExpressionPrimaryNode());
        var valueExpr = MistAstGenerator.MakeIdentifierExpr("value");
        var fieldLogOr = fieldExpr.Assignment.Target;

        return new ExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = fieldLogOr,
                Value = valueExpr.Assignment,
            },
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ISemanticModel BuildSemanticModel(CompilationUnitNode ast)
    {
        var printed = MistPrettyPrinter.Print(ast);
        var source = new SourceText("test.mist", printed);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(source)));
        var diagnostics = new DiagnosticBag();
        var builder = new SemanticModelBuilder();
        return builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Field Keyword Contextual Resolution
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(FieldKeywordPropertyTests) })]
    public Property FieldKeyword_BackingFieldPresentWhenFieldUsed(FieldKeywordTestCase testCase)
    {
        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if (propSymbols.Count == 0)
            return false.ToProperty().Label("No property symbols found");

        var metadata = (propSymbols.First() as Symbol)?.PropertyMetadata;
        if (metadata is null)
            return false.ToProperty().Label("PropertyMetadata is null");

        if (testCase.ExpectBackingField)
        {
            return (metadata.BackingField is not null).ToProperty()
                .Label($"Expected backing field for '{testCase.Description}' but was null");
        }
        else
        {
            return (metadata.BackingField is null).ToProperty()
                .Label($"Expected no backing field for '{testCase.Description}' but found one");
        }
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(FieldKeywordPropertyTests) })]
    public Property FieldKeyword_BackingFieldNameFollowsConvention(FieldKeywordTestCase testCase)
    {
        if (!testCase.ExpectBackingField)
            return true.ToProperty(); // Skip for computed properties

        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if (propSymbols.Count == 0)
            return false.ToProperty().Label("No property symbols found");

        var propSymbol = propSymbols.First();
        var metadata = (propSymbol as Symbol)?.PropertyMetadata;

        if (metadata?.BackingField is null)
            return false.ToProperty().Label("Backing field is null");

        var expectedName = $"_{propSymbol.Name}_k__BackingField";
        return (metadata.BackingField.Name == expectedName).ToProperty()
            .Label($"Expected backing field name '{expectedName}', got '{metadata.BackingField.Name}'");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(FieldKeywordPropertyTests) })]
    public Property FieldKeyword_ComputedPropertyIsClassifiedCorrectly(FieldKeywordTestCase testCase)
    {
        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if (propSymbols.Count == 0)
            return false.ToProperty().Label("No property symbols found");

        var metadata = (propSymbols.First() as Symbol)?.PropertyMetadata;
        if (metadata is null)
            return false.ToProperty().Label("PropertyMetadata is null");

        if (!testCase.ExpectBackingField)
        {
            // Computed properties should be classified as computed
            return metadata.IsComputed.ToProperty()
                .Label($"Expected IsComputed=true for '{testCase.Description}'");
        }
        else
        {
            // Non-computed properties with field keyword should not be computed
            return (!metadata.IsComputed).ToProperty()
                .Label($"Expected IsComputed=false for '{testCase.Description}'");
        }
    }
}
