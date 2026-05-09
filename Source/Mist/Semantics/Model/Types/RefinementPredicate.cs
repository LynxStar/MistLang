using System.Collections.Immutable;
using Mist.Generated;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// Represents the constraint predicate attached to a refinement type.
/// Stores the pattern AST and provides evaluation and implication checking.
/// </summary>
public sealed class RefinementPredicate
{
    /// <summary>
    /// The pattern AST node for emission and structural analysis.
    /// </summary>
    public AndPatternNode AstNode { get; }

    /// <summary>
    /// Human-readable description of the predicate (e.g., ">= 18", ">= 0 and &lt;= 100").
    /// </summary>
    public string Description { get; }

    public RefinementPredicate(AndPatternNode astNode)
    {
        AstNode = astNode;
        Description = BuildDescription(astNode);
    }

    /// <summary>
    /// Evaluates the predicate against a compile-time constant value.
    /// Returns true if the value satisfies the predicate, false otherwise.
    /// Handles integer, boolean, and character literals against relational patterns
    /// and compound <c>and</c> patterns.
    /// </summary>
    public bool Evaluate(object constantValue)
    {
        // All parts of the AndPattern must be satisfied.
        if (!EvaluateUnaryPattern(AstNode.Left, constantValue))
            return false;

        // If Right is present and distinct from Left, evaluate it.
        if (AstNode.Right is not null && !ReferenceEquals(AstNode.Right, AstNode.Left))
        {
            if (!EvaluateUnaryPattern(AstNode.Right, constantValue))
                return false;
        }

        // Evaluate any additional UnaryPatterns (for 3+ part and-chains).
        if (!AstNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in AstNode.UnaryPatterns)
            {
                if (!EvaluateUnaryPattern(part, constantValue))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if this predicate logically implies another predicate.
    /// Used for subset relationship proving. If this returns true, any value
    /// satisfying <c>this</c> predicate also satisfies <paramref name="other"/>.
    /// </summary>
    public bool Implies(RefinementPredicate other)
    {
        // Extract relational constraints from both predicates.
        var sourceConstraints = ExtractConstraints(this);
        var targetConstraints = ExtractConstraints(other);

        // For implication to hold, every constraint in the target must be
        // implied by at least one constraint in the source.
        foreach (var target in targetConstraints)
        {
            var implied = false;
            foreach (var source in sourceConstraints)
            {
                if (ConstraintImplies(source, target))
                {
                    implied = true;
                    break;
                }
            }

            if (!implied)
                return false;
        }

        return targetConstraints.Count > 0;
    }

    // ─── Description Building ────────────────────────────────────────────

    private static string BuildDescription(AndPatternNode andPattern)
    {
        var parts = new List<string>();
        parts.Add(DescribeUnaryPattern(andPattern.Left));

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            parts.Add(DescribeUnaryPattern(andPattern.Right));
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                parts.Add(DescribeUnaryPattern(part));
            }
        }

        return string.Join(" and ", parts);
    }

    private static string DescribeUnaryPattern(UnaryPatternNode unary)
    {
        return unary switch
        {
            DirectPatternUnaryPatternNode direct => DescribePrimaryPattern(direct.Primary),
            NotPatternUnaryPatternNode not => $"not {DescribePrimaryPattern(not.Operand)}",
            _ => "?"
        };
    }

    private static string DescribePrimaryPattern(PrimaryPatternNode primary)
    {
        return primary switch
        {
            GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte =>
                $">= {ExtractExpressionText(gte.Constant)}",
            LessThanOrEqualRelationalPatternPrimaryPatternNode lte =>
                $"<= {ExtractExpressionText(lte.Constant)}",
            GreaterThanRelationalPatternPrimaryPatternNode gt =>
                $"> {ExtractExpressionText(gt.Constant)}",
            LessThanRelationalPatternPrimaryPatternNode lt =>
                $"< {ExtractExpressionText(lt.Constant)}",
            ConstantPatternPrimaryPatternNode constant =>
                ExtractConstantLiteralText(constant.Literal),
            _ => "?"
        };
    }

    private static string ExtractExpressionText(ExpressionNode expr)
    {
        var value = TryExtractLongFromExpression(expr);
        return value.HasValue ? value.Value.ToString() : "?";
    }

    private static string ExtractConstantLiteralText(ConstantLiteralNode literal)
    {
        return literal switch
        {
            IntegerConstantConstantLiteralNode intLit => intLit.Value.Lexeme,
            CharConstantConstantLiteralNode charLit => charLit.Value.Lexeme,
            TrueConstantConstantLiteralNode => "true",
            FalseConstantConstantLiteralNode => "false",
            StringConstantConstantLiteralNode strLit => strLit.Value.Lexeme,
            FloatConstantConstantLiteralNode floatLit => floatLit.Value.Lexeme,
            RegexConstantConstantLiteralNode regexLit => regexLit.Value.Lexeme,
            _ => "?"
        };
    }

    // ─── Evaluation ──────────────────────────────────────────────────────

