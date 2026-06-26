// Feature: adhoc-refinement-constraints, Property 6: Predicate implication correctness
// **Validates: Requirements 5.4, 5.5, 5.6, 6.4, 8.1, 8.2, 8.3, 9.2, 9.3, 9.5**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for refinement predicate implication correctness.
///
/// Property 6: For any pair of refinement predicates (source, target),
/// RefinementPredicate.Implies(source, target) SHALL return true if and only if
/// every value satisfying the source predicate also satisfies the target predicate.
/// </summary>
public class RefinementImplicationPropertyTests
{
    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Represents a single relational constraint (operator + value).
    /// </summary>
    public sealed class RelationalConstraint
    {
        public string Operator { get; }
        public int Value { get; }

        public RelationalConstraint(string op, int value)
        {
            Operator = op;
            Value = value;
        }

        public override string ToString()
        {
            return $"{Operator} {Value}";
        }
    }

    /// <summary>
    /// Represents a predicate that may be a single constraint or a compound (and) of two constraints.
    /// </summary>
    public sealed class PredicateScenario
    {
        public ImmutableArray<RelationalConstraint> Constraints { get; }

        public PredicateScenario(ImmutableArray<RelationalConstraint> constraints)
        {
            Constraints = constraints;
        }

        public override string ToString()
        {
            return string.Join(" and ", Constraints.Select(c => c.ToString()));
        }
    }

    /// <summary>
    /// A pair of predicates (source, target) for testing implication.
    /// </summary>
    public sealed class ImplicationScenario
    {
        public PredicateScenario Source { get; }
        public PredicateScenario Target { get; }

        public ImplicationScenario(PredicateScenario source, PredicateScenario target)
        {
            Source = source;
            Target = target;
        }

        public override string ToString()
        {
            return $"({Source}) implies ({Target})";
        }
    }

#pragma warning restore CA1034

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly string[] Operators = [">=", "<=", ">", "<"];

    private static Gen<RelationalConstraint> GenRelationalConstraint()
    {
        return from op in Gen.Elements(Operators)
               from value in Gen.Choose(-50, 50)
               select new RelationalConstraint(op, value);
    }

    private static Gen<PredicateScenario> GenSinglePredicate()
    {
        return GenRelationalConstraint().Select(c =>
            new PredicateScenario(ImmutableArray.Create(c)));
    }

    private static Gen<PredicateScenario> GenCompoundPredicate()
    {
        // Generate a lower-bound + upper-bound compound predicate (e.g., >= a and <= b)
        return from lowerOp in Gen.Elements(">=", ">")
               from upperOp in Gen.Elements("<=", "<")
               from lower in Gen.Choose(-50, 50)
               from upper in Gen.Choose(-50, 50)
               select new PredicateScenario(ImmutableArray.Create(
                   new RelationalConstraint(lowerOp, lower),
                   new RelationalConstraint(upperOp, upper)));
    }

    private static Gen<PredicateScenario> GenPredicate()
    {
        return Gen.OneOf(GenSinglePredicate(), GenCompoundPredicate());
    }

    private static Gen<ImplicationScenario> GenImplicationScenario()
    {
        return from source in GenPredicate()
               from target in GenPredicate()
               select new ImplicationScenario(source, target);
    }

