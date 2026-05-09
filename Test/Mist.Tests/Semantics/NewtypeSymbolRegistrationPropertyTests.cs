// Feature: newtypes, Property 6: Symbol registration with correct kind
//
// **Validates: Requirements 3.1**
//
// For any valid NewtypeDeclaration, after semantic model building, the symbol
// table SHALL contain a symbol with SymbolKind.Newtype for the declared name
// in the enclosing scope.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Text;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for newtype symbol registration in the semantic model.
///
/// **Validates: Requirements 3.1**
///
/// For any valid NewtypeDeclaration, after semantic model building, the symbol
/// table SHALL contain a symbol with <see cref="SymbolKind.Newtype"/> for the
/// declared name in the enclosing scope.
/// </summary>
public class NewtypeSymbolRegistrationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme) =>
        new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);

    private static ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var sourceText = new SourceText("test.mist", "");
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return Builder.Build(
            compilation,
            ImmutableArray.Create(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── Type reference helpers ─────────────────────────────────────────

    private static TypeReferenceNode MakePrimitiveTypeRef(string typeName)
    {
        PrimitiveTypeNode primitiveType = typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            "long" => new LongTypePrimitiveTypeNode(),
            "char" => new CharTypePrimitiveTypeNode(),
            "byte" => new ByteTypePrimitiveTypeNode(),
            "decimal" => new DecimalTypePrimitiveTypeNode(),
            "short" => new ShortTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };

        return new PrimitiveTypeRefTypeReferenceNode { PrimitiveType = primitiveType };
    }

    private static TypeReferenceNode MakeNamedTypeRef(string name) =>
        new NamedTypeRefTypeReferenceNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
        };

    private static TypeReferenceNode MakeTypeRef(string typeName)
    {
        return typeName switch
        {
            "int" or "string" or "bool" or "float" or "double"
                or "long" or "char" or "byte" or "decimal" or "short"
                => MakePrimitiveTypeRef(typeName),
            _ => MakeNamedTypeRef(typeName),
        };
    }

    // ─── AST construction ───────────────────────────────────────────────

    private static CompilationUnitNode BuildAst(NewtypeSourceScenario scenario)
    {
        var newtypeDecl = new NewtypeDeclarationNode
        {
            Name = Tok(scenario.NewtypeName),
            SourceType = MakeTypeRef(scenario.SourceTypeText),
        };

        var newtypeItem = new NewtypeItemTopLevelItemNode
        {
            NewtypeDeclaration = newtypeDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(newtypeItem),
        };
    }

    // ─── FsCheck Arbitrary registration ─────────────────────────────────

    public static Arbitrary<NewtypeSourceScenario> ArbitraryNewtypeSourceScenario() =>
        MistNewtypeGenerator.GenNewtypeSource().ToArbitrary();

    // ═══════════════════════════════════════════════════════════════════════
    // Property 6: Symbol registration with correct kind
    //
    // **Validates: Requirements 3.1**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any valid NewtypeDeclaration, after semantic model building, the
    /// symbol table SHALL contain a symbol with SymbolKind.Newtype for the
    /// declared name in the enclosing scope.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(NewtypeSymbolRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property Newtype_IsRegisteredWithCorrectSymbolKind(
        NewtypeSourceScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Look for the newtype symbol in AllSymbols
        var newtypeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.NewtypeName && s.Kind == SymbolKind.Newtype);

        if (newtypeSymbol is null)
        {
            return false.ToProperty()
                .Label($"Newtype '{scenario.NewtypeName}' not found with SymbolKind.Newtype. " +
                       $"All user symbols: [{string.Join(", ", model.AllSymbols.Where(s => s.Origin == SymbolOrigin.UserCode).Select(s => $"{s.Name}:{s.Kind}"))}]")
                .Label($"Scenario: {scenario}");
        }

        // Verify the symbol's origin is UserCode
        var originCorrect = newtypeSymbol.Origin == SymbolOrigin.UserCode;

        return originCorrect.ToProperty()
            .Label($"Newtype '{scenario.NewtypeName}' registered with SymbolKind.Newtype, Origin={newtypeSymbol.Origin}")
            .Label($"Scenario: {scenario}");
    }
}
