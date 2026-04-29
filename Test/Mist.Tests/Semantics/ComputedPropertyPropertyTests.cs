// Feature: properties, Property 8: Computed Property Classification Correctness
// **Validates: Requirements 9.1, 9.2**

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
/// Property-based tests for computed property classification correctness.
///
/// **Validates: Requirements 9.1, 9.2**
///
/// For any property, semantic model classifies as computed iff exactly one get accessor
/// with body and no set/init; computed properties have no backing field.
/// </summary>
public class ComputedPropertyPropertyTests
{
    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    // ─── Test data model ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public record ComputedPropertyTestCase(
        string Description,
        CompilationUnitNode Ast,
        bool ExpectComputed)
    {
        public override string ToString() => Description;
    }
#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates an expression-bodied property (always computed).
    /// Pattern: Type Name => expr;
    /// </summary>
    private static Gen<ComputedPropertyTestCase> GenExpressionBodiedComputed()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Total", "Label")
               from body in MistPropertyGenerator.GenInitializerForType(type)
               select new ComputedPropertyTestCase(
                   $"expression-bodied {propName} (computed)",
                   MakeExpressionBodiedProperty(type, propName, body),
                   ExpectComputed: true);
    }

    /// <summary>
    /// Generates a get-only property with block body (computed).
    /// Pattern: Type Name { get { return expr; } }
    /// </summary>
    private static Gen<ComputedPropertyTestCase> GenBlockGetOnlyComputed()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Total", "Label")
               from body in MistPropertyGenerator.GenInitializerForType(type)
               select new ComputedPropertyTestCase(
                   $"block get-only {propName} (computed)",
                   MakeBlockGetOnlyProperty(type, propName, body),
                   ExpectComputed: true);
    }

    /// <summary>
    /// Generates a get-only property with expression accessor (computed).
    /// Pattern: Type Name { get => expr; }
    /// </summary>
    private static Gen<ComputedPropertyTestCase> GenExpressionGetOnlyComputed()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Total", "Label")
               from body in MistPropertyGenerator.GenInitializerForType(type)
               select new ComputedPropertyTestCase(
                   $"expression get-only {propName} (computed)",
                   MakeExpressionGetOnlyProperty(type, propName, body),
                   ExpectComputed: true);
    }

    /// <summary>
    /// Generates a get+set auto-property (NOT computed).
    /// Pattern: Type Name { get; set; }
    /// </summary>
    private static Gen<ComputedPropertyTestCase> GenAutoGetSetNotComputed()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Total", "Label")
               select new ComputedPropertyTestCase(
                   $"auto get+set {propName} (not computed)",
                   MakeAutoGetSetProperty(type, propName),
                   ExpectComputed: false);
    }

    /// <summary>
    /// Generates a get+init auto-property (NOT computed).
    /// Pattern: Type Name { get; init; }
    /// </summary>
    private static Gen<ComputedPropertyTestCase> GenAutoGetInitNotComputed()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Total", "Label")
               select new ComputedPropertyTestCase(
                   $"auto get+init {propName} (not computed)",
                   MakeAutoGetInitProperty(type, propName),
                   ExpectComputed: false);
    }

    /// <summary>
    /// Generates a get-only auto-property (NOT computed — auto get has no body).
    /// Pattern: Type Name { get; }
    /// </summary>
    private static Gen<ComputedPropertyTestCase> GenAutoGetOnlyNotComputed()
    {
        return from type in MistTypeGenerator.GenPrimitiveTypeRef()
               from propName in Gen.Elements("Name", "Value", "Total", "Label")
               select new ComputedPropertyTestCase(
                   $"auto get-only {propName} (not computed)",
                   MakeAutoGetOnlyProperty(type, propName),
                   ExpectComputed: false);
    }

    private static Gen<ComputedPropertyTestCase> GenTestCase()
    {
        return Gen.OneOf(
            GenExpressionBodiedComputed(),
            GenBlockGetOnlyComputed(),
            GenExpressionGetOnlyComputed(),
            GenAutoGetSetNotComputed(),
            GenAutoGetInitNotComputed(),
            GenAutoGetOnlyNotComputed());
    }

    public static Arbitrary<ComputedPropertyTestCase> ArbitraryComputedPropertyTestCase() =>
        GenTestCase().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    private static CompilationUnitNode MakeExpressionBodiedProperty(
        TypeReferenceNode type, string propName, ExpressionNode body)
    {
        var property = new ExpressionBodiedPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Body = body,
        };
        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakeBlockGetOnlyProperty(
        TypeReferenceNode type, string propName, ExpressionNode returnExpr)
    {
        var getAccessor = new BlockGetAccessorAccessorDeclarationNode
        {
            Body = new BlockNode
            {
                Statements = ImmutableArray.Create<StatementNode>(
                    new ReturnStatementStatementNode
                    {
                        ReturnStatement = new ReturnStatementNode { Value = returnExpr },
                    }),
            },
        };

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(getAccessor),
        };
        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakeExpressionGetOnlyProperty(
        TypeReferenceNode type, string propName, ExpressionNode expr)
    {
        var getAccessor = new ExpressionGetAccessorAccessorDeclarationNode
        {
            Expression = expr,
        };

        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(getAccessor),
        };
        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakeAutoGetSetProperty(TypeReferenceNode type, string propName)
    {
        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(
                new AutoGetAccessorAccessorDeclarationNode(),
                new AutoSetAccessorAccessorDeclarationNode()),
        };
        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakeAutoGetInitProperty(TypeReferenceNode type, string propName)
    {
        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(
                new AutoGetAccessorAccessorDeclarationNode(),
                new AutoInitAccessorAccessorDeclarationNode()),
        };
        return MistPropertyGenerator.MakeProgramWithProperty(property);
    }

    private static CompilationUnitNode MakeAutoGetOnlyProperty(TypeReferenceNode type, string propName)
    {
        var property = new BlockPropertyPropertyDeclarationNode
        {
            Modifiers = ImmutableArray.Create<ModifierNode>(new PublicModifierModifierNode()),
            Type = type,
            Name = Tok(propName),
            Accessors = ImmutableArray.Create<AccessorDeclarationNode>(
                new AutoGetAccessorAccessorDeclarationNode()),
        };
        return MistPropertyGenerator.MakeProgramWithProperty(property);
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
            diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 8: Computed Property Classification Correctness
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ComputedPropertyPropertyTests) })]
    public Property ComputedProperty_ClassificationMatchesExpectation(ComputedPropertyTestCase testCase)
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

        return (metadata.IsComputed == testCase.ExpectComputed).ToProperty()
            .Label($"IsComputed mismatch for '{testCase.Description}': " +
                   $"got {metadata.IsComputed}, expected {testCase.ExpectComputed}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ComputedPropertyPropertyTests) })]
    public Property ComputedProperty_NoBackingFieldWhenComputed(ComputedPropertyTestCase testCase)
    {
        if (!testCase.ExpectComputed)
            return true.ToProperty(); // Only check computed properties

        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if (propSymbols.Count == 0)
            return false.ToProperty().Label("No property symbols found");

        var metadata = (propSymbols.First() as Symbol)?.PropertyMetadata;
        if (metadata is null)
            return false.ToProperty().Label("PropertyMetadata is null");

        return (metadata.BackingField is null).ToProperty()
            .Label($"Computed property '{testCase.Description}' should have no backing field");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ComputedPropertyPropertyTests) })]
    public Property ComputedProperty_HasGetAccessorOnly(ComputedPropertyTestCase testCase)
    {
        if (!testCase.ExpectComputed)
            return true.ToProperty(); // Only check computed properties

        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if (propSymbols.Count == 0)
            return false.ToProperty().Label("No property symbols found");

        var metadata = (propSymbols.First() as Symbol)?.PropertyMetadata;
        if (metadata is null)
            return false.ToProperty().Label("PropertyMetadata is null");

        var hasGetOnly = metadata.HasGet && !metadata.HasSet && !metadata.HasInit;
        return hasGetOnly.ToProperty()
            .Label($"Computed property should have get-only. " +
                   $"HasGet={metadata.HasGet}, HasSet={metadata.HasSet}, HasInit={metadata.HasInit}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(ComputedPropertyPropertyTests) })]
    public Property ComputedProperty_NonComputedAutoPropertyHasBackingField(ComputedPropertyTestCase testCase)
    {
        if (testCase.ExpectComputed)
            return true.ToProperty(); // Only check non-computed properties

        var model = BuildSemanticModel(testCase.Ast);

        var propSymbols = model.AllSymbols
            .Where(s => s.Kind == SymbolKind.Property)
            .ToList();

        if (propSymbols.Count == 0)
            return false.ToProperty().Label("No property symbols found");

        var metadata = (propSymbols.First() as Symbol)?.PropertyMetadata;
        if (metadata is null)
            return false.ToProperty().Label("PropertyMetadata is null");

        // Non-computed auto-properties should have a backing field
        var hasAutoAccessor =
            (metadata.GetAccessor?.BodyKind == AccessorBodyKind.Auto) ||
            (metadata.SetAccessor?.BodyKind == AccessorBodyKind.Auto) ||
            (metadata.InitAccessor?.BodyKind == AccessorBodyKind.Auto);

        if (hasAutoAccessor)
        {
            return (metadata.BackingField is not null).ToProperty()
                .Label($"Non-computed auto-property '{testCase.Description}' should have backing field");
        }

        return true.ToProperty();
    }
}
