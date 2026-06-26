// Feature: refinement-comparison-engine, Property 1: Equality-to-relational equivalence
// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Generated;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for the refinement comparison engine expansion.
///
/// Property 1: Equality-to-relational equivalence
/// For any integer value `a` and any relational constraint target,
/// Implies(== a, target) returns the same result as Implies(>= a and &lt;= a, target).
/// </summary>
public class RefinementComparisonEnginePropertyTests
{
    // ─── Scenario types ──────────────────────────────────────────────────

#pragma warning disable CA1034 // nested types are the FsCheck-friendly way to surface scenario records

    /// <summary>
    /// Represents a relational constraint target (operator + value).
    /// </summary>
    public sealed class RelationalTarget
    {
        public string Operator { get; }
        public int Value { get; }

        public RelationalTarget(string op, int value)
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
    /// A scenario pairing an equality value with a relational target.
    /// </summary>
    public sealed class EqualityRelationalScenario
    {
        public int EqualityValue { get; }
        public RelationalTarget Target { get; }

        public EqualityRelationalScenario(int equalityValue, RelationalTarget target)
        {
            EqualityValue = equalityValue;
            Target = target;
        }

        public override string ToString()
        {
            return $"(== {EqualityValue}) implies ({Target})";
        }
    }

    /// <summary>
    /// A scenario for not-pattern tests pairing a not-value with a relational target and an equality target.
    /// </summary>
    public sealed class NotPatternScenario
    {
        public int NotValue { get; }
        public RelationalTarget RelationalTarget { get; }
        public int EqualityTarget { get; }

        public NotPatternScenario(int notValue, RelationalTarget relationalTarget, int equalityTarget)
        {
            NotValue = notValue;
            RelationalTarget = relationalTarget;
            EqualityTarget = equalityTarget;
        }

        public override string ToString()
        {
            return $"(not {NotValue}) vs relational ({RelationalTarget}), equality (== {EqualityTarget})";
        }
    }

    /// <summary>
    /// A scenario pairing two integer values for cross-operator relational implication testing.
    /// </summary>
    public sealed class CrossOperatorScenario
    {
        public int A { get; }
        public int B { get; }

        public CrossOperatorScenario(int a, int b)
        {
            A = a;
            B = b;
        }

        public override string ToString()
        {
            return $"a={A}, b={B}";
        }
    }

    /// <summary>
    /// A scenario pairing an or-pattern source (set of integer values) with a relational target.
    /// </summary>
    public sealed class OrPatternSourceScenario
    {
        public ImmutableArray<int> OrValues { get; }
        public RelationalTarget Target { get; }

        public OrPatternSourceScenario(ImmutableArray<int> orValues, RelationalTarget target)
        {
            OrValues = orValues;
            Target = target;
        }

        public override string ToString()
        {
            return $"({string.Join(" or ", OrValues)}) implies ({Target})";
        }
    }

    /// <summary>
    /// A scenario pairing two sets of integer values for or-pattern subset implication testing.
    /// </summary>
    public sealed class OrPatternSubsetScenario
    {
        public ImmutableArray<int> A { get; }
        public ImmutableArray<int> B { get; }

        public OrPatternSubsetScenario(ImmutableArray<int> a, ImmutableArray<int> b)
        {
            A = a;
            B = b;
        }

        public override string ToString()
        {
            return $"({string.Join(" or ", A)}) implies ({string.Join(" or ", B)})";
        }
    }

    /// <summary>
    /// A scenario for or-pattern target membership testing: an equality value and a set of values.
    /// </summary>
    public sealed class OrPatternTargetScenario
    {
        public int EqualityValue { get; }
        public ImmutableArray<int> SetValues { get; }

        public OrPatternTargetScenario(int equalityValue, ImmutableArray<int> setValues)
        {
            EqualityValue = equalityValue;
            SetValues = setValues;
        }

        public override string ToString()
        {
            return $"(== {EqualityValue}) implies or({string.Join(", ", SetValues)})";
        }
    }

    /// <summary>
    /// Represents a predicate of any supported type for soundness testing.
    /// </summary>
    public sealed class AnyPredicate
    {
        public enum PredicateKind { Relational, Equality, Not, Or }

        public PredicateKind Kind { get; }
        public string? Operator { get; }
        public int Value { get; }
        public ImmutableArray<int> OrValues { get; }

