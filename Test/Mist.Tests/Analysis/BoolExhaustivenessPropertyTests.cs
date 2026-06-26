// Feature: switch-expressions, Property 2: Bool Exhaustiveness
// **Validates: Requirements 6.1, 6.2, 6.5, 6.7, 6.8, 15.1, 15.2, 15.3**

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
/// Property-based tests for bool exhaustiveness checking.
///
/// **Validates: Requirements 6.1, 6.2, 6.5, 6.7, 6.8, 15.1, 15.2, 15.3**
///
/// For any switch expression with a <c>bool</c> scrutinee and any combination of
/// <c>true</c>/<c>false</c> constant pattern arms and catch-all arms (with or without guards),
/// the exhaustiveness checker SHALL report the switch as exhaustive if and only if:
/// (a) both <c>true</c> and <c>false</c> are covered by unguarded arms, OR
/// (b) an unguarded catch-all arm (<c>_</c> or <c>var</c>) is present.
/// </summary>
public class BoolExhaustivenessPropertyTests
{
    // ─── Arm classification ──────────────────────────────────────────────

    public enum ArmKind
    {
        TrueConstant,
        FalseConstant,
        CatchAll,
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public sealed record BoolExhaustivenessScenario(
        IReadOnlyList<(ArmKind Kind, bool HasGuard)> Arms,
        string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<(ArmKind Kind, bool HasGuard)> GenArm()
    {
        return from kind in Gen.Elements(ArmKind.TrueConstant, ArmKind.FalseConstant, ArmKind.CatchAll)
               from hasGuard in Gen.Elements(true, false)
               select (kind, hasGuard);
    }

    public static Arbitrary<BoolExhaustivenessScenario> ArbitraryBoolExhaustivenessScenario()
    {
        var gen = from armCount in Gen.Choose(0, 6)
                  from arms in Gen.ArrayOf(GenArm(), armCount)
                  let label = string.Join(", ", arms.Select(a =>
                      $"{a.Kind}{(a.HasGuard ? "(guarded)" : "")}"))
                  select new BoolExhaustivenessScenario(arms, label);
        return gen.ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    private static PatternNode MakeBoolConstantPattern(bool value)
    {
        PrimaryPatternNode primary = new ConstantPatternPrimaryPatternNode
        {
            Literal = value
                ? (ConstantLiteralNode)new TrueConstantConstantLiteralNode()
                : new FalseConstantConstantLiteralNode(),
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

    private static PatternNode MakePatternForKind(ArmKind kind)
    {
        return kind switch
        {
            ArmKind.TrueConstant => MakeBoolConstantPattern(true),
            ArmKind.FalseConstant => MakeBoolConstantPattern(false),
            ArmKind.CatchAll => MakeCatchAllPattern(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    // ─── Oracle: expected exhaustiveness ─────────────────────────────────

    private static bool ShouldBeExhaustive(IReadOnlyList<(ArmKind Kind, bool HasGuard)> arms)
    {
        var hasUnguardedCatchAll = arms.Any(a => a.Kind == ArmKind.CatchAll && !a.HasGuard);
        if(hasUnguardedCatchAll)
        {
            return true;
        }

        var hasUnguardedTrue = arms.Any(a => a.Kind == ArmKind.TrueConstant && !a.HasGuard);
        var hasUnguardedFalse = arms.Any(a => a.Kind == ArmKind.FalseConstant && !a.HasGuard);
        return hasUnguardedTrue && hasUnguardedFalse;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Bool Exhaustiveness
    //
    // **Validates: Requirements 6.1, 6.2, 6.5, 6.7, 6.8, 15.1, 15.2, 15.3**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(BoolExhaustivenessPropertyTests) })]
    public Property BoolSwitch_IsExhaustive_IffBothValuesOrCatchAllCovered(
        BoolExhaustivenessScenario scenario)
    {
        // Build the arm list for the ExhaustivenessChecker
        var arms = scenario.Arms
            .Select(a => (MakePatternForKind(a.Kind), a.HasGuard))
            .ToList();

        IType boolType = LegacyPrimitiveTypes.Bool;

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(boolType, arms);
        var isExhaustive = uncovered.Count == 0;
        var expectedExhaustive = ShouldBeExhaustive(scenario.Arms);

        return (isExhaustive == expectedExhaustive).ToProperty()
            .Label($"Expected exhaustive={expectedExhaustive}, got exhaustive={isExhaustive}. " +
                   $"Arms: [{scenario.Label}]. Uncovered: [{string.Join(", ", uncovered)}]");
    }
}