    private static bool EvaluateUnaryPattern(UnaryPatternNode unary, object value)
    {
        return unary switch
        {
            DirectPatternUnaryPatternNode direct => EvaluatePrimaryPattern(direct.Primary, value),
            NotPatternUnaryPatternNode not => !EvaluatePrimaryPattern(not.Operand, value),
            _ => false
        };
    }

    private static bool EvaluatePrimaryPattern(PrimaryPatternNode primary, object value)
    {
        switch (primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
                return CompareToConstant(value, gte.Constant) >= 0;

            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
                return CompareToConstant(value, lte.Constant) <= 0;

            case GreaterThanRelationalPatternPrimaryPatternNode gt:
                return CompareToConstant(value, gt.Constant) > 0;

            case LessThanRelationalPatternPrimaryPatternNode lt:
                return CompareToConstant(value, lt.Constant) < 0;

            case ConstantPatternPrimaryPatternNode constant:
                return EqualsConstant(value, constant.Literal);

            case ParenthesizedPatternPrimaryPatternNode paren:
                return EvaluateParenthesizedPattern(paren.Inner, value);

            default:
                // Property patterns, type patterns, etc. cannot be evaluated at compile time.
                return false;
        }
    }

    private static bool EvaluateParenthesizedPattern(PatternNode pattern, object value)
    {
        // PatternNode → OrPatternNode → AndPatternNode
        var orPattern = pattern.OrPattern;
        if (orPattern is null) return false;

        // Evaluate the left (first) and-pattern.
        if (EvaluateAndPattern(orPattern.Left, value))
            return true;

        // Evaluate any additional or-alternatives.
        if (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            if (EvaluateAndPattern(orPattern.Right, value))
                return true;
        }

        if (!orPattern.AndPatterns.IsDefaultOrEmpty)
        {
            foreach (var andAlt in orPattern.AndPatterns)
            {
                if (EvaluateAndPattern(andAlt, value))
                    return true;
            }
        }

        return false;
    }

