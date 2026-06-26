// Feature: switch-expressions, Property 3: Type Union Exhaustiveness
// **Validates: Requirements 6.1, 6.3, 6.5, 6.7, 6.8, 16.1, 16.2, 16.3, 16.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for type union exhaustiveness checking.
///
/// **Validates: Requirements 6.1, 6.3, 6.5, 6.7, 6.8, 16.1, 16.2, 16.3, 16.4**
///
/// For any type union scrutinee (including nested unions) and any combination of type pattern
/// arms and catch-all arms (with or without guards), the exhaustiveness checker SHALL report
/// the switch as exhaustive if and only if: (a) every leaf constituent type of the flattened
/// union is covered by an unguarded type pattern arm, OR (b) an unguarded catch-all arm is present.
/// </summary>
public class TypeUnionExhaustivenessPropertyTests
{
    // ─── Type pool for union construction ────────────────────────────────
    // The ExhaustivenessChecker.ExtractTypeDisplayName maps primitive keywords
    // to their CLR names (int → Int32, bool → Boolean, etc.). The union's
    // constituent types must use the same display names for matching to work.

    private static readonly (IType Type, string Keyword)[] TypePool =
    [
        (new StructType("Int32", new Scope(null), containingNamespace: null), "int"),
        (new StructType("Boolean", new Scope(null), containingNamespace: null), "bool"),
        (new ClassType("String", new Scope(null), containingNamespace: null), "string"),
        (new StructType("Single", new Scope(null), containingNamespace: null), "float"),
        (new StructType("Double", new Scope(null), containingNamespace: null), "double"),
    ];

    // ─── Arm classification ──────────────────────────────────────────────

    public enum ArmKind
    {
        TypePattern,
        CatchAll,
    }

    // ─── Scenario record ─────────────────────────────────────────────────

#pragma warning disable CA1034
    public sealed record UnionExhaustivenessScenario(
        UnionType ScrutineeType,
        IReadOnlyList<(ArmKind Kind, int TypeIndex, bool HasGuard)> Arms,
        string Label)
    {
        public override string ToString()
        {
            return Label;
        }
    }
#pragma warning restore CA1034

    // ─── Generator ───────────────────────────────────────────────────────

    private static Gen<UnionType> GenUnionType()
    {
        // Generate a flat union of 2-4 distinct types
        var flatUnion = from count in Gen.Choose(2, 4)
                        from indices in Gen.Shuffle(Enumerable.Range(0, TypePool.Length).ToArray())
                            .Select(arr => arr.Take(count).ToArray())
                        let types = indices.Select(i => TypePool[i].Type).ToArray()
                        select new UnionType(types);

        // Generate a nested union: (T1 | T2) | T3
        var nestedUnion = from innerCount in Gen.Choose(2, 3)
                          from outerCount in Gen.Choose(1, 2)
                          from indices in Gen.Shuffle(Enumerable.Range(0, TypePool.Length).ToArray())
                              .Select(arr => arr.Take(innerCount + outerCount).ToArray())
                          let innerTypes = indices.Take(innerCount).Select(i => TypePool[i].Type).ToArray()
                          let outerTypes = indices.Skip(innerCount).Select(i => TypePool[i].Type).ToArray()
                          let inner = new UnionType(innerTypes)
                          let allConstituents = new IType[] { inner }.Concat(outerTypes).ToArray()
                          select new UnionType(allConstituents);

        return Gen.Frequency((3, flatUnion), (1, nestedUnion));
    }

    private static Gen<(ArmKind Kind, int TypeIndex, bool HasGuard)> GenArm(int typePoolSize)
    {
        return from kind in Gen.Frequency((3, Gen.Constant(ArmKind.TypePattern)), (1, Gen.Constant(ArmKind.CatchAll)))
               from typeIdx in Gen.Choose(0, typePoolSize - 1)
               from hasGuard in Gen.Elements(true, false)
               select (kind, typeIdx, hasGuard);
    }

    private static Storm.Runtime.Token Tok(string lexeme)
    {
        return MistTypeGenerator.MakeToken(lexeme);
    }