        private AnyPredicate(PredicateKind kind, string? op, int value, ImmutableArray<int> orValues)
        {
            Kind = kind;
            Operator = op;
            Value = value;
            OrValues = orValues;
        }

        public static AnyPredicate Relational(string op, int value)
        {
            return new(PredicateKind.Relational, op, value, ImmutableArray<int>.Empty);
        }

        public static AnyPredicate Equality(int value)
        {
            return new(PredicateKind.Equality, null, value, ImmutableArray<int>.Empty);
        }

        public static AnyPredicate Not(int value)
        {
            return new(PredicateKind.Not, null, value, ImmutableArray<int>.Empty);
        }

        public static AnyPredicate Or(ImmutableArray<int> values)
        {
            return new(PredicateKind.Or, null, 0, values);
        }

        public override string ToString()
        {
            return Kind switch
            {
                PredicateKind.Relational => $"{Operator} {Value}",
                PredicateKind.Equality => $"== {Value}",
                PredicateKind.Not => $"not {Value}",
                PredicateKind.Or => $"({string.Join(" or ", OrValues)})",
                _ => "?"
            };
        }
    }

    /// <summary>
    /// A scenario pairing two arbitrary predicates for soundness testing.
    /// </summary>
    public sealed class SoundnessScenario
    {
        public AnyPredicate Source { get; }
        public AnyPredicate Target { get; }

        public SoundnessScenario(AnyPredicate source, AnyPredicate target)
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

    private static readonly string[] RelationalOperators = [">=", "<=", ">", "<"];

    private static Gen<RelationalTarget> GenRelationalTarget()
    {
        return from op in Gen.Elements(RelationalOperators)
               from value in Gen.Choose(-50, 50)
               select new RelationalTarget(op, value);
    }

    private static Gen<EqualityRelationalScenario> GenEqualityRelationalScenario()
    {
        return from eqValue in Gen.Choose(-50, 50)
               from target in GenRelationalTarget()
               select new EqualityRelationalScenario(eqValue, target);
    }

    public static Arbitrary<EqualityRelationalScenario> ArbitraryEqualityRelationalScenario()
    {
        return GenEqualityRelationalScenario().ToArbitrary();
    }

    private static Gen<CrossOperatorScenario> GenCrossOperatorScenario()
    {
        return from a in Gen.Choose(-50, 50)
               from b in Gen.Choose(-50, 50)
               select new CrossOperatorScenario(a, b);
    }

    public static Arbitrary<CrossOperatorScenario> ArbitraryCrossOperatorScenario()
    {
        return GenCrossOperatorScenario().ToArbitrary();
    }

    private static Gen<NotPatternScenario> GenNotPatternScenario()
    {
        return from notValue in Gen.Choose(-50, 50)
               from relTarget in GenRelationalTarget()
               from eqTarget in Gen.Choose(-50, 50)
               select new NotPatternScenario(notValue, relTarget, eqTarget);
    }

    public static Arbitrary<NotPatternScenario> ArbitraryNotPatternScenario()
    {
        return GenNotPatternScenario().ToArbitrary();
    }

    private static Gen<OrPatternSourceScenario> GenOrPatternSourceScenario()
    {
        return from values in Gen.Choose(-50, 50).ListOf()
                   .Where(list => list.Count is >= 2 and <= 5)
               from target in GenRelationalTarget()
               select new OrPatternSourceScenario(values.ToImmutableArray(), target);
    }

    public static Arbitrary<OrPatternSourceScenario> ArbitraryOrPatternSourceScenario()
    {
        return GenOrPatternSourceScenario().ToArbitrary();
    }

    private static Gen<OrPatternSubsetScenario> GenOrPatternSubsetScenario()
    {
        return from aValues in Gen.Choose(-50, 50).ListOf()
                   .Where(list => list.Count is >= 2 and <= 3)
               from bValues in Gen.Choose(-50, 50).ListOf()
                   .Where(list => list.Count is >= 2 and <= 5)
               select new OrPatternSubsetScenario(aValues.ToImmutableArray(), bValues.ToImmutableArray());
    }

    public static Arbitrary<OrPatternSubsetScenario> ArbitraryOrPatternSubsetScenario()
    {
        return GenOrPatternSubsetScenario().ToArbitrary();
    }

