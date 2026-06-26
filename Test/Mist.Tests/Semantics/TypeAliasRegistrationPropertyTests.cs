// Feature: type-aliases, Property 3: Semantic model registration
//
// **Validates: Requirements 4.1, 4.2**
//
// For any valid type alias declaration (with or without type parameters),
// building the semantic model SHALL register a symbol with
// SymbolKind.TypeAlias in the enclosing scope, and the symbol's type
// parameters SHALL match the declaration's type parameter list.
//
// All work runs in-process — no external process is spawned per iteration
// (in keeping with the workspace test-process-spawning policy).

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Describes a generated type alias declaration for property testing.
/// </summary>
public sealed class TypeAliasRegistrationScenario
{
    public string AliasName { get; }
    public IReadOnlyList<string> TypeParameterNames { get; }
    public string TargetTypeName { get; }

    public TypeAliasRegistrationScenario(
        string aliasName,
        IReadOnlyList<string> typeParameterNames,
        string targetTypeName)
    {
        AliasName = aliasName;
        TypeParameterNames = typeParameterNames;
        TargetTypeName = targetTypeName;
    }

    public bool IsGeneric => TypeParameterNames.Count > 0;

    public override string ToString()
    {
        return IsGeneric
            ? $"type {AliasName}<{string.Join(", ", TypeParameterNames)}> = {TargetTypeName};"
            : $"type {AliasName} = {TargetTypeName};";
    }
}

/// <summary>
/// Property-based tests for type alias semantic model registration.
///
/// **Validates: Requirements 4.1, 4.2**
///
/// For any valid type alias declaration (with or without type parameters),
/// building the semantic model SHALL register a symbol with
/// SymbolKind.TypeAlias in the enclosing scope, and the symbol's type
/// parameters SHALL match the declaration's type parameter list.
/// </summary>
public class TypeAliasRegistrationPropertyTests
{
    // ─── Shared infrastructure ───────────────────────────────────────────

