// Feature: void-unit-type, Property 6: Void/non-void equality type mismatch
// **Validates: Requirements 9.3**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests verifying that comparing a VoidType expression with a non-void
/// expression using == or != produces a diagnostic.
///
/// Property 6: For any non-void type T, comparing a VoidType expression with a T expression
/// using == or != SHALL produce a type incompatibility diagnostic (MIST_TC_0005).
/// </summary>
public class VoidEqualityMismatchPropertyTests
{
    private readonly SemanticModelBuilder builder = new();
    private readonly TypeCheckerAnalyzer analyzer = new();

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private static MistCompilation MakeCompilation(string source = "")
    {
        var sourceText = new SourceText("test.mist", source);
        return new MistCompilation(ImmutableArray.Create(new CompilationUnit(sourceText)));
    }

    private ISemanticModel BuildModel(Storm.Runtime.SyntaxNode ast)
    {
        var compilation = MakeCompilation();
        var diagnostics = new DiagnosticBag();
        return builder.Build(compilation, ImmutableArray.Create(ast), ambientTypeResolver: null, namespaceRoot: "TestProject", projectFolder: "/test", diagnostics);
    }

    private ImmutableArray<Diagnostic> RunTypeChecker(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        analyzer.Analyze(model, bag);
        return bag.ToImmutableArray();
    }

    // ─── Test data models ────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a non-void type with its corresponding literal expression.
    /// </summary>
    public enum NonVoidKind
    {
        IntegerType,
        BooleanType,
        StringType,
        FloatingType,
        CharacterType,
    }

    /// <summary>
    /// The equality operator used in the comparison.
    /// </summary>
    public enum EqualityOp
    {
        Equal,
        NotEqual,
    }

    /// <summary>
    /// Which side of the equality expression the Void.Void expression appears on.
    /// </summary>
    public enum VoidSide
    {
        Left,
        Right,
    }