    private static Gen<OrPatternTargetScenario> GenOrPatternTargetScenario()
    {
        return from eqValue in Gen.Choose(-50, 50)
               from setValues in Gen.Choose(-50, 50).ListOf()
                   .Where(list => list.Count is >= 2 and <= 5)
               select new OrPatternTargetScenario(eqValue, setValues.ToImmutableArray());
    }

    public static Arbitrary<OrPatternTargetScenario> ArbitraryOrPatternTargetScenario()
    {
        return GenOrPatternTargetScenario().ToArbitrary();
    }

    private static Gen<AnyPredicate> GenAnyPredicate()
    {
        var genRelational = from op in Gen.Elements(RelationalOperators)
                            from value in Gen.Choose(-50, 50)
                            select AnyPredicate.Relational(op, value);

        var genEquality = from value in Gen.Choose(-50, 50)
                          select AnyPredicate.Equality(value);

        var genNot = from value in Gen.Choose(-50, 50)
                     select AnyPredicate.Not(value);

        var genOr = from values in Gen.Choose(-50, 50).ListOf()
                        .Where(list => list.Count is >= 2 and <= 5)
                    select AnyPredicate.Or(values.ToImmutableArray());

        return Gen.OneOf(genRelational, genEquality, genNot, genOr);
    }

    private static Gen<SoundnessScenario> GenSoundnessScenario()
    {
        return from source in GenAnyPredicate()
               from target in GenAnyPredicate()
               select new SoundnessScenario(source, target);
    }

    public static Arbitrary<SoundnessScenario> ArbitrarySoundnessScenario()
    {
        return GenSoundnessScenario().ToArbitrary();
    }

    // ─── AST Construction Helpers ────────────────────────────────────────

    /// <summary>
    /// Builds a RefinementPredicate representing `== value` (a single equality constraint).
    /// </summary>
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

    /// <summary>
    /// Builds a RefinementPredicate representing `>= value and &lt;= value`
    /// (a compound predicate equivalent to equality).
    /// </summary>
    private static RefinementPredicate BuildBoundedEqualityPredicate(int value)
    {
        var gteExpr = MakeConstantExpression(value);
        var lteExpr = MakeConstantExpression(value);

        var gtePrimary = new GreaterThanOrEqualRelationalPatternPrimaryPatternNode { Constant = gteExpr };
        var ltePrimary = new LessThanOrEqualRelationalPatternPrimaryPatternNode { Constant = lteExpr };

        var gteUnary = new DirectPatternUnaryPatternNode { Primary = gtePrimary };
        var lteUnary = new DirectPatternUnaryPatternNode { Primary = ltePrimary };

        var andNode = new AndPatternNode { Left = gteUnary, Right = lteUnary };
        return new RefinementPredicate(andNode);
    }

    /// <summary>
    /// Builds a RefinementPredicate representing a single relational constraint.
    /// </summary>
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

    /// <summary>
    /// Builds a RefinementPredicate representing `not value` (a not/inequality constraint).
    /// </summary>
    private static RefinementPredicate BuildNotPredicate(int value)
    {
        var constantLiteral = new IntegerConstantConstantLiteralNode
        {
            Value = MistTypeGenerator.MakeToken(value.ToString()),
        };
        var constantPattern = new ConstantPatternPrimaryPatternNode { Literal = constantLiteral };
        var notUnary = new NotPatternUnaryPatternNode { Operand = constantPattern };
        var andNode = new AndPatternNode { Left = notUnary };
        return new RefinementPredicate(andNode);
    }

    /// <summary>
    /// Builds a RefinementPredicate representing an or-pattern from a set of integer values.
    /// AST shape: AndPatternNode → DirectPatternUnaryPatternNode → ParenthesizedPatternPrimaryPatternNode
    /// → PatternNode → OrPatternNode with multiple AndPatternNode alternatives,
    /// each containing a DirectPatternUnaryPatternNode → ConstantPatternPrimaryPatternNode.
    /// </summary>
    private static RefinementPredicate BuildOrPredicate(ImmutableArray<int> values)
    {
        // Build each alternative as an AndPatternNode with a single constant.
        AndPatternNode BuildConstantAlternative(int v)
        {
            var literal = new IntegerConstantConstantLiteralNode
            {
                Value = MistTypeGenerator.MakeToken(v.ToString()),
            };
            var constant = new ConstantPatternPrimaryPatternNode { Literal = literal };
            var direct = new DirectPatternUnaryPatternNode { Primary = constant };
            return new AndPatternNode { Left = direct };
        }

        // Build the OrPatternNode with Left, Right, and AndPatterns.
        var orNode = new OrPatternNode { Left = BuildConstantAlternative(values[0]) };

        if(values.Length >= 2)
        {
            orNode.Right = BuildConstantAlternative(values[1]);
        }

        if(values.Length > 2)
        {
            orNode.AndPatterns = values.Skip(2).Select(BuildConstantAlternative).ToImmutableArray();
        }

        // Wrap in PatternNode → ParenthesizedPatternPrimaryPatternNode → DirectPatternUnaryPatternNode → AndPatternNode
        var patternNode = new PatternNode { OrPattern = orNode };
        var paren = new ParenthesizedPatternPrimaryPatternNode { Inner = patternNode };
        var outerDirect = new DirectPatternUnaryPatternNode { Primary = paren };
        var rootAnd = new AndPatternNode { Left = outerDirect };

        return new RefinementPredicate(rootAnd);
    }

