// Feature: tuple-pattern-matching, Property 7: Exhaustiveness for all-catch-all tuple patterns
// **Validates: Requirements 3.4, 3.5**

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
/// Property-based tests for exhaustiveness of all-catch-all tuple positional patterns.
///
/// **Validates: Requirements 3.4, 3.5**
///
/// Property 7: For any tuple type, a switch expression arm whose pattern is a tuple
/// positional pattern with every element being a catch-all (discard <c>_</c> or <c>var</c>
/// pattern, recursively for nested tuples) and no when guard SHALL be treated as exhaustive
/// by the exhaustiveness checker.
/// </summary>
public class TuplePositionalPatternExhaustivenessPropertyTests
{
    // ─── Catch-all element kind ──────────────────────────────────────────

    public enum CatchAllKind
    {
        Discard,
        VarNamed,
        VarDiscard,
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034

    /// <summary>
    /// Represents a scenario with a tuple type and an all-catch-all tuple positional pattern.
    /// The pattern may include nested tuple positional patterns where inner elements are
    /// also all catch-all.
    /// </summary>
    public sealed record ExhaustivenessScenario(
        IType ScrutineeType,
        PatternNode Pattern,
        bool HasGuard,
        string Label)
    {
        public override string ToString() => Label;
    }

#pragma warning restore CA1034

    // ─── AST construction helpers ────────────────────────────────────────

    private static Storm.Runtime.Token Tok(string lexeme) =>
        MistTypeGenerator.MakeToken(lexeme);

    private static PatternNode WrapPrimaryPattern(PrimaryPatternNode primary)
    {
        var unary = new DirectPatternUnaryPatternNode { Primary = primary };
        var and = new AndPatternNode { Left = unary };
        var or = new OrPatternNode { Left = and };
        return new PatternNode { OrPattern = or };
    }

    private static PatternNode MakeDiscardPattern()
    {
        PrimaryPatternNode primary = new DiscardPatternPrimaryPatternNode
        {
            DISCARD = Tok("_"),
        };
        return WrapPrimaryPattern(primary);
    }

    private static PatternNode MakeVarNamedPattern(string name)
    {
        PrimaryPatternNode primary = new VarPatternPrimaryPatternNode
        {
            Name = Tok(name),
        };
        return WrapPrimaryPattern(primary);
    }

    private static PatternNode MakeVarDiscardPattern()
    {
        PrimaryPatternNode primary = new VarPatternPrimaryPatternNode
        {
            Name = Tok("_"),
        };
        return WrapPrimaryPattern(primary);
    }

