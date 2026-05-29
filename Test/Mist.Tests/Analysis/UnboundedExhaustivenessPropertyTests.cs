// Feature: switch-expressions, Property 4: Unbounded Type Exhaustiveness
// **Validates: Requirements 6.1, 6.4, 6.5, 6.7, 6.8**

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for unbounded type exhaustiveness checking.
///
/// **Validates: Requirements 6.1, 6.4, 6.5, 6.7, 6.8**
///
/// For any switch expression with a numeric or string scrutinee and any combination of
/// constant/relational pattern arms (with or without guards), the exhaustiveness checker
/// SHALL report the switch as exhaustive if and only if an unguarded catch-all arm
/// (<c>_</c> or <c>var</c>) is present.
/// </summary>
public class UnboundedExhaustivenessPropertyTests
{
    // ─── Unbounded types ─────────────────────────────────────────────────

    private static readonly IType[] UnboundedTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.String,
        LegacyPrimitiveTypes.Short,
        LegacyPrimitiveTypes.Byte,
    ];

    // ─── Arm classification ──────────────────────────────────────────────

    public enum ArmKind
    {
        Constant,
        Relational,
        CatchAll,
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public sealed record UnboundedExhaustivenessScenario(
        IType ScrutineeType,
        IReadOnlyList<(ArmKind Kind, bool HasGuard)> Arms,
        string Label)
    {
        public override string ToString() => Label;
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<(ArmKind Kind, bool HasGuard)> GenArm() =>
        from kind in Gen.Frequency(
            (3, Gen.Constant(ArmKind.Constant)),
            (2, Gen.Constant(ArmKind.Relational)),
            (1, Gen.Constant(ArmKind.CatchAll)))
        from hasGuard in Gen.Elements(true, false)
        select (kind, hasGuard);

    public static Arbitrary<UnboundedExhaustivenessScenario> ArbitraryUnboundedExhaustivenessScenario()
    {
        var gen = from typeIdx in Gen.Choose(0, UnboundedTypes.Length - 1)
                  from armCount in Gen.Choose(0, 8)
                  from arms in Gen.ArrayOf(GenArm(), armCount)
                  let scrutType = UnboundedTypes[typeIdx]
                  let label = $"Type({scrutType.DisplayName}), Arms: [{string.Join(", ", arms.Select(a =>
                      $"{a.Kind}{(a.HasGuard ? "(guarded)" : "")}"))}]"
                  select new UnboundedExhaustivenessScenario(scrutType, arms, label);
        return gen.ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static PatternNode MakeConstantPattern()
    {
        // Create a constant pattern with an integer literal
        PrimaryPatternNode primary = new ConstantPatternPrimaryPatternNode
        {
            Literal = new IntegerConstantConstantLiteralNode
            {
                Value = Tok("42"),
            },
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeRelationalPattern()
    {
        // Create a relational pattern (> 0) using GreaterThanRelationalPatternPrimaryPatternNode
        var intLitPrimary = new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = Tok("0") };
        var postfix = new PostfixNode
        {
            Target = intLitPrimary,
            Suffix = ImmutableArray<PostfixSuffixNode>.Empty,
        };
        var unaryExpr = new PostfixExpressionUnaryNode { Primary = postfix };
        var mult = new MultiplicativeNode { Left = unaryExpr };
        var add = new AdditiveNode { Left = mult };
        var rel = new RelationalComparisonRelationalNode { Left = add };
        var eq = new EqualityNode { Left = rel };
        var logAnd = new LogicalAndNode { Left = eq };
        var logOr = new LogicalOrNode { Left = logAnd };
        var assign = new AssignmentNode { Target = new VoidCoalescingNode { Left = logOr } };
        var constExpr = new ExpressionNode { Assignment = assign };

        PrimaryPatternNode primary = new GreaterThanRelationalPatternPrimaryPatternNode
        {
            Constant = constExpr,
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeCatchAllPattern()
    {
        PrimaryPatternNode primary = new VarPatternPrimaryPatternNode
        {
            Name = Tok("_"),
        };
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakePatternForKind(ArmKind kind) => kind switch
    {
        ArmKind.Constant => MakeConstantPattern(),
        ArmKind.Relational => MakeRelationalPattern(),
        ArmKind.CatchAll => MakeCatchAllPattern(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    // ─── Oracle: expected exhaustiveness ─────────────────────────────────

    private static bool ShouldBeExhaustive(IReadOnlyList<(ArmKind Kind, bool HasGuard)> arms)
    {
        // For unbounded types, only an unguarded catch-all makes it exhaustive
        return arms.Any(a => a.Kind == ArmKind.CatchAll && !a.HasGuard);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 4: Unbounded Type Exhaustiveness
    //
    // **Validates: Requirements 6.1, 6.4, 6.5, 6.7, 6.8**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(UnboundedExhaustivenessPropertyTests) })]
    public Property UnboundedSwitch_IsExhaustive_IffUnguardedCatchAllPresent(
        UnboundedExhaustivenessScenario scenario)
    {
        // Build the arm list for the ExhaustivenessChecker
        var arms = scenario.Arms
            .Select(a => (MakePatternForKind(a.Kind), a.HasGuard))
            .ToList();

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(scenario.ScrutineeType, arms);
        var isExhaustive = uncovered.Count == 0;
        var expectedExhaustive = ShouldBeExhaustive(scenario.Arms);

        return (isExhaustive == expectedExhaustive).ToProperty()
            .Label($"Expected exhaustive={expectedExhaustive}, got exhaustive={isExhaustive}. " +
                   $"Scenario: {scenario.Label}. Uncovered: [{string.Join(", ", uncovered)}]");
    }
}
