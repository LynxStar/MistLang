// Feature: cast-operators, Property 2: Cast operator symbol registration
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**

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
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for cast operator symbol registration in the semantic model.
///
/// Property 2: For any set of cast operator declarations with varying cast kinds
/// (implicit/explicit) and target types, the semantic model builder SHALL create a
/// <see cref="SymbolKind.Method"/> symbol for each cast operator with a name matching
/// the pattern <c>operator_{kind}_{targetType}</c>, register it in the enclosing
/// type's member scope, and register a <see cref="SymbolKind.Parameter"/> symbol for
/// the single parameter within the cast operator's scope.
///
/// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
/// </summary>
public class CastOperatorRegistrationPropertyTests
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

    // ─── Scenario type ───────────────────────────────────────────────────

    /// <summary>
    /// Describes a single cast operator to register inside a type.
    /// </summary>
#pragma warning disable CA1034
    public record CastOperatorScenario(
        string CastKind,
        string TargetTypeName,
        string ParamTypeName,
        string ParamName,
        string EnclosingTypeName,
        bool IsStruct,
        bool IsIntrinsic)
    {
        public string ExpectedSymbolName =>
            $"operator_{CastKind}_{TargetTypeName}";

        public override string ToString() =>
            $"{(IsIntrinsic ? "intrinsic " : "")}public static {CastKind} operator " +
            $"{TargetTypeName}({ParamTypeName} {ParamName}) in {(IsStruct ? "struct" : "class")} {EnclosingTypeName}";
    }
