// Feature: refinement-comparison-engine, Unit tests for unsupported pattern rejection and edge cases
// **Validates: Requirements 4.1, 4.2, 4.3, 7.7**

using Mist.Generated;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Unit tests verifying that the refinement comparison engine gracefully handles
/// unsupported patterns by returning false (not crashing). These cover:
/// - Property patterns (Requirement 4.1)
/// - Regex patterns (Requirement 4.2)
/// - Arithmetic expressions in pattern operands (Requirement 4.3)
/// - Or-patterns with non-constant alternatives (Requirement 7.7)
/// </summary>
public class RefinementComparisonEngineEdgeCaseTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────

    private static RefinementPredicate BuildRelationalPredicate(string op, int value)
    {
        var expr = MakeConstantExpression(value);
        PrimaryPatternNode primary = op switch
        {
            ">=" => new GreaterThanOrEqualRelationalPatternPrimaryPatternNode { Constant = expr },
            "<=" => new LessThanOrEqualRelationalPatternPrimaryPatternNode { Constant = expr },
            ">" => new GreaterThanRelationalPatternPrimaryPatternNode { Constant = expr },
            "<" => new LessThanRelationalPatternPrimaryPatternNode { Constant = expr },
            _ => throw new ArgumentException($"Unknown operator: {op}"),
        };

        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var andNode = new AndPatternNode { Left = unary };
        return new RefinementPredicate(andNode);
    }

    private static RefinementPredicate BuildEqualityPredicate(int value)
    {
        var constantLiteral = new IntegerConstantConstantLiteralNode
        {
            Value = MistTypeGenerator.MakeToken(value.ToString()),
        };
        var constantPattern = new ConstantPatternPrimaryPatternNode { Literal = constantLiteral };
        var unary = new DirectPatternUnaryPatternNode { Primary = constantPattern };
        var andNode = new AndPatternNode { Left = unary };
        return new RefinementPredicate(andNode);
    }

    private static VoidCoalescingNode MakeConstantExpression(int value)
    {
        return MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken(value.ToString()),
            }).AsAssignment().Target;
    }

    // ─── Test 1: Property pattern rejection (Requirement 4.1) ────────────

    /// <summary>
    /// A predicate containing a property pattern should cause Implies to return false
    /// without throwing an exception. Property patterns cannot be reasoned about
    /// by the comparison engine.
    /// </summary>
    [Fact]
    public void PropertyPattern_AsSource_ImpliesRelational_ReturnsFalse()
    {
        // Build a property pattern: { Length > 5 }
        var propertyPattern = new PropertyPatternPrimaryPatternNode
        {
            TypeName = null,
            Subpatterns = new PropertySubpatternListNode
            {
                PropertySubpattern = new PropertySubpatternNode
                {
                    Name = MistTypeGenerator.MakeToken("Length"),
                    Pattern = MakePropertySubpatternPattern(),
                },
            },
            Name = null,
        };

        var unary = new DirectPatternUnaryPatternNode { Primary = propertyPattern };
        var andNode = new AndPatternNode { Left = unary };
        var source = new RefinementPredicate(andNode);

        var target = BuildRelationalPredicate(">=", 0);

        // Should return false without throwing.
        var result = source.Implies(target);
        Assert.False(result);
    }

    /// <summary>
    /// A predicate containing a property pattern as the target should cause Implies
    /// to return false without throwing.
    /// </summary>
    [Fact]
    public void PropertyPattern_AsTarget_ImpliesFromEquality_ReturnsFalse()
    {
        var propertyPattern = new PropertyPatternPrimaryPatternNode
        {
            TypeName = null,
            Subpatterns = new PropertySubpatternListNode
            {
                PropertySubpattern = new PropertySubpatternNode
                {
                    Name = MistTypeGenerator.MakeToken("Value"),
                    Pattern = MakePropertySubpatternPattern(),
                },
            },
            Name = null,
        };

        var unary = new DirectPatternUnaryPatternNode { Primary = propertyPattern };
        var andNode = new AndPatternNode { Left = unary };
        var target = new RefinementPredicate(andNode);

        var source = BuildEqualityPredicate(7);

        // Should return false without throwing.
        var result = source.Implies(target);
        Assert.False(result);
    }

    // ─── Test 2: Regex pattern rejection (Requirement 4.2) ───────────────

    /// <summary>
    /// A predicate containing a regex constant pattern should cause Implies to return false
    /// without throwing. Regex patterns cannot be reasoned about by the comparison engine.
    /// </summary>
    [Fact]
    public void RegexPattern_AsSource_ImpliesRelational_ReturnsFalse()
    {
        // Build a regex constant pattern: `[a-z]+`
        var regexLiteral = new RegexConstantConstantLiteralNode
        {
            Value = MistTypeGenerator.MakeToken("`[a-z]+`"),
        };
        var constantPattern = new ConstantPatternPrimaryPatternNode { Literal = regexLiteral };
        var unary = new DirectPatternUnaryPatternNode { Primary = constantPattern };
        var andNode = new AndPatternNode { Left = unary };
        var source = new RefinementPredicate(andNode);

        var target = BuildRelationalPredicate(">=", 0);

        // Should return false without throwing.
        var result = source.Implies(target);
        Assert.False(result);
    }

    /// <summary>
    /// A predicate containing a regex constant pattern as the target should cause Implies
    /// to return false without throwing.
    /// </summary>
    [Fact]
    public void RegexPattern_AsTarget_ImpliesFromEquality_ReturnsFalse()
    {
        var regexLiteral = new RegexConstantConstantLiteralNode
        {
            Value = MistTypeGenerator.MakeToken("`\\d+`"),
        };
        var constantPattern = new ConstantPatternPrimaryPatternNode { Literal = regexLiteral };
        var unary = new DirectPatternUnaryPatternNode { Primary = constantPattern };
        var andNode = new AndPatternNode { Left = unary };
        var target = new RefinementPredicate(andNode);

        var source = BuildEqualityPredicate(42);

        // Should return false without throwing.
        var result = source.Implies(target);
        Assert.False(result);
    }

    // ─── Test 3: Arithmetic expression non-extraction (Requirement 4.3) ──

    /// <summary>
    /// A relational pattern whose constant operand is an arithmetic expression (e.g., 3 + 4)
    /// should not be extractable. TryExtractLongFromExpression returns null for non-literal
    /// expressions, so Implies should return false.
    /// </summary>
    [Fact]
    public void ArithmeticExpression_InRelationalPattern_ImpliesReturnsFalse()
    {
        // Build an expression representing "3 + 4" (an additive binary expression).
        var leftExpr = MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken("3"),
            });
        var rightExpr = MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken("4"),
            });
        var arithmeticExpr = MistAstGenerator.MakeAdditive(leftExpr, rightExpr, "+");

        // Build a >= pattern with the arithmetic expression as the constant operand.
        var gtePrimary = new GreaterThanOrEqualRelationalPatternPrimaryPatternNode
        {
            Constant = arithmeticExpr.AsAssignment().Target,
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = gtePrimary };
        var andNode = new AndPatternNode { Left = unary };
        var source = new RefinementPredicate(andNode);

        // Target: >= 5 (a simple relational predicate).
        var target = BuildRelationalPredicate(">=", 5);

        // Should return false because the arithmetic expression cannot be extracted.
        var result = source.Implies(target);
        Assert.False(result);
    }

    /// <summary>
    /// An arithmetic expression as the target's constant operand should also prevent extraction,
    /// causing Implies to return false.
    /// </summary>
    [Fact]
    public void ArithmeticExpression_InTargetRelationalPattern_ImpliesReturnsFalse()
    {
        var source = BuildRelationalPredicate(">=", 10);

        // Build a target with arithmetic expression "2 * 5".
        var leftExpr = MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken("2"),
            });
        var rightExpr = MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken("5"),
            });
        var arithmeticExpr = MistAstGenerator.MakeMultiplicative(leftExpr, rightExpr, "*");

        var gtePrimary = new GreaterThanOrEqualRelationalPatternPrimaryPatternNode
        {
            Constant = arithmeticExpr.AsAssignment().Target,
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = gtePrimary };
        var andNode = new AndPatternNode { Left = unary };
        var target = new RefinementPredicate(andNode);

        // Should return false because the target's arithmetic expression cannot be extracted.
        var result = source.Implies(target);
        Assert.False(result);
    }

    // ─── Test 4: Or-pattern with non-constant alternatives (Req 7.7) ─────

    /// <summary>
    /// An or-pattern where one alternative is a type pattern (not an integer constant)
    /// should cause Implies to return false. The engine cannot reason about non-constant
    /// alternatives in or-patterns.
    /// </summary>
    [Fact]
    public void OrPattern_WithTypePatternAlternative_ImpliesReturnsFalse()
    {
        // Build an or-pattern where the first alternative is a constant (7)
        // and the second is a type pattern (int x).
        var constantLiteral = new IntegerConstantConstantLiteralNode
        {
            Value = MistTypeGenerator.MakeToken("7"),
        };
        var constantPrimary = new ConstantPatternPrimaryPatternNode { Literal = constantLiteral };
        var constantDirect = new DirectPatternUnaryPatternNode { Primary = constantPrimary };
        var constantAnd = new AndPatternNode { Left = constantDirect };

        // Type pattern alternative: int x
        var typePatternPrimary = new TypePatternPrimaryPatternNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = new IntTypePrimitiveTypeNode(),
                }
            },
            Name = MistTypeGenerator.MakeToken("x"),
        };
        var typeDirect = new DirectPatternUnaryPatternNode { Primary = typePatternPrimary };
        var typeAnd = new AndPatternNode { Left = typeDirect };

        // Build OrPatternNode with constant as Left and type pattern as Right.
        var orNode = new OrPatternNode { Left = constantAnd, Right = typeAnd };
        var patternNode = new PatternNode { OrPattern = orNode };
        var paren = new ParenthesizedPatternPrimaryPatternNode { Inner = patternNode };
        var outerDirect = new DirectPatternUnaryPatternNode { Primary = paren };
        var rootAnd = new AndPatternNode { Left = outerDirect };
        var source = new RefinementPredicate(rootAnd);

        var target = BuildRelationalPredicate(">=", 0);

        // Should return false because the or-pattern contains a non-constant alternative.
        var result = source.Implies(target);
        Assert.False(result);
    }

    /// <summary>
    /// An or-pattern where one alternative is a relational pattern (not a simple constant)
    /// should cause Implies to return false.
    /// </summary>
    [Fact]
    public void OrPattern_WithRelationalPatternAlternative_ImpliesReturnsFalse()
    {
        // Build an or-pattern where the first alternative is a constant (5)
        // and the second is a relational pattern (>= 10).
        var constantLiteral = new IntegerConstantConstantLiteralNode
        {
            Value = MistTypeGenerator.MakeToken("5"),
        };
        var constantPrimary = new ConstantPatternPrimaryPatternNode { Literal = constantLiteral };
        var constantDirect = new DirectPatternUnaryPatternNode { Primary = constantPrimary };
        var constantAnd = new AndPatternNode { Left = constantDirect };

        // Relational pattern alternative: >= 10
        var relExpr = MakeConstantExpression(10);
        var relPrimary = new GreaterThanOrEqualRelationalPatternPrimaryPatternNode { Constant = relExpr };
        var relDirect = new DirectPatternUnaryPatternNode { Primary = relPrimary };
        var relAnd = new AndPatternNode { Left = relDirect };

        // Build OrPatternNode with constant as Left and relational as Right.
        var orNode = new OrPatternNode { Left = constantAnd, Right = relAnd };
        var patternNode = new PatternNode { OrPattern = orNode };
        var paren = new ParenthesizedPatternPrimaryPatternNode { Inner = patternNode };
        var outerDirect = new DirectPatternUnaryPatternNode { Primary = paren };
        var rootAnd = new AndPatternNode { Left = outerDirect };
        var source = new RefinementPredicate(rootAnd);

        var target = BuildRelationalPredicate(">=", 0);

        // Should return false because the or-pattern contains a non-constant alternative.
        var result = source.Implies(target);
        Assert.False(result);
    }

    // ─── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a PatternNode for use inside a PropertySubpatternNode.
    /// Wraps a relational pattern (> 5) into the full PatternNode structure.
    /// </summary>
    private static PatternNode MakePropertySubpatternPattern()
    {
        var expr = MakeConstantExpression(5);
        var primary = new GreaterThanRelationalPatternPrimaryPatternNode { Constant = expr };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }
}