    private static VoidCoalescingNode MakeConstantExpression(int value)
    {
        return MistAstGenerator.WrapPrimary(
            new IntegerLiteralExpressionPrimaryNode
            {
                IntegerLiteral = MistTypeGenerator.MakeToken(value.ToString()),
            }).AsAssignment().Target;
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Equality-to-relational equivalence
    ///
    /// For any integer value `a` and any relational constraint target (operator, value),
    /// Implies(== a, target) SHALL return the same result as Implies(>= a and &lt;= a, target).
    ///
    /// This validates that the equality constraint extraction is semantically equivalent
    /// to the bounded relational representation.
    ///
    /// **Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool EqualityImpliesTarget_SameAs_BoundedRelationalImpliesTarget(EqualityRelationalScenario scenario)
    {
        var equalityPredicate = BuildEqualityPredicate(scenario.EqualityValue);
        var boundedPredicate = BuildBoundedEqualityPredicate(scenario.EqualityValue);
        var targetPredicate = BuildRelationalPredicate(scenario.Target.Operator, scenario.Target.Value);

        var equalityResult = equalityPredicate.Implies(targetPredicate);
        var boundedResult = boundedPredicate.Implies(targetPredicate);

        return equalityResult == boundedResult;
    }

    // ─── Property 2: Not-pattern identity and non-implication ─────────────

    /// <summary>
    /// Property 2: Not-pattern identity and non-implication
    ///
    /// For any integer value `a`:
    /// - `not a` implies `not a` (identity) → true
    /// - `not a` implies any relational target → false
    /// - `not a` implies `== b` → false (for any b)
    ///
    /// **Validates: Requirements 2.1, 2.3, 2.4, 8.1, 8.3, 8.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool NotPattern_Identity_And_NonImplication(NotPatternScenario scenario)
    {
        var notPredicate = BuildNotPredicate(scenario.NotValue);

        // Sub-property 1: not a implies not a (identity) → true
        var notIdentityTarget = BuildNotPredicate(scenario.NotValue);
        var identityResult = notPredicate.Implies(notIdentityTarget);
        if(!identityResult)
        {
            return false;
        }

        // Sub-property 2: not a implies any relational target → false
        var relationalTarget = BuildRelationalPredicate(scenario.RelationalTarget.Operator, scenario.RelationalTarget.Value);
        var relationalResult = notPredicate.Implies(relationalTarget);
        if(relationalResult)
        {
            return false;
        }

        // Sub-property 3: not a implies == b → false (for any b)
        var equalityTarget = BuildEqualityPredicate(scenario.EqualityTarget);
        var equalityResult = notPredicate.Implies(equalityTarget);
        if(equalityResult)
        {
            return false;
        }

        return true;
    }

    // ─── Property 5: Cross-operator relational implication ───────────────

    /// <summary>
    /// Property 5: Cross-operator relational implication
    ///
    /// For any integer values `a` and `b`:
    /// 1. Implies(>= a, > b) == (a > b)
    /// 2. Implies(&lt;= a, &lt; b) == (a &lt; b)
    /// 3. Implies(> a, > b) == (a >= b)
    /// 4. Implies(&lt; a, &lt; b) == (a &lt;= b)
    ///
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool CrossOperatorRelationalImplication_HoldsWithCorrectBoundaryConditions(
        CrossOperatorScenario scenario)
    {
        var a = scenario.A;
        var b = scenario.B;

        // Rule 1: >= a implies > b iff a > b
        var gteA = BuildRelationalPredicate(">=", a);
        var gtB = BuildRelationalPredicate(">", b);
        var rule1 = gteA.Implies(gtB) == (a > b);

        // Rule 2: <= a implies < b iff a < b
        var lteA = BuildRelationalPredicate("<=", a);
        var ltB = BuildRelationalPredicate("<", b);
        var rule2 = lteA.Implies(ltB) == (a < b);

        // Rule 3: > a implies > b iff a >= b
        var gtA = BuildRelationalPredicate(">", a);
        var gtB2 = BuildRelationalPredicate(">", b);
        var rule3 = gtA.Implies(gtB2) == (a >= b);

        // Rule 4: < a implies < b iff a <= b
        var ltA = BuildRelationalPredicate("<", a);
        var ltB2 = BuildRelationalPredicate("<", b);
        var rule4 = ltA.Implies(ltB2) == (a <= b);

        return rule1 && rule2 && rule3 && rule4;
    }

    // ─── Property 3: Equality implies not-pattern ────────────────────────

    /// <summary>
    /// Property 3: Equality implies not-pattern
    ///
    /// For any integer values `a` and `b` where `a != b`: `Implies(== a, not b)` returns true.
    /// When `a == b`: `Implies(== a, not b)` returns false.
    ///
    /// **Validates: Requirements 2.2, 8.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool EqualityImpliesNotPattern_TrueWhenValuesDistinct(int a, int b)
    {
        var equalityPredicate = BuildEqualityPredicate(a);
        var notPredicate = BuildNotPredicate(b);

        var result = equalityPredicate.Implies(notPredicate);

        return result == (a != b);
    }

    // ─── Property 4: Relational-to-not implication ───────────────────────

    /// <summary>
    /// Property 4: Relational-to-not implication
    ///
    /// For any integer value `a`:
    /// - `Implies(&gt; a, not a)` SHALL return true (strict greater excludes a)
    /// - `Implies(&lt; a, not a)` SHALL return true (strict less excludes a)
    /// - `Implies(&gt;= a, not a)` SHALL return false (range includes a)
    /// - `Implies(&lt;= a, not a)` SHALL return false (range includes a)
    ///
    /// **Validates: Requirements 2.5, 8.5, 8.6, 8.7, 8.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RelationalToNotImplication_StrictExcludesValue_NonStrictDoesNot(int a)
    {
        var notPredicate = BuildNotPredicate(a);

        var gtImpliesNot = BuildRelationalPredicate(">", a).Implies(notPredicate);
        var ltImpliesNot = BuildRelationalPredicate("<", a).Implies(notPredicate);
        var gteImpliesNot = BuildRelationalPredicate(">=", a).Implies(notPredicate);
        var lteImpliesNot = BuildRelationalPredicate("<=", a).Implies(notPredicate);

        return gtImpliesNot == true
            && ltImpliesNot == true
            && gteImpliesNot == false
            && lteImpliesNot == false;
    }

    // ─── Property 6: Or-pattern source requires all alternatives to imply target ─

    /// <summary>
    /// Property 6: Or-pattern source requires all alternatives to imply target
    ///
    /// For any set of integer values forming an or-pattern source and any relational target,
    /// implication holds iff every value satisfies the target.
    ///
    /// **Validates: Requirements 7.4, 7.6**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool OrPatternSource_ImpliesRelationalTarget_IffAllValuesSatisfyTarget(
        OrPatternSourceScenario scenario)
    {
        var orPredicate = BuildOrPredicate(scenario.OrValues);
        var relationalTarget = BuildRelationalPredicate(scenario.Target.Operator, scenario.Target.Value);

        var impliesResult = orPredicate.Implies(relationalTarget);

        // Compute the expected result: every value in the or-pattern must satisfy the relational constraint.
        var allSatisfy = scenario.OrValues.All(v => BruteForceRelationalCheck(v, scenario.Target.Operator, scenario.Target.Value));

        return impliesResult == allSatisfy;
    }