#pragma warning restore CA1034

    // ─── FsCheck generators ──────────────────────────────────────────────

    private static readonly string[] TypeNames =
        ["Banana", "Weight", "Temperature", "Distance", "Velocity"];

    private static readonly string[] ParamNames =
        ["value", "source", "input", "item", "arg"];

    private static readonly string[] CastKinds = ["implicit", "explicit"];

    /// <summary>
    /// Generates a single cast operator scenario with random kind, types, and enclosing type.
    /// </summary>
    private static Gen<CastOperatorScenario> GenSingleScenario()
    {
        return from castKind in Gen.Elements(CastKinds)
               from targetType in Gen.Elements(TypeNames)
               from paramType in Gen.Elements(TypeNames)
               from paramName in Gen.Elements(ParamNames)
               from enclosingType in Gen.Elements(TypeNames)
               from isStruct in Gen.Elements(true, false)
               from isIntrinsic in Gen.Elements(true, false)
               select new CastOperatorScenario(
                   castKind, targetType, paramType, paramName,
                   enclosingType, isStruct, isIntrinsic);
    }

    /// <summary>
    /// Generates a list of 1–4 cast operator scenarios for a single enclosing type.
    /// Ensures unique (kind, targetType) pairs to avoid duplicate registration conflicts.
    /// </summary>
    private static Gen<CastOperatorScenario[]> GenScenarioSet()
    {
        return from enclosingType in Gen.Elements(TypeNames)
               from isStruct in Gen.Elements(true, false)
               from count in Gen.Choose(1, 4)
               from scenarios in Gen.ArrayOf(GenSingleScenario(), count)
               let normalized = scenarios
                   .Select(s => s with { EnclosingTypeName = enclosingType, IsStruct = isStruct })
                   .DistinctBy(s => (s.CastKind, s.TargetTypeName))
                   .ToArray()
               where normalized.Length >= 1
               select normalized;
    }

    public static Arbitrary<CastOperatorScenario[]> ArbitraryScenarioSets() =>
        GenScenarioSet().ToArbitrary();

    public static Arbitrary<CastOperatorScenario> ArbitrarySingleScenario() =>
        GenSingleScenario().ToArbitrary();

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates a minimal identifier expression AST node suitable for use as a return value.
    /// </summary>
    private static ExpressionNode MakeIdentifierExpression(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    private static CastOperatorKindNode MakeCastKindNode(string kind) =>
        kind == "implicit"
            ? new ImplicitCastCastOperatorKindNode()
            : (CastOperatorKindNode)new ExplicitCastCastOperatorKindNode();

    private static TypeReferenceNode MakeNamedTypeRef(string name) =>
        new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode
        {
            Name = new QualifiedIdentifierNode { IDENTIFIER = Tok(name) },
        } };

    private static CastOperatorDeclarationNode MakeCastOperatorDecl(CastOperatorScenario scenario)
    {
        var modifiers = scenario.IsIntrinsic
            ? ImmutableArray.Create<ModifierNode>(
                new IntrinsicModifierModifierNode(),
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode())
            : ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode());

        var body = scenario.IsIntrinsic
            ? (OperatorBodyNode)new OperatorNoBodyOperatorBodyNode()
            : new OperatorBlockBodyOperatorBodyNode
            {
                Block = new BlockNode
                {
                    Statements = ImmutableArray.Create<StatementNode>(
                        new ReturnStatementStatementNode
                        {
                            ReturnStatement = new ReturnStatementNode
                            {
                                Value = MakeIdentifierExpression(scenario.ParamName),
                            },
                        }),
                },
            };

        return new CastOperatorDeclarationNode
        {
            Modifiers = modifiers,
            CastKind = MakeCastKindNode(scenario.CastKind),
            TargetType = MakeNamedTypeRef(scenario.TargetTypeName),
            Parameters = new ParameterListNode
            {
                Parameter = new ParameterNode
                {
                    Type = MakeNamedTypeRef(scenario.ParamTypeName),
                    Name = Tok(scenario.ParamName),
                },
            },
            Body = body,
        };
    }

    private static CompilationUnitNode BuildAst(CastOperatorScenario[] scenarios)
    {
        var enclosingName = scenarios[0].EnclosingTypeName;
        var isStruct = scenarios[0].IsStruct;

        var members = scenarios
            .Select(s => (MemberDeclarationNode)new CastOperatorMemberMemberDeclarationNode
            {
                CastOperator = MakeCastOperatorDecl(s),
            })
            .ToImmutableArray();

        TypeDeclarationNode typeDecl;
        if (isStruct)
        {
            typeDecl = new StructTypeTypeDeclarationNode
            {
                StructDeclaration = new StructDeclarationNode
                {
                    Name = Tok(enclosingName),
                    Members = members,
                },
            };
        }
        else
        {
            typeDecl = new ClassTypeTypeDeclarationNode
            {
                ClassDeclaration = new ClassDeclarationNode
                {
                    Name = Tok(enclosingName),
                    Members = members,
                },
            };
        }

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create(typeDecl).AsTopLevelItems(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Cast operator symbol registration
    //
    // **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For any set of cast operator declarations, the semantic model builder creates
    /// a <see cref="SymbolKind.Method"/> symbol for each, with the correct encoded name.
    ///
    /// **Validates: Requirements 5.1, 5.2**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_RegistersMethodSymbolWithEncodedName(
        CastOperatorScenario[] scenarios)
    {
        var ast = BuildAst(scenarios);
        var model = BuildModel(ast);

        foreach (var scenario in scenarios)
        {
            var expectedName = scenario.ExpectedSymbolName;
            var symbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == expectedName && s.Kind == SymbolKind.Method);

            if (symbol is null)
                return false.ToProperty()
                    .Label($"No SymbolKind.Method symbol named '{expectedName}' found. " +
                           $"All symbols: [{string.Join(", ", model.AllSymbols.Select(s => $"{s.Name}:{s.Kind}"))}]");
        }

        return true.ToProperty()
            .Label("All cast operators registered as Method symbols with correct encoded names");
    }

    /// <summary>
    /// For any cast operator declaration, the symbol is registered in the enclosing
    /// type's member scope (not at global scope).
    ///
    /// **Validates: Requirement 5.2**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_RegisteredInEnclosingTypeMemberScope(
        CastOperatorScenario[] scenarios)
    {
        var ast = BuildAst(scenarios);
        var model = BuildModel(ast);

        var enclosingName = scenarios[0].EnclosingTypeName;
        var isStruct = scenarios[0].IsStruct;

        // Find the enclosing type symbol
        var typeSymbol = model.AllSymbols
            .FirstOrDefault(s => s.Name == enclosingName &&
                                 s.Kind == (isStruct ? SymbolKind.Struct : SymbolKind.Class));

        if (typeSymbol is null)
            return false.ToProperty()
                .Label($"Enclosing type '{enclosingName}' not found in model");

        // Get the member scope from the type
        IScope? memberScope = typeSymbol.DeclaredType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return false.ToProperty()
                .Label($"Could not get MemberScope from type '{enclosingName}'");

        foreach (var scenario in scenarios)
        {
            var expectedName = scenario.ExpectedSymbolName;
            var member = memberScope.Members
                .FirstOrDefault(m => m.Name == expectedName && m.Kind == SymbolKind.Method);

            if (member is null)
                return false.ToProperty()
                    .Label($"Symbol '{expectedName}' not found in {enclosingName}'s member scope. " +
                           $"Members: [{string.Join(", ", memberScope.Members.Select(m => $"{m.Name}:{m.Kind}"))}]");
        }

        return true.ToProperty()
            .Label("All cast operators registered in enclosing type's member scope");
    }

    /// <summary>
    /// For any cast operator declaration, the symbol correctly distinguishes between
    /// implicit and explicit kinds via the encoded name prefix.
    ///
    /// **Validates: Requirement 5.3**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_NameEncodesKindAndTargetType(
        CastOperatorScenario[] scenarios)
    {
        var ast = BuildAst(scenarios);
        var model = BuildModel(ast);

        foreach (var scenario in scenarios)
        {
            var expectedName = scenario.ExpectedSymbolName;
            var symbol = model.AllSymbols
                .FirstOrDefault(s => s.Name == expectedName && s.Kind == SymbolKind.Method);

            if (symbol is null)
                return false.ToProperty()
                    .Label($"Symbol '{expectedName}' not found");

            // Verify the name encodes the cast kind
            var expectedPrefix = $"operator_{scenario.CastKind}_";
            if (!symbol.Name.StartsWith(expectedPrefix))
                return false.ToProperty()
                    .Label($"Symbol name '{symbol.Name}' does not start with '{expectedPrefix}'");

            // Verify the name encodes the target type
            var encodedTarget = symbol.Name.Substring(expectedPrefix.Length);
            if (encodedTarget != scenario.TargetTypeName)
                return false.ToProperty()
                    .Label($"Encoded target type '{encodedTarget}' does not match expected '{scenario.TargetTypeName}'");
        }

        return true.ToProperty()
            .Label("All cast operator names correctly encode kind and target type");
    }

    /// <summary>
    /// For any cast operator declaration, the intrinsic flag is correctly propagated
    /// to the registered symbol.
    ///
    /// **Validates: Requirement 5.1**
    /// </summary>
    [Property(
        Arbitrary = new[] { typeof(CastOperatorRegistrationPropertyTests) },
        MaxTest = 100)]
    public Property CastOperator_IntrinsicFlagMatchesModifier(
        CastOperatorScenario[] scenarios)
    {
        var ast = BuildAst(scenarios);
        var model = BuildModel(ast);

        foreach (var scenario in scenarios)
        {
            var expectedName = scenario.ExpectedSymbolName;
            var symbol = model.AllSymbols
                .OfType<Symbol>()
                .FirstOrDefault(s => s.Name == expectedName && s.Kind == SymbolKind.Method);

            if (symbol is null)
                return false.ToProperty()
                    .Label($"Symbol '{expectedName}' not found");

            if (symbol.IsIntrinsic != scenario.IsIntrinsic)
                return false.ToProperty()
                    .Label($"Symbol '{expectedName}' has IsIntrinsic={symbol.IsIntrinsic}, " +
                           $"expected {scenario.IsIntrinsic}");

            // HasBody should be the inverse of IsIntrinsic
            if (symbol.HasBody == scenario.IsIntrinsic)
                return false.ToProperty()
                    .Label($"Symbol '{expectedName}' has HasBody={symbol.HasBody}, " +
                           $"expected {!scenario.IsIntrinsic}");
        }

        return true.ToProperty()
            .Label("All cast operator intrinsic flags match their modifiers");
    }
}