    /// <summary>
    /// A scenario combining a non-void type, equality operator, and operand order.
    /// </summary>
    public record VoidEqualityScenario(
        NonVoidKind TypeKind,
        EqualityOp Operator,
        VoidSide Side)
    {
        public override string ToString()
        {
            return Side == VoidSide.Left
                ? $"Void.Void {(Operator == EqualityOp.Equal ? "==" : "!=")} {TypeKind}"
                : $"{TypeKind} {(Operator == EqualityOp.Equal ? "==" : "!=")} Void.Void";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    public static Gen<VoidEqualityScenario> GenScenario()
    {
        return from typeKind in Gen.Elements(
                   NonVoidKind.IntegerType,
                   NonVoidKind.BooleanType,
                   NonVoidKind.StringType,
                   NonVoidKind.FloatingType,
                   NonVoidKind.CharacterType)
               from op in Gen.Elements(
                   EqualityOp.Equal,
                   EqualityOp.NotEqual)
               from side in Gen.Elements(
                   VoidSide.Left,
                   VoidSide.Right)
               select new VoidEqualityScenario(typeKind, op, side);
    }

    public static Arbitrary<VoidEqualityScenario> ArbitraryScenario()
    {
        return GenScenario().ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    /// <summary>
    /// Creates the expression <c>Void.Void</c> as an AST node, returning the
    /// UnaryNode for use in equality expressions.
    /// </summary>
    private static UnaryNode MakeVoidDotVoidUnary()
    {
        var primary = new IdentifierExpressionPrimaryNode
        {
            Name = Tok("Void"),
        };
        var suffix = new MemberAccessSuffixPostfixSuffixNode
        {
            Name = Tok("Void"),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray.Create<PostfixSuffixNode>(suffix),
        };
        return new PostfixExpressionUnaryNode { Primary = postfix };
    }

    /// <summary>
    /// Creates a literal expression for the given non-void type kind,
    /// returning the UnaryNode for use in equality expressions.
    /// </summary>
    private static UnaryNode MakeLiteralUnary(NonVoidKind kind)
    {
        PrimaryNode primary = kind switch
        {
            NonVoidKind.IntegerType => new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("42") },
            NonVoidKind.BooleanType => new TrueLiteralExpressionPrimaryNode(),
            NonVoidKind.StringType => new StringLiteralExpressionPrimaryNode { StringLiteral = Tok("\"hello\"") },
            NonVoidKind.FloatingType => new FloatLiteralExpressionPrimaryNode { FloatLiteral = Tok("3.14") },
            NonVoidKind.CharacterType => new CharLiteralExpressionPrimaryNode { CharLiteral = Tok("'a'") },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var postfix = new PostfixNode
        {
            Target = primary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        return new PostfixExpressionUnaryNode { Primary = postfix };
    }

    /// <summary>
    /// Creates an equality expression with the given left and right unary nodes and operator.
    /// Returns a full ExpressionNode.
    /// </summary>
    private static ExpressionNode MakeEqualityExpression(UnaryNode left, UnaryNode right, string op)
    {
        var leftMult = new MultiplicativeNode { Left = left };
        var rightMult = new MultiplicativeNode { Left = right };
        var leftAdd = new AdditiveNode { Left = leftMult };
        var rightAdd = new AdditiveNode { Left = rightMult };
        var leftRel = new RelationalComparisonRelationalNode { Left = leftAdd };
        var rightRel = new RelationalComparisonRelationalNode { Left = rightAdd };
        var eq = new EqualityNode { Left = leftRel, Operator = Tok(op), Right = rightRel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        return new AssignmentFormExpressionNode { Assignment = assign };
    }

    /// <summary>
    /// Creates a standard Main method for the entry point.
    /// </summary>
    private static MethodDeclarationNode MakeMainMethod(ImmutableArray<StatementNode>? body = null)
    {
        return new MethodDeclarationNode
        {
            Name = Tok("Main"),
            ReturnType = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() },
            Parameters = null,
            Body = new MethodBlockBodyMethodBodyNode { Block = new BlockNode { Statements = body ?? ImmutableArray<StatementNode>.Empty } },
            Modifiers = ImmutableArray.Create<ModifierNode>(
                new PublicModifierModifierNode(),
                new StaticModifierModifierNode()),
        };
    }

    // ─── Scenario builders ───────────────────────────────────────────────

    /// <summary>
    /// Builds a program containing an equality comparison between Void.Void and a non-void literal.
    /// The comparison is placed as an expression statement inside Main.
    /// </summary>
    private static CompilationUnitNode BuildEqualityMismatchProgram(VoidEqualityScenario scenario)
    {
        var voidUnary = MakeVoidDotVoidUnary();
        var literalUnary = MakeLiteralUnary(scenario.TypeKind);
        var opStr = scenario.Operator == EqualityOp.Equal ? "==" : "!=";

        var (left, right) = scenario.Side == VoidSide.Left
            ? (voidUnary, literalUnary)
            : (literalUnary, voidUnary);

        var equalityExpr = MakeEqualityExpression(left, right, opStr);

        var exprStmt = new ExpressionStatementStatementNode
        {
            ExpressionStatement = new ExpressionStatementNode { Expression = equalityExpr },
        };

        var mainBody = ImmutableArray.Create<StatementNode>(exprStmt);

        var classDecl = new TypeBodyDeclarationNode
        {
            Keyword = new ClassKeywordTypeKeywordNode(),
            Name = Tok("Program"),
            Members = ImmutableArray.Create<MemberDeclarationNode>(
                new MethodMemberMemberDeclarationNode { Method = MakeMainMethod(mainBody) }),
        };

        return new CompilationUnitNode
        {
            Items = ImmutableArray.Create<TypeDeclarationNode>(
                new BodyTypeTypeDeclarationNode { TypeBody = classDecl }).AsTopLevelItems(),
        };
    }

    // ─── Property tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 6: For any non-void type T, comparing a VoidType expression with a T expression
    /// using == or != SHALL produce a type incompatibility diagnostic (MIST_TC_0005).
    ///
    /// **Validates: Requirements 9.3**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(VoidEqualityMismatchPropertyTests) }, MaxTest = 100)]
    public bool VoidNonVoidEquality_ProducesDiagnostic(VoidEqualityScenario scenario)
    {
        var ast = BuildEqualityMismatchProgram(scenario);

        var model = BuildModel(ast);
        var diagnostics = RunTypeChecker(model);

        var errors = diagnostics.Where(d =>
            d.Severity == DiagnosticSeverity.Error &&
            d.Id.Value == "MIST_TC_0005").ToList();

        return errors.Count > 0;
    }
}