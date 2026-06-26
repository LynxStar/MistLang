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
        if(!EvaluateUnaryPattern(AstNode.Left, constantValue))
        {
            return false;
        }

        // If Right is present and distinct from Left, evaluate it.
        if(AstNode.Right is not null && !ReferenceEquals(AstNode.Right, AstNode.Left))
        {
            if(!EvaluateUnaryPattern(AstNode.Right, constantValue))
            {
                return false;
            }
        }

        // Evaluate any additional UnaryPatterns (for 3+ part and-chains).
        if(!AstNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in AstNode.UnaryPatterns)
            {
                if(!EvaluateUnaryPattern(part, constantValue))
                {
                    return false;
                }
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
        // Extract constraints from both predicates.
        var sourceConstraints = ExtractConstraints(this);
        var targetConstraints = ExtractConstraints(other);

        // For implication to hold, every constraint in the target must be
        // implied by at least one constraint in the source.
        foreach(var target in targetConstraints)
        {
            var implied = false;
            foreach(var source in sourceConstraints)
            {
                if(ConstraintPairImplies(source, target))
                {
                    implied = true;
                    break;
                }
            }

            if(!implied)
            {
                return false;
            }
        }

        return targetConstraints.Count > 0;
    }

    // ─── Description Building ────────────────────────────────────────────

    private static string BuildDescription(AndPatternNode andPattern)
    {
        var parts = new List<string>
        {
            DescribeUnaryPattern(andPattern.Left)
        };

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            parts.Add(DescribeUnaryPattern(andPattern.Right));
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
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

    private static string ExtractExpressionText(VoidCoalescingNode expr)
    {
        var value = TryExtractLongFromVoidCoalescing(expr);
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
        switch(primary)
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
        if(orPattern is null)
        {
            return false;
        }

        // Evaluate the left (first) and-pattern.
        if(EvaluateAndPattern(orPattern.Left, value))
        {
            return true;
        }

        // Evaluate any additional or-alternatives.
        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            if(EvaluateAndPattern(orPattern.Right, value))
            {
                return true;
            }
        }

        if(!orPattern.AndPatterns.IsDefaultOrEmpty)
        {
            foreach(var andAlt in orPattern.AndPatterns)
            {
                if(EvaluateAndPattern(andAlt, value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool EvaluateAndPattern(AndPatternNode andPattern, object value)
    {
        if(!EvaluateUnaryPattern(andPattern.Left, value))
        {
            return false;
        }

        if(andPattern.Right is not null && !ReferenceEquals(andPattern.Right, andPattern.Left))
        {
            if(!EvaluateUnaryPattern(andPattern.Right, value))
            {
                return false;
            }
        }

        if(!andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in andPattern.UnaryPatterns)
            {
                if(!EvaluateUnaryPattern(part, value))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Compares a runtime value to the constant in a relational pattern's expression.
    /// Returns negative if value &lt; constant, zero if equal, positive if value &gt; constant.
    /// Returns <see cref="int.MinValue"/> if comparison is not possible.
    /// </summary>
    private static int CompareToConstant(object value, VoidCoalescingNode constantExpr)
    {
        var constantLong = TryExtractLongFromVoidCoalescing(constantExpr);
        if(!constantLong.HasValue)
        {
            return int.MinValue;
        }

        var valueLong = ConvertToLong(value);
        if(!valueLong.HasValue)
        {
            return int.MinValue;
        }

        return valueLong.Value.CompareTo(constantLong.Value);
    }

    private static bool EqualsConstant(object value, ConstantLiteralNode literal)
    {
        switch(literal)
        {
            case IntegerConstantConstantLiteralNode intLit:
                {
                    var litValue = ParseIntegerLexeme(intLit.Value.Lexeme);
                    var valLong = ConvertToLong(value);
                    return litValue.HasValue && valLong.HasValue && valLong.Value == litValue.Value;
                }
            case CharConstantConstantLiteralNode charLit:
                {
                    if(value is char c && charLit.Value.Lexeme.Length >= 3)
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
                    if(value is not string strValue)
                    {
                        return false;
                    }

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
                    catch(ArgumentException)
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

    private readonly record struct EqualityConstraint(long Value);

    private readonly record struct NotConstraint(long Value);

    private readonly record struct OrConstraint(ImmutableArray<long> Values);

    private enum ConstraintKind { Relational, Equality, Not, Or }

    private readonly record struct Constraint(
        ConstraintKind Kind,
        RelationalConstraint? Relational = null,
        EqualityConstraint? Equality = null,
        NotConstraint? NotValue = null,
        OrConstraint? Or = null);

    private static List<Constraint> ExtractConstraints(RefinementPredicate predicate)
    {
        // Check if the AndPatternNode represents an or-pattern via a parenthesized pattern.
        // Shape: AndPatternNode.Left is DirectPatternUnaryPatternNode containing
        // ParenthesizedPatternPrimaryPatternNode with an OrPatternNode that has multiple alternatives.
        if(TryExtractOrConstraint(predicate.AstNode, out var orConstraint))
        {
            return orConstraint;
        }

        var constraints = new List<Constraint>();
        ExtractConstraintsFromUnary(predicate.AstNode.Left, constraints);

        if(predicate.AstNode.Right is not null &&
            !ReferenceEquals(predicate.AstNode.Right, predicate.AstNode.Left))
        {
            ExtractConstraintsFromUnary(predicate.AstNode.Right, constraints);
        }

        if(!predicate.AstNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            foreach(var part in predicate.AstNode.UnaryPatterns)
            {
                ExtractConstraintsFromUnary(part, constraints);
            }
        }

        return constraints;
    }

    /// <summary>
    /// Attempts to extract an OrConstraint from an AndPatternNode that represents an or-pattern.
    /// Returns true if the node contains a parenthesized or-pattern with all integer constant alternatives.
    /// Returns false if the node does not represent an or-pattern or contains non-integer alternatives.
    /// </summary>
    private static bool TryExtractOrConstraint(AndPatternNode andNode, out List<Constraint> constraints)
    {
        constraints = [];

        // The or-pattern must be the sole content of the AndPatternNode (no Right, no UnaryPatterns).
        if(andNode.Right is not null && !ReferenceEquals(andNode.Right, andNode.Left))
        {
            return false;
        }

        if(!andNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            return false;
        }

        // Left must be a DirectPatternUnaryPatternNode containing a ParenthesizedPatternPrimaryPatternNode.
        if(andNode.Left is not DirectPatternUnaryPatternNode direct)
        {
            return false;
        }

        if(direct.Primary is not ParenthesizedPatternPrimaryPatternNode paren)
        {
            return false;
        }

        // The parenthesized pattern's inner must be a PatternNode with an OrPatternNode.
        var orPattern = paren.Inner?.OrPattern;
        if(orPattern is null)
        {
            return false;
        }

        // Check that the OrPatternNode actually has multiple alternatives.
        // If Right is the same reference as Left and AndPatterns is empty, it's a single alternative (not an or-pattern).
        var hasMultipleAlternatives = (orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
                                     || !orPattern.AndPatterns.IsDefaultOrEmpty;
        if(!hasMultipleAlternatives)
        {
            return false;
        }

        // Extract constant values from each or-alternative.
        var values = new List<long>();

        if(!TryExtractConstantFromAndPattern(orPattern.Left, out var firstValue))
        {
            return false;
        }

        values.Add(firstValue);

        if(orPattern.Right is not null && !ReferenceEquals(orPattern.Right, orPattern.Left))
        {
            if(!TryExtractConstantFromAndPattern(orPattern.Right, out var rightValue))
            {
                return false;
            }

            values.Add(rightValue);
        }

        if(!orPattern.AndPatterns.IsDefaultOrEmpty)
        {
            foreach(var alt in orPattern.AndPatterns)
            {
                if(!TryExtractConstantFromAndPattern(alt, out var altValue))
                {
                    return false;
                }

                values.Add(altValue);
            }
        }

        constraints = [new Constraint(ConstraintKind.Or, Or: new OrConstraint(values.ToImmutableArray()))];
        return true;
    }

    /// <summary>
    /// Extracts a single integer constant value from an AndPatternNode that represents
    /// a single constant alternative in an or-pattern.
    /// The AndPatternNode must contain exactly one DirectPatternUnaryPatternNode with a ConstantPatternPrimaryPatternNode.
    /// </summary>
    private static bool TryExtractConstantFromAndPattern(AndPatternNode andNode, out long value)
    {
        value = 0;

        // Must be a single unary pattern (no Right, no UnaryPatterns).
        if(andNode.Right is not null && !ReferenceEquals(andNode.Right, andNode.Left))
        {
            return false;
        }

        if(!andNode.UnaryPatterns.IsDefaultOrEmpty)
        {
            return false;
        }

        if(andNode.Left is not DirectPatternUnaryPatternNode direct)
        {
            return false;
        }

        if(direct.Primary is not ConstantPatternPrimaryPatternNode constant)
        {
            return false;
        }

        var extracted = TryExtractLongFromLiteral(constant.Literal);
        if(!extracted.HasValue)
        {
            return false;
        }

        value = extracted.Value;
        return true;
    }

    private static void ExtractConstraintsFromUnary(UnaryPatternNode unary, List<Constraint> constraints)
    {
        if(unary is NotPatternUnaryPatternNode notPattern)
        {
            if(notPattern.Operand is ConstantPatternPrimaryPatternNode constant)
            {
                var val = TryExtractLongFromLiteral(constant.Literal);
                if(val.HasValue)
                {
                    constraints.Add(new Constraint(ConstraintKind.Not, NotValue: new NotConstraint(val.Value)));
                }
            }

            return;
        }

        if(unary is not DirectPatternUnaryPatternNode direct)
        {
            return;
        }

        switch(direct.Primary)
        {
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gte:
                {
                    var val = TryExtractLongFromVoidCoalescing(gte.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new Constraint(ConstraintKind.Relational, Relational: new RelationalConstraint(RelOp.Gte, val.Value)));
                    }

                    break;
                }
            case LessThanOrEqualRelationalPatternPrimaryPatternNode lte:
                {
                    var val = TryExtractLongFromVoidCoalescing(lte.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new Constraint(ConstraintKind.Relational, Relational: new RelationalConstraint(RelOp.Lte, val.Value)));
                    }

                    break;
                }
            case GreaterThanRelationalPatternPrimaryPatternNode gt:
                {
                    var val = TryExtractLongFromVoidCoalescing(gt.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new Constraint(ConstraintKind.Relational, Relational: new RelationalConstraint(RelOp.Gt, val.Value)));
                    }

                    break;
                }
            case LessThanRelationalPatternPrimaryPatternNode lt:
                {
                    var val = TryExtractLongFromVoidCoalescing(lt.Constant);
                    if(val.HasValue)
                    {
                        constraints.Add(new Constraint(ConstraintKind.Relational, Relational: new RelationalConstraint(RelOp.Lt, val.Value)));
                    }

                    break;
                }
            case ConstantPatternPrimaryPatternNode constant:
                {
                    var val = TryExtractLongFromLiteral(constant.Literal);
                    if(val.HasValue)
                    {
                        constraints.Add(new Constraint(ConstraintKind.Equality, Equality: new EqualityConstraint(val.Value)));
                    }

                    break;
                }
        }
    }

    /// <summary>
    /// Determines if a source constraint logically implies a target constraint.
    /// Dispatches based on constraint kinds to the appropriate implication logic.
    /// </summary>
    private static bool ConstraintPairImplies(Constraint source, Constraint target)
    {
        return (source.Kind, target.Kind) switch
        {
            (ConstraintKind.Relational, ConstraintKind.Relational) =>
                source.Relational.HasValue && target.Relational.HasValue &&
                ConstraintImplies(source.Relational.Value, target.Relational.Value),

            (ConstraintKind.Equality, ConstraintKind.Relational) =>
                source.Equality.HasValue && target.Relational.HasValue &&
                EqualityImpliesRelational(source.Equality.Value.Value, target.Relational.Value),

            (ConstraintKind.Equality, ConstraintKind.Equality) =>
                source.Equality.HasValue && target.Equality.HasValue &&
                source.Equality.Value.Value == target.Equality.Value.Value,

            (ConstraintKind.Not, ConstraintKind.Not) =>
                source.NotValue.HasValue && target.NotValue.HasValue &&
                source.NotValue.Value.Value == target.NotValue.Value.Value,

            (ConstraintKind.Equality, ConstraintKind.Not) =>
                source.Equality.HasValue && target.NotValue.HasValue &&
                source.Equality.Value.Value != target.NotValue.Value.Value,

            (ConstraintKind.Relational, ConstraintKind.Not) =>
                source.Relational.HasValue && target.NotValue.HasValue &&
                RelationalImpliesNot(source.Relational.Value, target.NotValue.Value.Value),

            (ConstraintKind.Not, ConstraintKind.Relational) => false,

            (ConstraintKind.Not, ConstraintKind.Equality) => false,

            // Or-pattern as target: source must imply membership in the target's value set.
            (ConstraintKind.Equality, ConstraintKind.Or) =>
                source.Equality.HasValue && target.Or.HasValue &&
                target.Or.Value.Values.Contains(source.Equality.Value.Value),

            (ConstraintKind.Or, ConstraintKind.Or) =>
                source.Or.HasValue && target.Or.HasValue &&
                OrSubsetOf(source.Or.Value.Values, target.Or.Value.Values),

            // Or-pattern as source: every alternative must individually imply the target.
            (ConstraintKind.Or, ConstraintKind.Relational) =>
                source.Or.HasValue && target.Relational.HasValue &&
                AllValuesImplyRelational(source.Or.Value.Values, target.Relational.Value),

            (ConstraintKind.Or, ConstraintKind.Not) =>
                source.Or.HasValue && target.NotValue.HasValue &&
                AllValuesNotEqual(source.Or.Value.Values, target.NotValue.Value.Value),

            // Or-pattern does NOT imply a single equality value (disjunction doesn't prove single value).
            (ConstraintKind.Or, ConstraintKind.Equality) => false,

            // Relational bound doesn't prove membership in a finite set.
            (ConstraintKind.Relational, ConstraintKind.Or) => false,

            // Not-pattern doesn't prove membership in a finite set.
            (ConstraintKind.Not, ConstraintKind.Or) => false,

            _ => false
        };
    }

    /// <summary>
    /// Checks if every value in <paramref name="source"/> is contained in <paramref name="target"/> (subset check).
    /// </summary>
    private static bool OrSubsetOf(ImmutableArray<long> source, ImmutableArray<long> target)
    {
        foreach(var value in source)
        {
            if(!target.Contains(value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if every value in the or-pattern satisfies the relational constraint.
    /// </summary>
    private static bool AllValuesImplyRelational(ImmutableArray<long> values, RelationalConstraint target)
    {
        foreach(var value in values)
        {
            if(!EqualityImpliesRelational(value, target))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks if every value in the or-pattern is not equal to the given value.
    /// </summary>
    private static bool AllValuesNotEqual(ImmutableArray<long> values, long notValue)
    {
        foreach(var value in values)
        {
            if(value == notValue)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Determines if a relational constraint implies a not-pattern (inequality).
    /// > a implies not b when a >= b (all values > a are != b when a >= b)
    /// &lt; a implies not b when a &lt;= b (all values &lt; a are != b when a &lt;= b)
    /// >= a implies not b when a > b (all values >= a are != b when a > b)
    /// &lt;= a implies not b when a &lt; b (all values &lt;= a are != b when a &lt; b)
    /// </summary>
    private static bool RelationalImpliesNot(RelationalConstraint source, long notValue)
    {
        return source.Op switch
        {
            RelOp.Gt => source.Value >= notValue,
            RelOp.Lt => source.Value <= notValue,
            RelOp.Gte => source.Value > notValue,
            RelOp.Lte => source.Value < notValue,
            _ => false
        };
    }

    /// <summary>
    /// Determines if an equality constraint (== a) implies a relational constraint (op b).
    /// The equality value satisfies the relational constraint when the value itself meets the bound.
    /// </summary>
    private static bool EqualityImpliesRelational(long value, RelationalConstraint target)
    {
        return target.Op switch
        {
            RelOp.Gte => value >= target.Value,
            RelOp.Lte => value <= target.Value,
            RelOp.Gt => value > target.Value,
            RelOp.Lt => value < target.Value,
            _ => false
        };
    }

    /// <summary>
    /// Determines if a source relational constraint logically implies a target relational constraint.
    /// </summary>
    private static bool ConstraintImplies(RelationalConstraint source, RelationalConstraint target)
    {
        // >= a implies >= b when a >= b
        // >= a implies > b when a > b
        // > a implies >= b when a + 1 >= b (since x > a means x >= a+1)
        // > a implies > b when a >= b
        // <= a implies <= b when a <= b
        // <= a implies < b when a < b
        // < a implies <= b when a - 1 <= b (since x < a means x <= a-1)
        // < a implies < b when a <= b

        return (source.Op, target.Op) switch
        {
            // Source: >= a
            (RelOp.Gte, RelOp.Gte) => source.Value >= target.Value,
            (RelOp.Gte, RelOp.Gt) => source.Value > target.Value,

            // Source: > a (x > a means x >= a+1)
            (RelOp.Gt, RelOp.Gte) => source.Value + 1 >= target.Value,
            (RelOp.Gt, RelOp.Gt) => source.Value >= target.Value,

            // Source: <= a
            (RelOp.Lte, RelOp.Lte) => source.Value <= target.Value,
            (RelOp.Lte, RelOp.Lt) => source.Value < target.Value,

            // Source: < a (x < a means x <= a-1)
            (RelOp.Lt, RelOp.Lte) => source.Value - 1 <= target.Value,
            (RelOp.Lt, RelOp.Lt) => source.Value <= target.Value,

            _ => false
        };
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts a long integer value from a ConstantLiteralNode (used by constant patterns).
    /// Returns null if the literal is not an integer constant.
    /// </summary>
    private static long? TryExtractLongFromLiteral(ConstantLiteralNode literal)
    {
        if(literal is IntegerConstantConstantLiteralNode intLit)
        {
            return ParseIntegerLexeme(intLit.Value.Lexeme);
        }

        return null;
    }

    /// <summary>
    /// Extracts a long integer value from an ExpressionNode by navigating the
    /// full expression hierarchy down to an integer literal primary node.
    /// </summary>
    internal static long? TryExtractLongFromExpression(ExpressionNode expr)
    {
        // Navigate: Expression → Assignment → VoidCoalescing → ... → Primary
        var assignment = expr.AsAssignmentOrNull();
        if(assignment is null)
        {
            return null; // Lambda form is not a constant integer.
        }

        if(assignment.Value is not null)
        {
            return null;
        }

        return TryExtractLongFromVoidCoalescing(assignment.Target);
    }

    /// <summary>
    /// Extracts a long integer value from a <see cref="VoidCoalescingNode"/> by navigating the
    /// expression hierarchy down to an integer literal primary node. Used for relational-pattern
    /// constants, which are bounded to <c>VoidCoalescing</c>.
    /// </summary>
    internal static long? TryExtractLongFromVoidCoalescing(VoidCoalescingNode? voidCoalescing)
    {
        // Navigate: VoidCoalescing → LogicalOr → LogicalAnd → Equality
        //         → Relational → Additive → Multiplicative → Unary → Postfix → Primary
        var logicalOr = voidCoalescing?.Left;
        if(logicalOr?.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            return null;
        }

        var logicalAnd = logicalOr?.Left;
        if(logicalAnd?.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            return null;
        }

        var equality = logicalAnd?.Left;
        if(equality?.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
        {
            return null;
        }

        if(equality?.Left is not RelationalComparisonRelationalNode relCmp)
        {
            return null;
        }

        if(relCmp.Right is not null && !ReferenceEquals(relCmp.Right, relCmp.Left))
        {
            return null;
        }

        var additive = relCmp.Left;
        if(additive?.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
        {
            return null;
        }

        var multiplicative = additive?.Left;
        if(multiplicative?.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
        {
            return null;
        }

        if(multiplicative?.Left is not PostfixExpressionUnaryNode postfixUnary)
        {
            return null;
        }

        var postfix = postfixUnary.Primary;
        if(postfix.Suffix.Length > 0)
        {
            return null;
        }

        if(postfix.Target is not IntegerLiteralExpressionPrimaryNode intLiteral)
        {
            return null;
        }

        return ParseIntegerLexeme(intLiteral.IntegerLiteral.Lexeme);
    }

    internal static long? ParseIntegerLexeme(string lexeme)
    {
        var upper = lexeme.ToUpperInvariant();
        var numericPart = lexeme;
        if(upper.EndsWith("UL") || upper.EndsWith("LU"))
        {
            numericPart = lexeme[..^2];
        }
        else if(upper.EndsWith("L") || upper.EndsWith("U"))
        {
            numericPart = lexeme[..^1];
        }

        if(long.TryParse(numericPart, out var value))
        {
            return value;
        }

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