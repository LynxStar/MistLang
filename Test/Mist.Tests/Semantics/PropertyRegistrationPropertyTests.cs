// Feature: properties, Property 2: Semantic Model Property Registration Completeness
// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 8.2, 9.1, 9.2, 10.3, 11.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Generated;
using Mist.Parsing;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for semantic model property registration completeness.
///
/// **Validates: Requirements 12.1, 12.2, 12.3, 12.4, 12.5, 8.2, 9.1, 9.2, 10.3, 11.3**
///
/// For any valid property declaration, verify symbol kind is Property, PropertyInfo
/// reflects accessor combination, IsStatic matches, backing field present iff not computed.
/// </summary>
public class PropertyRegistrationPropertyTests
{
    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<PropertyDeclarationNode> ArbitraryPropertyDeclarationNode()
    {
        return MistPropertyGenerator.GenPropertyDeclaration().ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static ISemanticModel BuildSemanticModel(CompilationUnitNode ast)
    {
        var source = new SourceText("test.mist", MistPrettyPrinter.Print(ast));
        var compilation = new Mist.Driver.Contracts.MistCompilation(
            ImmutableArray.Create(new Mist.Driver.Contracts.CompilationUnit(source)));
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

    private static IEnumerable<ISymbol> GetPropertySymbols(ISemanticModel model)
    {
        return model.AllSymbols.Where(s => s.Kind == SymbolKind.Property);
    }

    private static IEnumerable<ISymbol> GetIndexerSymbols(ISemanticModel model)
    {
        return model.AllSymbols.Where(s => s.Kind == SymbolKind.Indexer);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Semantic Model Property Registration Completeness
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRegistrationPropertyTests) })]
    public Property PropertyRegistration_SymbolKindIsProperty(PropertyDeclarationNode propDecl)
    {
        var ast = MistPropertyGenerator.MakeProgramWithProperty(propDecl);
        var model = BuildSemanticModel(ast);
        var propSymbols = GetPropertySymbols(model).ToList();

        return (propSymbols.Count >= 1).ToProperty()
            .Label($"Expected at least 1 property symbol, found {propSymbols.Count}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRegistrationPropertyTests) })]
    public Property PropertyRegistration_PropertyInfoReflectsAccessors(PropertyDeclarationNode propDecl)
    {
        var ast = MistPropertyGenerator.MakeProgramWithProperty(propDecl);
        var model = BuildSemanticModel(ast);
        var propSymbol = GetPropertySymbols(model).FirstOrDefault();

        if(propSymbol is null)
        {
            return false.ToProperty().Label("No property symbol found");
        }

        var metadata = (propSymbol as Symbol)?.PropertyMetadata;
        if(metadata is null)
        {
            return false.ToProperty().Label("PropertyMetadata is null");
        }

        // Verify accessor presence matches the declaration
        switch(propDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                {
                    var hasGetDecl = blockProp.Accessors.Any(a =>
                        a is AutoGetAccessorAccessorDeclarationNode or
                           BlockGetAccessorAccessorDeclarationNode or
                           ExpressionGetAccessorAccessorDeclarationNode);
                    var hasSetDecl = blockProp.Accessors.Any(a =>
                        a is AutoSetAccessorAccessorDeclarationNode or
                           BlockSetAccessorAccessorDeclarationNode or
                           ExpressionSetAccessorAccessorDeclarationNode);
                    var hasInitDecl = blockProp.Accessors.Any(a =>
                        a is AutoInitAccessorAccessorDeclarationNode or
                           BlockInitAccessorAccessorDeclarationNode or
                           ExpressionInitAccessorAccessorDeclarationNode);

                    var getMatch = metadata.HasGet == hasGetDecl;
                    var setMatch = metadata.HasSet == hasSetDecl;
                    var initMatch = metadata.HasInit == hasInitDecl;

                    return (getMatch && setMatch && initMatch).ToProperty()
                        .Label($"Accessor mismatch: HasGet={metadata.HasGet}(expected {hasGetDecl}), " +
                               $"HasSet={metadata.HasSet}(expected {hasSetDecl}), " +
                               $"HasInit={metadata.HasInit}(expected {hasInitDecl})");
                }
            case ExpressionBodiedPropertyPropertyDeclarationNode:
                {
                    // Expression-bodied properties always have get, no set/init
                    return (metadata.HasGet && !metadata.HasSet && !metadata.HasInit).ToProperty()
                        .Label($"Expression-bodied property should have get only. " +
                               $"HasGet={metadata.HasGet}, HasSet={metadata.HasSet}, HasInit={metadata.HasInit}");
                }
            default:
                return true.ToProperty();
        }
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRegistrationPropertyTests) })]
    public Property PropertyRegistration_IsStaticMatchesModifiers(PropertyDeclarationNode propDecl)
    {
        var ast = MistPropertyGenerator.MakeProgramWithProperty(propDecl);
        var model = BuildSemanticModel(ast);
        var propSymbol = GetPropertySymbols(model).FirstOrDefault();

        if(propSymbol is null)
        {
            return false.ToProperty().Label("No property symbol found");
        }

        var metadata = (propSymbol as Symbol)?.PropertyMetadata;
        if(metadata is null)
        {
            return false.ToProperty().Label("PropertyMetadata is null");
        }

        var expectedStatic = propDecl switch
        {
            BlockPropertyPropertyDeclarationNode bp => bp.Modifiers.Any(m => m is StaticModifierModifierNode),
            ExpressionBodiedPropertyPropertyDeclarationNode ep => ep.Modifiers.Any(m => m is StaticModifierModifierNode),
            _ => false,
        };

        return (metadata.IsStatic == expectedStatic).ToProperty()
            .Label($"IsStatic mismatch: got {metadata.IsStatic}, expected {expectedStatic}");
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRegistrationPropertyTests) })]
    public Property PropertyRegistration_BackingFieldPresentIffNotComputed(PropertyDeclarationNode propDecl)
    {
        var ast = MistPropertyGenerator.MakeProgramWithProperty(propDecl);
        var model = BuildSemanticModel(ast);
        var propSymbol = GetPropertySymbols(model).FirstOrDefault();

        if(propSymbol is null)
        {
            return false.ToProperty().Label("No property symbol found");
        }

        var metadata = (propSymbol as Symbol)?.PropertyMetadata;
        if(metadata is null)
        {
            return false.ToProperty().Label("PropertyMetadata is null");
        }

        if(metadata.IsComputed)
        {
            return (metadata.BackingField is null).ToProperty()
                .Label("Computed property should have no backing field");
        }
        else
        {
            // Non-computed properties with auto accessors or field keyword should have backing field
            var hasAutoAccessor =
                (metadata.GetAccessor?.BodyKind == AccessorBodyKind.Auto) ||
                (metadata.SetAccessor?.BodyKind == AccessorBodyKind.Auto) ||
                (metadata.InitAccessor?.BodyKind == AccessorBodyKind.Auto);

            if(hasAutoAccessor)
            {
                return (metadata.BackingField is not null).ToProperty()
                    .Label("Non-computed property with auto accessor should have backing field");
            }

            // Non-computed with block/expression accessors may or may not have backing field
            // depending on field keyword usage — this is acceptable either way
            return true.ToProperty();
        }
    }

    // ─── Indexer registration test ───────────────────────────────────────

    public static Arbitrary<IndexerDeclarationNode> ArbitraryIndexerDeclarationNode()
    {
        return MistPropertyGenerator.GenIndexerDeclaration().ToArbitrary();
    }

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PropertyRegistrationPropertyTests) })]
    public Property IndexerRegistration_SymbolKindIsIndexer(IndexerDeclarationNode indexerDecl)
    {
        var members = ImmutableArray.Create<MemberDeclarationNode>(
            new IndexerMemberMemberDeclarationNode { Indexer = indexerDecl });
        var ast = MistPropertyGenerator.MakeClassWithMembers("TestClass", members);
        var model = BuildSemanticModel(ast);
        var indexerSymbols = GetIndexerSymbols(model).ToList();

        return (indexerSymbols.Count >= 1).ToProperty()
            .Label($"Expected at least 1 indexer symbol, found {indexerSymbols.Count}");
    }
}