    private static readonly SemanticModelBuilder Builder = new();

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return new(kind: 0,
            span: new Storm.Runtime.SourceSpan(0, lexeme.Length),
            start: new Storm.Runtime.LinePosition(1, 1),
            end: new Storm.Runtime.LinePosition(1, 1 + lexeme.Length),
            channel: Storm.Runtime.TokenChannel.Default,
            lexeme: lexeme);
    }

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

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] AliasNames =
        ["Foo", "Bar", "Baz", "Qux", "MyAlias", "Result", "Value", "Item"];

    private static readonly string[] TargetTypes =
        ["int", "string", "bool", "float", "double", "long", "char", "byte"];

    private static readonly string[] TypeParamNames = ["T", "U", "V", "W", "K"];

    // ─── FsCheck generators ─────────────────────────────────────────────

    /// <summary>
    /// Generates a simple (non-generic) type alias scenario.
    /// </summary>
    private static Gen<TypeAliasRegistrationScenario> GenSimpleAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from target in Gen.Elements(TargetTypes)
               select new TypeAliasRegistrationScenario(name, Array.Empty<string>(), target);
    }

    /// <summary>
    /// Generates a generic type alias scenario with 1–5 type parameters.
    /// </summary>
    private static Gen<TypeAliasRegistrationScenario> GenGenericAlias()
    {
        return from name in Gen.Elements(AliasNames)
               from paramCount in Gen.Choose(1, 5)
               from target in Gen.Elements(TargetTypes)
               let typeParams = TypeParamNames.Take(paramCount).ToArray()
               select new TypeAliasRegistrationScenario(name, typeParams, target);
    }

    private static Gen<TypeAliasRegistrationScenario> GenScenario()
    {
        return Gen.OneOf(GenSimpleAlias(), GenGenericAlias());
    }

    public static Arbitrary<TypeAliasRegistrationScenario> ArbitraryTypeAliasRegistrationScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static TypeParameterListNode MakeTypeParamList(IReadOnlyList<string> names)
    {
        var first = new RegularTypeParameterTypeParameterNode { Name = Tok(names[0]) };
        var rest = names.Skip(1)
            .Select(n => (TypeParameterNode)new RegularTypeParameterTypeParameterNode { Name = Tok(n) })
            .ToImmutableArray();
        return new TypeParameterListNode
        {
            TypeParameter = first,
            TypeParameters = rest,
        };
    }

    private static RefinableTypeExpressionNode MakeRefinableTypeExpr(string name)
    {
        return new RefinableTypeExpressionNode
        {
            First = new RefinableSingleTypeExprNode
            {
                Type = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
                },
            },
        };
    }

    private static CompilationUnitNode BuildAst(TypeAliasRegistrationScenario scenario)
    {
        var aliasDecl = new TypeAliasDeclarationNode
        {
            Name = Tok(scenario.AliasName),
            TypeParams = scenario.IsGeneric
                ? MakeTypeParamList(scenario.TypeParameterNames)
                : null,
            Type = MakeRefinableTypeExpr(scenario.TargetTypeName),
        };

        var aliasItem = new TypeAliasItemTopLevelItemNode
        {
            TypeAliasDeclaration = aliasDecl,
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TopLevelItemNode>(aliasItem),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Semantic model registration
    //
    // **Validates: Requirements 4.1, 4.2**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any valid type alias declaration, the semantic model SHALL contain
    /// a symbol with SymbolKind.TypeAlias and the correct name in the
    /// enclosing scope.
    ///
    /// **Validates: Requirement 4.1**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(TypeAliasRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property TypeAlias_IsRegisteredWithCorrectSymbolKind(
        TypeAliasRegistrationScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Look for the alias symbol in AllSymbols
        var aliasSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == scenario.AliasName && s.Kind == SymbolKind.TypeAlias);

        if(aliasSymbol is null)
        {
            return false.ToProperty()
                .Label($"Type alias '{scenario.AliasName}' not found with SymbolKind.TypeAlias. " +
                       $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        return true.ToProperty()
            .Label($"Type alias '{scenario.AliasName}' registered with SymbolKind.TypeAlias");
    }

    /// <summary>
    /// For any valid generic type alias declaration, the registered symbol's
    /// type parameters SHALL match the declaration's type parameter list in
    /// count and names.
    ///
    /// **Validates: Requirement 4.2**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(TypeAliasRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property TypeAlias_TypeParametersMatchDeclaration(
        TypeAliasRegistrationScenario scenario)
    {
        var ast = BuildAst(scenario);
        var model = BuildModel(ast);

        // Find the alias symbol
        var aliasSymbol = model.AllSymbols
            .OfType<Symbol>()
            .FirstOrDefault(s => s.Name == scenario.AliasName && s.Kind == SymbolKind.TypeAlias);

        if(aliasSymbol is null)
        {
            return false.ToProperty()
                .Label($"Type alias '{scenario.AliasName}' not found in model");
        }

        var registeredParams = aliasSymbol.TypeParameters;
        var expectedCount = scenario.TypeParameterNames.Count;

        if(registeredParams.Count != expectedCount)
        {
            return false.ToProperty()
                .Label($"Expected {expectedCount} type parameters, got {registeredParams.Count}. " +
                       $"Scenario: {scenario}");
        }

        // Verify each type parameter name matches
        for(var i = 0; i < expectedCount; i++)
        {
            if(registeredParams[i].DisplayName != scenario.TypeParameterNames[i])
            {
                return false.ToProperty()
                    .Label($"Type parameter at index {i}: expected '{scenario.TypeParameterNames[i]}', " +
                           $"got '{registeredParams[i].DisplayName}'. Scenario: {scenario}");
            }
        }

        return true.ToProperty()
            .Label($"Type parameters match for '{scenario}'");
    }
}