    /// <summary>
    /// Evaluates whether a single integer value satisfies a relational constraint.
    /// </summary>
    private static bool BruteForceRelationalCheck(int value, string op, int target)
    {
        return op switch
        {
            ">=" => value >= target,
            "<=" => value <= target,
            ">" => value > target,
            "<" => value < target,
            _ => throw new ArgumentException($"Unknown operator: {op}"),
        };
    }

    // ─── Property 7: Or-pattern target membership ────────────────────────

    /// <summary>
    /// Property 7: Or-pattern target membership
    ///
    /// For any integer value `a` and any set of values `S`,
    /// `Implies(== a, or(S))` is true iff `a ∈ S`.
    ///
    /// **Validates: Requirements 7.1, 7.5**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool OrPatternTarget_EqualityImpliesOrPattern_IffValueInSet(
        OrPatternTargetScenario scenario)
    {
        var equalityPredicate = BuildEqualityPredicate(scenario.EqualityValue);
        var orPredicate = BuildOrPredicate(scenario.SetValues);

        var impliesResult = equalityPredicate.Implies(orPredicate);

        // Expected: implication holds iff the equality value is a member of the or-pattern set.
        var expected = scenario.SetValues.Contains(scenario.EqualityValue);

        return impliesResult == expected;
    }