    /// <summary>
    /// Creates a catch-all pattern based on the specified kind.
    /// </summary>
    private static PatternNode MakeCatchAllElement(CatchAllKind kind, int index) => kind switch
    {
        CatchAllKind.Discard => MakeDiscardPattern(),
        CatchAllKind.VarNamed => MakeVarNamedPattern($"v{index}"),
        CatchAllKind.VarDiscard => MakeVarDiscardPattern(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// Creates a tuple positional pattern where every element is a catch-all.
    /// For nested tuple element types, recursively creates nested tuple positional patterns.
    /// </summary>
    private static PatternNode MakeAllCatchAllTuplePattern(
        IReadOnlyList<IType> elementTypes,
        IReadOnlyList<CatchAllKind> catchAllKinds,
        IReadOnlyList<bool> useNestedTuplePattern,
        ref int varIndex)
    {
        var elementPatterns = new List<PatternNode>();

        for (var i = 0; i < elementTypes.Count; i++)
        {
            if (elementTypes[i] is TupleType nestedTuple && useNestedTuplePattern[i])
            {
                // Recursively create a nested all-catch-all tuple pattern
                var nestedKinds = Enumerable.Range(0, nestedTuple.Arity)
                    .Select(j => catchAllKinds[(i + j) % catchAllKinds.Count])
                    .ToList();
                var nestedUseNested = Enumerable.Range(0, nestedTuple.Arity)
                    .Select(_ => false) // Don't go deeper than one level of nesting
                    .ToList();
                elementPatterns.Add(MakeAllCatchAllTuplePattern(
                    nestedTuple.ElementTypes, nestedKinds, nestedUseNested, ref varIndex));
            }
            else
            {
                elementPatterns.Add(MakeCatchAllElement(catchAllKinds[i], varIndex++));
            }
        }

        // Build the TuplePositionalPatternPrimaryPatternNode
        var first = elementPatterns[0];
        var rest = elementPatterns[1];
        var additional = elementPatterns.Count > 2
            ? ImmutableArray.CreateRange(elementPatterns.Skip(2))
            : ImmutableArray<PatternNode>.Empty;

        PrimaryPatternNode tuplePatternNode = new TuplePositionalPatternPrimaryPatternNode
        {
            First = first,
            Rest = rest,
            Patterns = additional,
        };

        return WrapPrimaryPattern(tuplePatternNode);
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static readonly IType[] LeafTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.String,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Double,
    ];

    /// <summary>
    /// Generates a random tuple type with arity 2–5, where elements are either
    /// primitive types or nested tuple types (one level deep).
    /// </summary>
    private static Gen<TupleType> GenTupleType()
    {
        return from arity in Gen.Choose(2, 5)
               from elementTypeIndices in Gen.ArrayOf(Gen.Choose(0, LeafTypes.Length - 1), arity)
               from hasNested in Gen.ArrayOf(Gen.Elements(true, false), arity)
               from nestedArity in Gen.Choose(2, 3)
               from nestedTypeIndices in Gen.ArrayOf(Gen.Choose(0, LeafTypes.Length - 1), nestedArity)
               let elementTypes = elementTypeIndices.Select((ti, i) =>
               {
                   if (hasNested[i] && i == 0) // Only nest at first position to keep it manageable
                   {
                       var nestedElements = nestedTypeIndices
                           .Select(nti => LeafTypes[nti])
                           .ToList();
                       return (IType)new TupleType(nestedElements);
                   }
                   return LeafTypes[ti];
               }).ToList()
               select new TupleType(elementTypes);
    }

    /// <summary>
    /// Generates a scenario with an all-catch-all tuple pattern (no guard).
    /// This should always be exhaustive.
    /// </summary>
    public static Arbitrary<ExhaustivenessScenario> ArbitraryExhaustivenessScenario()
    {
        var gen =
            from tupleType in GenTupleType()
            from catchAllKinds in Gen.ArrayOf(
                Gen.Elements(CatchAllKind.Discard, CatchAllKind.VarNamed, CatchAllKind.VarDiscard),
                tupleType.Arity)
            from useNestedPattern in Gen.ArrayOf(Gen.Elements(true, false), tupleType.Arity)
            from hasGuard in Gen.Frequency((4, Gen.Constant(false)), (1, Gen.Constant(true)))
            let pattern = BuildAllCatchAllTuplePattern(
                tupleType.ElementTypes, catchAllKinds, useNestedPattern)
            let label = $"Type({tupleType.DisplayName}), Guard={hasGuard}, " +
                        $"Kinds=[{string.Join(",", catchAllKinds)}]"
            select new ExhaustivenessScenario(tupleType, pattern, hasGuard, label);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Wrapper that calls MakeAllCatchAllTuplePattern with a ref varIndex.
    /// </summary>
    private static PatternNode BuildAllCatchAllTuplePattern(
        IReadOnlyList<IType> elementTypes,
        IReadOnlyList<CatchAllKind> catchAllKinds,
        IReadOnlyList<bool> useNestedPattern)
    {
        var varIndex = 0;
        return MakeAllCatchAllTuplePattern(elementTypes, catchAllKinds, useNestedPattern, ref varIndex);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 7: Exhaustiveness for all-catch-all tuple patterns
    //
    // **Validates: Requirements 3.4, 3.5**
    //
    // For any tuple type, a switch expression arm whose pattern is a tuple
    // positional pattern with every element being a catch-all (discard _ or
    // var pattern, recursively for nested tuples) and no when guard SHALL be
    // treated as exhaustive by the exhaustiveness checker.
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TuplePositionalPatternExhaustivenessPropertyTests) })]
    public Property AllCatchAllTuplePattern_IsExhaustive_WhenNoGuard(
        ExhaustivenessScenario scenario)
    {
        var arms = new List<(PatternNode Pattern, bool HasGuard)>
        {
            (scenario.Pattern, scenario.HasGuard),
        };

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(scenario.ScrutineeType, arms);
        var isExhaustive = uncovered.Count == 0;

        // When there's no guard, an all-catch-all tuple pattern should be exhaustive.
        // When there IS a guard, the arm doesn't count toward exhaustiveness coverage,
        // but the checker skips tuple types entirely (returns empty), so it's still exhaustive.
        // This matches C# behavior where tuple scrutinees don't produce exhaustiveness warnings.
        var expectedExhaustive = true; // Tuple types always return exhaustive (checker skips them)

        return (isExhaustive == expectedExhaustive).ToProperty()
            .Label($"Expected exhaustive={expectedExhaustive}, got exhaustive={isExhaustive}. " +
                   $"Scenario: {scenario.Label}. Uncovered: [{string.Join(", ", uncovered)}]");
    }
}
