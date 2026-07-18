// Feature: pattern-matching, Property 4: Is-Expression Evaluates to Bool
// **Validates: Requirements 6.1**

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
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for is-expression bool type.
///
/// **Validates: Requirements 6.1**
///
/// Property 4: For any valid <c>is</c> expression (with any pattern kind), the
/// semantic model SHALL record the expression type as <c>bool</c>.
/// </summary>
public class IsExpressionBoolTypePropertyTests
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
    /// Represents a pattern kind for testing is-expression bool type.
    /// </summary>
    public enum PatternKind
    {
        TypePattern,
        VarPattern,
        DiscardPattern,
        ConstantIntPattern,
        ConstantBoolPattern,
        RelationalPattern,
    }

    public record IsExprBoolScenario(PatternKind Kind, string ScrutineeName, string ScrutineeType)
    {
        public override string ToString()
        {
            return $"{ScrutineeName} is {Kind} (scrutinee: {ScrutineeType})";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] ScrutineeNames = ["x", "y", "z", "val", "item"];

    public static Arbitrary<IsExprBoolScenario> ArbitraryScenario()
    {
        var gen = from kind in Gen.Elements(
                      PatternKind.TypePattern,
                      PatternKind.VarPattern,
                      PatternKind.DiscardPattern,
                      PatternKind.ConstantIntPattern,
                      PatternKind.ConstantBoolPattern,
                      PatternKind.RelationalPattern)
                  from name in Gen.Elements(ScrutineeNames)
                  let scrutType = kind switch
                  {
                      PatternKind.ConstantBoolPattern => "bool",
                      PatternKind.RelationalPattern => "int",
                      PatternKind.ConstantIntPattern => "int",
                      _ => "int",
                  }
                  select new IsExprBoolScenario(kind, name, scrutType);
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
        var initializer = typeName switch
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
        var mult = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } };
        return new AdditiveNode { Left = mult };
    }

    private static PatternNode MakePattern(PatternKind kind)
    {
        PrimaryPatternNode primary = kind switch
        {
            PatternKind.TypePattern => new TypePatternPrimaryPatternNode
            {
                Type = new TypeReferenceNode
                {
                    First = new PrimitiveSingleTypeRefSingleTypeRefNode
                    {
                        PrimitiveType = new IntTypePrimitiveTypeNode(),
                    }
                },
                Name = Tok("matched"),
            },
            PatternKind.VarPattern => new VarPatternPrimaryPatternNode
            {
                Name = Tok("matched"),
            },
            PatternKind.DiscardPattern => new VarPatternPrimaryPatternNode
            {
                Name = Tok("_"),
            },
            PatternKind.ConstantIntPattern => new ConstantPatternPrimaryPatternNode
            {
                Literal = new IntegerConstantConstantLiteralNode
                {
                    Value = Tok("42"),
                },
            },
            PatternKind.ConstantBoolPattern => new ConstantPatternPrimaryPatternNode
            {
                Literal = new TrueConstantConstantLiteralNode(),
            },
            PatternKind.RelationalPattern => new GreaterThanRelationalPatternPrimaryPatternNode
            {
                Constant = MistAstGenerator.WrapPrimary(
                    new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") }).AsAssignment().Target,
            },
            _ => new VarPatternPrimaryPatternNode { Name = Tok("_") },
        };

        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static (CompilationUnitNode Ast, IsExpressionRelationalNode IsNode) BuildProgram(
        IsExprBoolScenario scenario)
    {
        var scrutineeDecl = MakeScrutineeDecl(scenario.ScrutineeType, scenario.ScrutineeName);
        var pattern = MakePattern(scenario.Kind);

        var scrutinee = MakeIdentifierAdditive(scenario.ScrutineeName);
        var isNode = new IsExpressionRelationalNode { Left = scrutinee, Pattern = pattern };
        var eq = new EqualityNode { Left = isNode };
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
            ifStatement);

        var ast = MistProgramGenerator.MakeProgram(statements);
        return (ast, isNode);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Is-Expression Evaluates to Bool
    //
    // **Validates: Requirements 6.1**
    //
    // For any valid is expression (with any pattern kind), the semantic
    // model SHALL record the expression type as bool.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(
        MaxTest = 100,
        Arbitrary = new[] { typeof(IsExpressionBoolTypePropertyTests) })]
    public Property IsExpression_HasBoolType(IsExprBoolScenario scenario)
    {
        var (ast, isNode) = BuildProgram(scenario);
        var model = BuildModel(ast);

        // Query the semantic model for the type of the is-expression node
        var exprType = model.GetTypeOf(isNode);

        if(exprType is null)
        {
            return false.ToProperty()
                .Label($"Is-expression has null type. Scenario: {scenario}");
        }

        var isBool = exprType.DisplayName == "Boolean"
                     || exprType.DisplayName.Equals("bool", StringComparison.OrdinalIgnoreCase);

        return isBool.ToProperty()
            .Label($"Expected bool type but got '{exprType.DisplayName}'. Scenario: {scenario}");
    }
}