    public static Arbitrary<UnionExhaustivenessScenario> ArbitraryUnionExhaustivenessScenario()
    {
        var gen = from union in GenUnionType()
                  from armCount in Gen.Choose(0, 8)
                  from arms in Gen.ArrayOf(GenArm(TypePool.Length), armCount)
                  let label = $"Union({union.DisplayName}), Arms: [{string.Join(", ", arms.Select(a =>
                      a.Kind == ArmKind.CatchAll
                          ? $"CatchAll{(a.HasGuard ? "(guarded)" : "")}"
                          : $"{TypePool[a.TypeIndex].Keyword}{(a.HasGuard ? "(guarded)" : "")}"))}]"
                  select new UnionExhaustivenessScenario(union, arms, label);
        return gen.ToArbitrary();
    }

    // ─── AST construction helpers ────────────────────────────────────────

    private static PrimitiveTypeNode MakePrimitiveTypeNode(string keyword)
    {
        return keyword switch
        {
            "int" => new IntTypePrimitiveTypeNode(),
            "bool" => new BoolTypePrimitiveTypeNode(),
            "string" => new StringTypePrimitiveTypeNode(),
            "float" => new FloatTypePrimitiveTypeNode(),
            "double" => new DoubleTypePrimitiveTypeNode(),
            _ => new IntTypePrimitiveTypeNode(),
        };
    }

    private static PatternNode MakeTypePattern(int typeIndex)
    {
        var keyword = TypePool[typeIndex].Keyword;
        PrimaryPatternNode primary = new TypePatternPrimaryPatternNode
        {
            Type = new TypeReferenceNode
            {
                First = new PrimitiveSingleTypeRefSingleTypeRefNode
                {
                    PrimitiveType = MakePrimitiveTypeNode(keyword),
                }
            },
            Name = Tok("x"),
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

    // ─── Oracle: expected exhaustiveness ─────────────────────────────────

    /// <summary>
    /// Flattens a union type to its leaf constituent types (non-union types).
    /// </summary>
    private static List<IType> FlattenUnion(UnionType union)
    {
        var leaves = new List<IType>();
        foreach(var constituent in union.Constituents)
        {
            if(constituent is UnionType nested)
            {
                leaves.AddRange(FlattenUnion(nested));
            }
            else
            {
                leaves.Add(constituent);
            }
        }

        return leaves;
    }

    private static bool ShouldBeExhaustive(
        UnionType scrutineeType,
        IReadOnlyList<(ArmKind Kind, int TypeIndex, bool HasGuard)> arms)
    {
        // An unguarded catch-all makes it exhaustive
        if(arms.Any(a => a.Kind == ArmKind.CatchAll && !a.HasGuard))
        {
            return true;
        }

        // All leaf types must be covered by unguarded type patterns
        var leafTypes = FlattenUnion(scrutineeType);
        var coveredDisplayNames = new HashSet<string>(StringComparer.Ordinal);

        foreach(var arm in arms)
        {
            if(arm.HasGuard || arm.Kind != ArmKind.TypePattern)
            {
                continue;
            }

            var typeName = TypePool[arm.TypeIndex].Type.DisplayName;
            _ = coveredDisplayNames.Add(typeName);
        }

        return leafTypes.All(t => coveredDisplayNames.Contains(t.DisplayName));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 3: Type Union Exhaustiveness
    //
    // **Validates: Requirements 6.1, 6.3, 6.5, 6.7, 6.8, 16.1, 16.2, 16.3, 16.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(TypeUnionExhaustivenessPropertyTests) })]
    public Property UnionSwitch_IsExhaustive_IffAllLeafTypesCoveredOrCatchAll(
        UnionExhaustivenessScenario scenario)
    {
        // Build the arm list for the ExhaustivenessChecker
        var arms = scenario.Arms
            .Select(a => (
                a.Kind == ArmKind.CatchAll ? MakeCatchAllPattern() : MakeTypePattern(a.TypeIndex),
                a.HasGuard))
            .ToList();

        var uncovered = ExhaustivenessChecker.CheckExhaustiveness(scenario.ScrutineeType, arms);
        var isExhaustive = uncovered.Count == 0;
        var expectedExhaustive = ShouldBeExhaustive(scenario.ScrutineeType, scenario.Arms);

        return (isExhaustive == expectedExhaustive).ToProperty()
            .Label($"Expected exhaustive={expectedExhaustive}, got exhaustive={isExhaustive}. " +
                   $"Scenario: {scenario.Label}. Uncovered: [{string.Join(", ", uncovered)}]");
    }
}