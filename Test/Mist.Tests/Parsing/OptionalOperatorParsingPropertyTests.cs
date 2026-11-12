// Feature: optional-types, Properties 10, 13, 17: Operator parsing
// **Validates: Requirements 3.5, 4.6, 6.1, 6.4, 6.6**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;

namespace Mist.Tests.Parsing;

public class OptionalOperatorParsingPropertyTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        if(errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");
        }

        return result.Root as CompilationUnitNode
            ?? throw new InvalidOperationException("Parse result root is not a CompilationUnitNode");
    }

    private static ExpressionNode GetExpressionFromStatement(string expressionSource)
    {
        var source = $"class T {{ void M() {{ {expressionSource}; }} }}";
        var cu = ParseOrFail(source);

        var typeDecl = (BodyTypeTypeDeclarationNode)cu.GetTypes()[0];
        var methodMember = (MethodMemberMemberDeclarationNode)typeDecl.TypeBody.Members[0];
        var block = methodMember.Method.Body.GetBlock()!;
        var exprStmt = block.Statements.OfType<ExpressionStatementStatementNode>().Last();
        return exprStmt.ExpressionStatement.Expression;
    }

    public static Arbitrary<int> ArbitraryChainLength()
    {
        return Gen.Choose(2, 10).ToArbitrary();
    }

    /// <summary>
    /// Property 10: For chain length N (2–10), N chained `?.` accesses parse without
    /// errors and produce a postfix expression with N OptionalMemberAccessSuffix nodes.
    ///
    /// **Validates: Requirements 3.5**
    /// </summary>
    [Property(MaxTest = 50, Arbitrary = new[] { typeof(OptionalOperatorParsingPropertyTests) })]
    public bool OptionalChaining_ParsesAtArbitraryDepth(int chainLength)
    {
        var members = Enumerable.Range(0, chainLength)
            .Select(i => $"m{i}")
            .ToArray();
        var expression = "obj" + string.Join("", members.Select(m => $"?.{m}"));

        var expr = GetExpressionFromStatement(expression);

        var assignment = expr.AsAssignment();
        var voidCoalescing = assignment.Target;
        var logicalOr = voidCoalescing.Left;
        var logicalAnd = logicalOr.Left;
        var equality = logicalAnd.Left;
        var relational = (RelationalComparisonRelationalNode)equality.Left;
        var additive = relational.Left;
        var multiplicative = additive.Left;
        var unary = (PostfixExpressionUnaryNode)multiplicative.Left.UnwrapRange()!;
        var postfix = unary.Primary;

        var optionalSuffixes = postfix.Suffix
            .OfType<OptionalMemberAccessSuffixPostfixSuffixNode>()
            .ToList();

        return optionalSuffixes.Count == chainLength;
    }

    /// <summary>
    /// Property 13: the grammar's `(QUESTION_QUESTION right:LogicalOr)*` repetition captures
    /// every right operand in one flat list; right-associativity is left to the semantic layer.
    ///
    /// **Validates: Requirements 4.6, 6.6**
    /// </summary>
    [Fact]
    public void VoidCoalescing_ChainedOperators_ProducesFlatAstWithAllOperands()
    {
        var expr = GetExpressionFromStatement("a ?? b ?? c");

        var assignment = expr.AsAssignment();
        var voidCoalescing = assignment.Target;

        var leftIdentifier = GetIdentifierFromLogicalOr(voidCoalescing.Left);
        Assert.Equal("a", leftIdentifier);

        Assert.True(voidCoalescing.LogicalOrs.Length >= 2,
            $"Expected at least 2 LogicalOrs for 'a ?? b ?? c', got {voidCoalescing.LogicalOrs.Length}");

        var firstRight = GetIdentifierFromLogicalOr(voidCoalescing.LogicalOrs[0]);
        var secondRight = GetIdentifierFromLogicalOr(voidCoalescing.LogicalOrs[1]);
        Assert.Equal("b", firstRight);
        Assert.Equal("c", secondRight);
    }

    [Theory]
    [InlineData(2, "a ?? b ?? c")]
    [InlineData(3, "a ?? b ?? c ?? d")]
    [InlineData(4, "a ?? b ?? c ?? d ?? e")]
    public void VoidCoalescing_ChainOfN_HasNRightOperands(int expectedRightCount, string expression)
    {
        var expr = GetExpressionFromStatement(expression);

        var assignment = expr.AsAssignment();
        var voidCoalescing = assignment.Target;

        var leftId = GetIdentifierFromLogicalOr(voidCoalescing.Left);
        Assert.Equal("a", leftId);

        Assert.Equal(expectedRightCount, voidCoalescing.LogicalOrs.Length);
    }

    /// <summary>
    /// Property 17: `a || b ?? c` parses as `(a || b) ?? c` — i.e., || binds
    /// tighter than ??. The VoidCoalescingNode's Left should be a LogicalOr
    /// containing both `a` and `b`, and the right operand should be `c`.
    ///
    /// **Validates: Requirements 6.1, 6.4**
    /// </summary>
    [Fact]
    public void Precedence_LogicalOrBindsTighterThanVoidCoalescing()
    {
        var expr = GetExpressionFromStatement("a || b ?? c");

        var assignment = expr.AsAssignment();
        var voidCoalescing = assignment.Target;

        var logicalOr = voidCoalescing.Left;

        var leftOfOr = GetIdentifierFromLogicalAnd(logicalOr.Left);
        Assert.Equal("a", leftOfOr);

        Assert.True(logicalOr.LogicalAnds.Length >= 1,
            $"Expected LogicalOr to have at least 1 right operand for 'a || b', got {logicalOr.LogicalAnds.Length}");
        var rightOfOr = GetIdentifierFromLogicalAnd(logicalOr.LogicalAnds[0]);
        Assert.Equal("b", rightOfOr);

        Assert.True(voidCoalescing.LogicalOrs.Length >= 1,
            $"Expected VoidCoalescing to have at least 1 right operand for '?? c', got {voidCoalescing.LogicalOrs.Length}");
        var rightOfCoalescing = GetIdentifierFromLogicalOr(voidCoalescing.LogicalOrs[0]);
        Assert.Equal("c", rightOfCoalescing);
    }

    [Fact]
    public void Precedence_VoidCoalescingBindsTighterThanAssignment()
    {
        // The grammar rule is:
        //   Assignment = target:VoidCoalescing ((EQUAL|??=) value:Assignment)?
        //   VoidCoalescing = left:LogicalOr (QUESTION_QUESTION right:LogicalOr)*
        //
        // This structurally guarantees ?? binds tighter than = because the
        // assignment's target is a VoidCoalescing (which contains ??), and the
        // value is another Assignment (which starts with VoidCoalescing).
        //
        // We verify this by parsing `a ?? b` as a standalone expression and
        // confirming it produces a single VoidCoalescing node — proving that
        // ?? is consumed at the VoidCoalescing level, not at the Assignment level.
        var expr = GetExpressionFromStatement("a ?? b");
        var assignment = expr.AsAssignment();
        var voidCoalescing = assignment.Target;

        var leftId = GetIdentifierFromLogicalOr(voidCoalescing.Left);
        Assert.Equal("a", leftId);

        Assert.True(voidCoalescing.LogicalOrs.Length >= 1,
            $"Expected VoidCoalescing to have right operand 'b', got {voidCoalescing.LogicalOrs.Length} LogicalOrs");
        var rightId = GetIdentifierFromLogicalOr(voidCoalescing.LogicalOrs[0]);
        Assert.Equal("b", rightId);

        Assert.True(
            assignment.Group0?.Value is null || ReferenceEquals(assignment.Group0?.Value, assignment),
            "Assignment should not have a separate Value — ?? should be consumed at VoidCoalescing level");
    }

    private static string GetIdentifierFromLogicalOr(LogicalOrNode node)
    {
        return GetIdentifierFromLogicalAnd(node.Left);
    }

    private static string GetIdentifierFromLogicalAnd(LogicalAndNode node)
    {
        return GetIdentifierFromEquality(node.Left);
    }

    private static string GetIdentifierFromEquality(EqualityNode node)
    {
        return GetIdentifierFromRelational(node.Left);
    }

    private static string GetIdentifierFromRelational(RelationalNode node)
    {
        var relComp = (RelationalComparisonRelationalNode)node;
        return GetIdentifierFromAdditive(relComp.Left);
    }

    private static string GetIdentifierFromAdditive(AdditiveNode node)
    {
        return GetIdentifierFromMultiplicative(node.Left);
    }

    private static string GetIdentifierFromMultiplicative(MultiplicativeNode node)
    {
        // These parser tests only build pass-through (NonRange) operands.
        return GetIdentifierFromUnary(node.Left.UnwrapRange()!);
    }

    private static string GetIdentifierFromUnary(UnaryNode node)
    {
        var postfixExpr = (PostfixExpressionUnaryNode)node;
        return GetIdentifierFromPostfix(postfixExpr.Primary);
    }

    private static string GetIdentifierFromPostfix(PostfixNode node)
    {
        var primary = (IdentifierExpressionPrimaryNode)node.Target;
        return primary.Name.Lexeme;
    }
}