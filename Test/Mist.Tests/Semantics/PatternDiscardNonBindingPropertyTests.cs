// Feature: pattern-matching, Property 3: Discard Pattern Non-Binding
// **Validates: Requirements 5.4**

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
/// Property-based tests for discard pattern non-binding.
///
/// **Validates: Requirements 5.4**
///
/// Property 3: For any <c>if</c> statement whose condition contains an <c>is</c>
/// expression with a discard pattern <c>_</c>, the then-branch scope SHALL NOT
/// contain any new variable introduced by the discard pattern.
/// </summary>
public class PatternDiscardNonBindingPropertyTests
{
    private readonly SemanticModelBuilder builder = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private ISemanticModel BuildModel(CompilationUnitNode ast, string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        var compilation = new MistCompilation(
            ImmutableArray.Create(new CompilationUnit(sourceText)));
        var diagnostics = new DiagnosticBag();
        return builder.Build(
            compilation,
            ImmutableArray.Create<Storm.Runtime.SyntaxNode>(ast),
            ambientTypeResolver: null,
            namespaceRoot: "TestProject",
            projectFolder: "/test",
            diagnostics);
    }

    // ─── Scenario records ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a discard pattern scenario: a scrutinee type and name.
    /// The pattern is always <c>var _</c> (discard).
    /// </summary>
    public record DiscardScenario(string ScrutineeType, string ScrutineeName)
    {
        public override string ToString()
        {
            return $"if ({ScrutineeName} is var _) — scrutinee type: {ScrutineeType}";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];
    private static readonly string[] PrimitiveTypes = ["int", "string", "bool"];

    public static Arbitrary<DiscardScenario> ArbitraryScenario()
    {
        var gen = from scrutType in Gen.Elements(PrimitiveTypes)
                  from scrutName in Gen.Elements(ScrutineeNames)
                  select new DiscardScenario(scrutType, scrutName);
        return gen.ToArbitrary();
    }

    // ─── AST construction ────────────────────────────────────────────────

    private static PrimitiveTypeNode MakePrimitiveType(string typeName)
    {
        return typeName switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
    }

    private static StatementNode MakeScrutineeDecl(string typeName, string name)
    {
        ExpressionNode initializer = typeName switch
        {
            "int" => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("42") }),
            "string" => MistAstGenerator.WrapPrimary(
                new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"hello\"") }),
            "bool" => MistAstGenerator.WrapPrimary(
                new TrueLiteralExpressionPrimaryNode()),
            _ => MistAstGenerator.WrapPrimary(
                new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") }),
        };

        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = MakePrimitiveType(typeName),
                    }
                },
                Name = Tok(name),
                Initializer = initializer,
            },
        };
    }

    private static AdditiveNode MakeIdentifierAdditive(string name)
    {
        var primary = new IdentifierExpressionPrimaryNode { Name = Tok(name) };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unary = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unary };
        return new AdditiveNode { Left = mult };
    }

    private static CompilationUnitNode BuildProgram(DiscardScenario scenario)
    {
        var scrutineeDecl = MakeScrutineeDecl(scenario.ScrutineeType, scenario.ScrutineeName);

        // Build discard pattern: var _
        var discardPrimary = new VarPatternPrimaryPatternNode
        {
            Name = Tok("_"),
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = discardPrimary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        var pattern = new PatternNode { OrPattern = or };

        // Build is expression
        var scrutinee = MakeIdentifierAdditive(scenario.ScrutineeName);
        var rel = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var isExpr = new AssignmentFormExpressionNode { Assignment = assign };

        var ifStatement = new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = isExpr,
                ThenBranch = new BlockNode
                {
                    Statements = ImmutableArray<StatementNode>.Empty,
                },
            },
        };

        var statements = ImmutableArray.Create(
            scrutineeDecl,
            (StatementNode)ifStatement);

        return MistProgramGenerator.MakeProgram(statements);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Discard Pattern Non-Binding
    //
    // **Validates: Requirements 5.4**
    //
    // For any if statement whose condition contains an is expression with
    // a discard pattern _, the then-branch scope SHALL NOT contain any
    // new variable introduced by the discard pattern.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(PatternDiscardNonBindingPropertyTests) })]
    public Property DiscardPattern_DoesNotIntroduceVariable(DiscardScenario scenario)
    {
        var ast = BuildProgram(scenario);
        var model = BuildModel(ast);

        // The discard pattern should NOT introduce a variable named "_"
        // as a LocalVariable in the model.
        var discardVar = model.AllSymbols
            .FirstOrDefault(s => s.Name == "_" && s.Kind == SymbolKind.LocalVariable);

        var noDiscardVariable = discardVar is null;

        return noDiscardVariable.ToProperty()
            .Label($"Discard pattern introduced a variable named '_' as LocalVariable. " +
                   $"Scenario: {scenario}");
    }
}