    private static bool EvaluateAndPattern(AndPatternNode andPattern, object value)
    {
        if (!EvaluateUnaryPattern(andPattern.Left, value))
            return false;

        if (andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            if (!EvaluateUnaryPattern(andPattern.Right, value))
                return false;
        }

        if (!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in andPattern.UnaryPatterns)
            {
                if (!EvaluateUnaryPattern(part, value))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares a runtime value to the constant in a relational pattern's expression.
    /// Returns negative if value &lt; constant, zero if equal, positive if value &gt; constant.
    /// Returns <see cref="int.MinValue"/> if comparison is not possible.
    /// </summary>
    private static int CompareToConstant(object value, ExpressionNode constantExpr)
    {
        var constantLong = TryExtractLongFromExpression(constantExpr);
        if (!constantLong.HasValue) return int.MinValue;

        var valueLong = ConvertToLong(value);
        if (!valueLong.HasValue) return int.MinValue;

        return valueLong.Value.CompareTo(constantLong.Value);
    }

    private static bool EqualsConstant(object value, ConstantLiteralNode literal)
    {
        switch (literal)
        {
            case IntegerConstantConstantLiteralNode intLit:
            {
                var litValue = ParseIntegerLexeme(intLit.Value.Lexeme);
                var valLong = ConvertToLong(value);
                return litValue.HasValue && valLong.HasValue && valLong.Value == litValue.Value;
            }
            case CharConstantConstantLiteralNode charLit:
            {
                if (value is char c && charLit.Value.Lexeme.Length >= 3)
                {
                    // Lexeme is like 'a' — extract the character.
                    return c == charLit.Value.Lexeme[1];
                }
                return false;
            }
            case TrueConstantConstantLiteralNode:
                return value is true;
            case FalseConstantConstantLiteralNode:
                return value is false;
            case RegexConstantConstantLiteralNode regexConst:
            {
                // Evaluate regex against string value using full-match semantics.
                if (value is not string strValue)
                    return false;

                // Strip backtick delimiters and resolve escape sequences.
                var lexeme = regexConst.Value.Lexeme;
                var raw = lexeme.Length >= 2 ? lexeme[1..^1] : string.Empty;
                var pattern = raw.Replace("\\`", "`").Replace("\\\\", "\\");

                try
                {
                    // Full-match semantics: anchor with ^(?:...)$
                    var fullMatchPattern = $"^(?:{pattern})$";
                    return System.Text.RegularExpressions.Regex.IsMatch(strValue, fullMatchPattern);
                }
                catch (System.ArgumentException)
                {
                    // Invalid regex pattern — cannot evaluate, treat as non-matching.
                    return false;
                }
            }
            default:
                return false;
        }
    }

    // ─── Implication / Subset Proving ────────────────────────────────────

    private enum RelOp { Gte, Lte, Gt, Lt }

    private readonly record struct RelationalConstraint(RelOp Op, long Value);

    private static List<RelationalConstraint> ExtractConstraints(RefinementPredicate predicate)
    {
        var constraints = new List<RelationalConstraint>();
        ExtractConstraintsFromUnary(predicate.AstNode.Left, constraints);

        if (predicate.AstNode.Right is not null &&
            !ReferenceEquals(predicate.AstNode.Right, predicate.AstNode.Left))
        {
            ExtractConstraintsFromUnary(predicate.AstNode.Right, constraints);
        }

        if (!predicate.AstNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach (var part in predicate.AstNode.UnaryPatterns)
            {
                ExtractConstraintsFromUnary(part, constraints);
            }
        }

        return constraints;
    }

    private static void ExtractConstraintsFromUnary(UnaryPatternNode unary, List<RelationalConstraint> constraints)
    {
        if (unary is not DirectPatternUnaryPatternNode direct) return;

        switch (direct.Primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
            {
                var val = TryExtractLongFromExpression(gte.Constant);
                if (val.HasValue) constraints.Add(new RelationalConstraint(RelOp.Gte, val.Value));
                break;
            }
            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
            {
                var val = TryExtractLongFromExpression(lte.Constant);
                if (val.HasValue) constraints.Add(new RelationalConstraint(RelOp.Lte, val.Value));
                break;
            }
            case GreaterThanRelationalPatternPrimaryPatternNode gt:
            {
                var val = TryExtractLongFromExpression(gt.Constant);
                if (val.HasValue) constraints.Add(new RelationalConstraint(RelOp.Gt, val.Value));
                break;
            }
            case LessThanRelationalPatternPrimaryPatternNode lt:
            {
                var val = TryExtractLongFromExpression(lt.Constant);
                if (val.HasValue) constraints.Add(new RelationalConstraint(RelOp.Lt, val.Value));
                break;
            }
        }
    }

    /// <summary>
    /// Determines if a source constraint logically implies a target constraint.
    /// </summary>
    private static bool ConstraintImplies(RelationalConstraint source, RelationalConstraint target)
    {
        // >= a implies >= b when a >= b
        // >= a implies > b when a > b
        // > a implies >= b when a >= b (since x > a means x >= a+1, so if a >= b then x >= b)
        // > a implies > b when a >= b
        // <= a implies <= b when a <= b
        // <= a implies < b when a < b
        // < a implies <= b when a <= b (since x < a means x <= a-1, so if a <= b then x <= b)
        // < a implies < b when a <= b

        return (source.Op, target.Op) switch
        {
            // Source: >= a
            (RelOp.Gte, RelOp.Gte) => source.Value >= target.Value,
            (RelOp.Gte, RelOp.Gt) => source.Value > target.Value,

            // Source: > a
            (RelOp.Gt, RelOp.Gte) => source.Value >= target.Value,
            (RelOp.Gt, RelOp.Gt) => source.Value >= target.Value,

            // Source: <= a
            (RelOp.Lte, RelOp.Lte) => source.Value <= target.Value,
            (RelOp.Lte, RelOp.Lt) => source.Value < target.Value,

            // Source: < a
            (RelOp.Lt, RelOp.Lte) => source.Value <= target.Value,
            (RelOp.Lt, RelOp.Lt) => source.Value <= target.Value,

            _ => false
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a long integer value from an ExpressionNode by navigating the
    /// full expression hierarchy down to an integer literal primary node.
    /// </summary>
    internal static long? TryExtractLongFromExpression(ExpressionNode expr)
    {
        // Navigate: Expression → Assignment → LogicalOr → LogicalAnd → Equality
        //         → Relational → Additive → Multiplicative → Unary → Postfix → Primary
        var assignment = expr.Assignment;
        if (assignment?.Value is not null) return null;

        var logicalOr = assignment?.Target;
        if (logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left)) return null;

        var logicalAnd = logicalOr?.Left;
        if (logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left)) return null;

        var equality = logicalAnd?.Left;
        if (equality?.Right is not null && !ReferenceEquals(equality.Right, equality.Left)) return null;

        if (equality?.Left is not RelationalComparisonRelationalNode relCmp) return null;
        if (relCmp.Right is not null && !ReferenceEquals(relCmp.Right, relCmp.Left)) return null;

        var additive = relCmp.Left;
        if (additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left)) return null;

        var multiplicative = additive?.Left;
        if (multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left)) return null;

        if (multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary) return null;

        var postfix = postfixUnary.Primary;
        if (postfix.Suffix.Length > 0) return null;

        if (postfix.Target is not IntegerLiteralExpressionPrimaryNode intLiteral) return null;

        return ParseIntegerLexeme(intLiteral.IntegerLiteral.Lexeme);
    }

    internal static long? ParseIntegerLexeme(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();
        var numericPart = lexeme;
        if (upper.EndsWith("UL") || upper.EndsWith("LU"))
            numericPart = lexeme[..^2];
        else if (upper.EndsWith("L") || upper.EndsWith("U"))
            numericPart = lexeme[..^1];

        if (long.TryParse(numericPart, out var value))
            return value;

        return null;
    }

    private static long? ConvertToLong(object value)
    {
        return value switch
        {
            int i => i,
            long l => l,
            short s => s,
            byte b => b,
            sbyte sb => sb,
            ushort us => us,
            uint ui => ui,
            char c => c,
            bool bo => bo ? 1 : 0,
            _ => null
        };
    }
}