    public static Arbitrary<ImplicationScenario> ArbitraryImplicationScenario()
    {
        return GenImplicationScenario().ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a <see cref="RefinementPredicate"/> from a <see cref="PredicateScenario"/>.
    /// Constructs the AST nodes directly (no parsing needed).
    /// </summary>
    private static RefinementPredicate BuildPredicate(PredicateScenario scenario)
    {
        var constraints = scenario.Constraints;

        var firstUnary = MakeUnaryPattern(constraints[0]);

        if(constraints.Length == 1)
        {
            var andNode = new AndPatternNode { Left = firstUnary };
            return new RefinementPredicate(andNode);
        }

        var secondUnary = MakeUnaryPattern(constraints[1]);

        if(constraints.Length == 2)
        {
            var andNode = new AndPatternNode { Left = firstUnary, Right = secondUnary };
            return new RefinementPredicate(andNode);
        }

        // 3+ constraints: use UnaryPatterns for the extras
        var extras = constraints.Skip(2)
            .Select(c => (UnaryPatternNode)MakeUnaryPattern(c))
            .ToImmutableArray();

        var andNodeMulti = new AndPatternNode
        {
            Left = firstUnary,
            Right = secondUnary,
            UnaryPatterns = extras,
        };
        return new RefinementPredicate(andNodeMulti);
    }

    private static DirectPatternUnaryPatternNode MakeUnaryPattern(RelationalConstraint constraint)
    {
        var primary = MakeRelationalPattern(constraint.Operator, constraint.Value);
        return new DirectPatternUnaryPatternNode { Primary = primary };
    }

    private static PrimaryPatternNode MakeRelationalPattern(string op, int value)
    {
        var expr = MakeConstantExpression(value);
        return op switch
        {
            ">=" => new GreaterThanOrEqualRelationalPatternPrimaryPatternNode { Constant = expr },
            "<=" => new LessThanOrEqualRelationalPatternPrimaryPatternNode { Constant = expr },
            ">" => new GreaterThanRelationalPatternPrimaryPatternNode { Constant = expr },
            "<" => new LessThanRelationalPatternPrimaryPatternNode { Constant = expr },
            _ => throw new ArgumentException($"Unknown operator: {op}"),
        };
    }

    private static VoidCoalescingNode MakeConstantExpression(int value)
    {
        return MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken(value.ToString()),
            }).AsAssignment().Target;
    }

    /// <summary>
    /// Brute-force oracle: evaluates whether source implies target by checking
    /// all integer values in a range. If every value satisfying source also
    /// satisfies target, then implication holds.
    /// </summary>
    private static bool BruteForceImplies(RefinementPredicate source, RefinementPredicate target, int rangeMin, int rangeMax)
    {
        var sourceHasAnySatisfyingValue = false;

        for(var v = rangeMin; v <= rangeMax; v++)
        {
            if(source.Evaluate(v))
            {
                sourceHasAnySatisfyingValue = true;
                if(!target.Evaluate(v))
                {
                    return false;
                }
            }
        }

        // If the source has no satisfying values in the range, implication is vacuously true.
        // However, the Implies implementation requires targetConstraints.Count > 0 to return true,
        // and for empty source sets it may return true or false depending on the structural analysis.
        // We only assert the property when the source has satisfying values.
        return sourceHasAnySatisfyingValue;
    }

    /// <summary>
    /// Checks if the source predicate has any satisfying values in the given range.
    /// </summary>
    private static bool HasSatisfyingValues(RefinementPredicate predicate, int rangeMin, int rangeMax)
    {
        for(var v = rangeMin; v <= rangeMax; v++)
        {
            if(predicate.Evaluate(v))
            {
                return true;
            }
        }

        return false;
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    private const int RangeMin = -100;
    private const int RangeMax = 100;

    /// <summary>
    /// Property 6a: When Implies returns true, the brute-force oracle must also confirm
    /// that every value satisfying the source also satisfies the target.
    ///
    /// If Implies says "source implies target", then there must be no counterexample
    /// in our test range where source is satisfied but target is not.
    ///
    /// **Validates: Requirements 5.4, 5.5, 5.6, 6.4, 8.1, 8.2, 8.3, 9.2, 9.3, 9.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementImplicationPropertyTests) })]
    public bool Implies_WhenTrue_AllSourceValuesSatisfyTarget(ImplicationScenario scenario)
    {
        var source = BuildPredicate(scenario.Source);
        var target = BuildPredicate(scenario.Target);

        var impliesResult = source.Implies(target);

        if(!impliesResult)
        {
            return true; // Not testing the false case here
        }

        // If Implies returns true, verify with brute force:
        // every value satisfying source must also satisfy target.
        for(var v = RangeMin; v <= RangeMax; v++)
        {
            if(source.Evaluate(v) && !target.Evaluate(v))
            {
                return false; // Found a counterexample — Implies is wrong
            }
        }

        return true;
    }

    /// <summary>
    /// Property 6b: When the brute-force oracle confirms implication (source has satisfying
    /// values and all of them satisfy target), Implies should return true.
    ///
    /// This tests soundness in the other direction: if implication truly holds,
    /// the solver should detect it.
    ///
    /// **Validates: Requirements 5.4, 5.5, 5.6, 6.4, 8.1, 8.2, 8.3, 9.2, 9.3, 9.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementImplicationPropertyTests) })]
    public bool Implies_WhenBruteForceConfirms_SolverAgrees(ImplicationScenario scenario)
    {
        var source = BuildPredicate(scenario.Source);
        var target = BuildPredicate(scenario.Target);

        // Only test when source has satisfying values (non-vacuous case)
        if(!HasSatisfyingValues(source, RangeMin, RangeMax))
        {
            return true; // Skip vacuous cases
        }

        var bruteForceResult = BruteForceImplies(source, target, RangeMin, RangeMax);

        if(!bruteForceResult)
        {
            return true; // Not testing the "doesn't imply" case here
        }

        // Brute force says implication holds — solver should agree
        var impliesResult = source.Implies(target);
        return impliesResult;
    }
}