    // ─── Property 8: Or-pattern subset implication ───────────────────────

    /// <summary>
    /// Property 8: Or-pattern subset implication
    ///
    /// For any two sets of integer values `A` and `B` represented as or-patterns,
    /// `Implies(A, B)` SHALL return true if and only if `A ⊆ B` (every element of A is also in B).
    ///
    /// **Validates: Requirements 7.2, 7.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool OrPatternSubsetImplication_TrueIffAIsSubsetOfB(OrPatternSubsetScenario scenario)
    {
        var orA = BuildOrPredicate(scenario.A);
        var orB = BuildOrPredicate(scenario.B);

        var impliesResult = orA.Implies(orB);

        // Expected: A implies B iff every element of A is contained in B.
        var aIsSubsetOfB = scenario.A.All(scenario.B.Contains);

        return impliesResult == aIsSubsetOfB;
    }

    // ─── Property 9: Soundness — Implies true means brute-force confirms ─

    /// <summary>
    /// Property 9: Soundness — Implies true means brute-force confirms
    ///
    /// For any pair of predicates from expanded constraint types, when
    /// Implies(source, target) returns true, then for every integer v in [-100, 100],
    /// if source.Evaluate(v) is true then target.Evaluate(v) must also be true.
    ///
    /// This is a one-directional soundness check: we only verify that Implies returning
    /// true is correct. There may be cases where brute-force would confirm but Implies
    /// returns false — that's acceptable (incompleteness is allowed).
    ///
    /// **Validates: Requirements 1.1–1.7, 2.1–2.5, 3.1–3.4, 7.1–7.7, 8.1–8.8**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(RefinementComparisonEnginePropertyTests) })]
    public bool Soundness_ImpliesTrueMeansBruteForceConfirms(SoundnessScenario scenario)
    {
        var sourcePredicate = BuildPredicateFromAny(scenario.Source);
        var targetPredicate = BuildPredicateFromAny(scenario.Target);

        var impliesResult = sourcePredicate.Implies(targetPredicate);

        // Only check soundness when Implies returns true.
        if(!impliesResult)
        {
            return true;
        }

        // Brute-force: for every v in [-100, 100], source(v) → target(v).
        return BruteForceImplies(sourcePredicate, targetPredicate, -100, 100);
    }

    /// <summary>
    /// Brute-force implication check: for every integer v in [lo, hi],
    /// if source.Evaluate(v) is true then target.Evaluate(v) must also be true.
    /// </summary>
    private static bool BruteForceImplies(RefinementPredicate source, RefinementPredicate target, int lo, int hi)
    {
        for(var v = lo; v <= hi; v++)
        {
            if(source.Evaluate(v) && !target.Evaluate(v))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Builds a RefinementPredicate from an AnyPredicate scenario value.
    /// </summary>
    private static RefinementPredicate BuildPredicateFromAny(AnyPredicate predicate)
    {
        return predicate.Kind switch
        {
            AnyPredicate.PredicateKind.Relational => BuildRelationalPredicate(predicate.Operator!, predicate.Value),
            AnyPredicate.PredicateKind.Equality => BuildEqualityPredicate(predicate.Value),
            AnyPredicate.PredicateKind.Not => BuildNotPredicate(predicate.Value),
            AnyPredicate.PredicateKind.Or => BuildOrPredicate(predicate.OrValues),
            _ => throw new ArgumentException($"Unknown predicate kind: {predicate.Kind}"),
        };
    }
}