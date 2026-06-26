using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Verifies that a switch expression's arms collectively cover all possible
/// values of the scrutinee's static type.
/// </summary>
internal static class ExhaustivenessChecker
{
    /// <summary>
    /// Checks exhaustiveness and returns a list of uncovered cases (empty if exhaustive).
    /// </summary>
    /// <param name="scrutineeType">The static type of the switch expression's scrutinee.</param>
    /// <param name="arms">
    /// The list of switch arms, each represented as a pattern node and a flag
    /// indicating whether the arm has a <c>when</c> guard. Arms with guards
    /// never count toward exhaustiveness coverage.
    /// </param>
    /// <returns>
    /// An empty list if the switch is exhaustive; otherwise a list of strings
    /// describing the uncovered cases.
    /// </returns>
    public static IReadOnlyList<string> CheckExhaustiveness(
        IType scrutineeType,
        IReadOnlyList<(PatternNode Pattern, bool HasGuard)> arms)
    {
        // Rule 1: Any arm that is a catch-all without a guard makes the switch exhaustive.
        if(HasUnguardedCatchAll(arms))
        {
            return Array.Empty<string>();
        }

        // Rule 2: Bool scrutinee — track true/false coverage.
        if(IsBoolType(scrutineeType))
        {
            return CheckBoolExhaustiveness(arms);
        }

        // Rule 3: Type union scrutinee — flatten and track constituent type coverage.
        if(scrutineeType is UnionType unionType)
        {
            return CheckUnionExhaustiveness(unionType, arms);
        }

        // Rule 3b: Tuple type scrutinee — the exhaustiveness checker cannot
        // meaningfully analyze relational/constant patterns for coverage across
        // tuple elements. When tuple positional pattern arms are present, skip
        // the exhaustiveness check (the checker would always report missing "_"
        // even when the arms are mathematically exhaustive). This matches C#'s
        // behavior where non-exhaustive tuple switches produce a warning at most.
        if(scrutineeType is TupleType)
        {
            return Array.Empty<string>();
        }

        // Rule 4: Numeric/string/other unbounded types — require catch-all.
        // Since we already checked for catch-all in Rule 1 and didn't find one,
        // the switch is not exhaustive for unbounded types.
        // NOTE: Regex patterns (RegexConstantConstantLiteralNode) are intentionally
        // non-exhaustive — regex patterns over strings can never prove they cover all
        // possible string values. They fall through to this rule because the string
        // scrutinee is an unbounded type. (Requirement 7.5)
        return new[] { "_" };
    }

    /// <summary>
    /// Returns <c>true</c> if any arm in the list is an unguarded catch-all pattern.
    /// A catch-all is a discard pattern (<c>_</c>) or a <c>var x</c> pattern.
    /// </summary>
    private static bool HasUnguardedCatchAll(IReadOnlyList<(PatternNode Pattern, bool HasGuard)> arms)
    {
        for(var i = 0; i < arms.Count; i++)
        {
            if(arms[i].HasGuard)
            {
                continue;
            }

            if(IsCatchAllPattern(arms[i].Pattern))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a pattern is a catch-all (discard <c>_</c>, <c>var x</c>,
    /// or a tuple positional pattern where every element is itself a catch-all).
    /// </summary>
    private static bool IsCatchAllPattern(PatternNode pattern)
    {
        // Navigate the pattern hierarchy: PatternNode → OrPatternNode → AndPatternNode → UnaryPatternNode → PrimaryPatternNode
        var primary = TryGetSinglePrimaryPattern(pattern);
        if(primary is null)
        {
            return false;
        }

        if(primary is VarPatternPrimaryPatternNode or DiscardPatternPrimaryPatternNode)
        {
            return true;
        }

        // A tuple positional pattern with all catch-all elements is itself a catch-all.
        if(primary is TuplePositionalPatternPrimaryPatternNode tuplePattern)
        {
            if(!IsCatchAllPattern(tuplePattern.First))
            {
                return false;
            }

            if(!IsCatchAllPattern(tuplePattern.Rest))
            {
                return false;
            }

            for(var i = 0; i < tuplePattern.Patterns.Length; i++)
            {
                if(!IsCatchAllPattern(tuplePattern.Patterns[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks bool exhaustiveness by tracking which boolean constants are covered
    /// by unguarded arms.
    /// </summary>
    private static IReadOnlyList<string> CheckBoolExhaustiveness(
        IReadOnlyList<(PatternNode Pattern, bool HasGuard)> arms)
    {
        var trueFound = false;
        var falseFound = false;

        for(var i = 0; i < arms.Count; i++)
        {
            if(arms[i].HasGuard)
            {
                continue;
            }

            var primary = TryGetSinglePrimaryPattern(arms[i].Pattern);
            if(primary is null)
            {
                continue;
            }

            if(primary is ConstantPatternPrimaryPatternNode constantPattern)
            {
                if(constantPattern.Literal is TrueConstantConstantLiteralNode)
                {
                    trueFound = true;
                }
                else if(constantPattern.Literal is FalseConstantConstantLiteralNode)
                {
                    falseFound = true;
                }
            }
        }

        if(trueFound && falseFound)
        {
            return Array.Empty<string>();
        }

        var missing = new List<string>();
        if(!trueFound)
        {
            missing.Add("true");
        }

        if(!falseFound)
        {
            missing.Add("false");
        }

        return missing;
    }

    /// <summary>
    /// Checks type union exhaustiveness by flattening nested unions to leaf types
    /// and tracking which constituent types are covered by unguarded type pattern arms.
    /// </summary>
    private static IReadOnlyList<string> CheckUnionExhaustiveness(
        UnionType unionType,
        IReadOnlyList<(PatternNode Pattern, bool HasGuard)> arms)
    {
        // Flatten nested unions to get all leaf constituent types.
        var leafTypes = new List<IType>();
        FlattenUnionType(unionType, leafTypes);

        // Track which leaf types are covered by unguarded type pattern arms.
        var coveredDisplayNames = new HashSet<string>(StringComparer.Ordinal);

        for(var i = 0; i < arms.Count; i++)
        {
            if(arms[i].HasGuard)
            {
                continue;
            }

            var primary = TryGetSinglePrimaryPattern(arms[i].Pattern);
            if(primary is null)
            {
                continue;
            }

            switch(primary)
            {
                case TypePatternPrimaryPatternNode typePattern:
                    var typeName = ExtractTypeDisplayName(typePattern.Type);
                    if(typeName is not null)
                    {
                        _ = coveredDisplayNames.Add(typeName);
                    }

                    break;

                case BareTypePatternPrimaryPatternNode bareTypePattern:
                    var bareTypeName = ExtractTypeDisplayName(bareTypePattern.Type);
                    if(bareTypeName is not null)
                    {
                        _ = coveredDisplayNames.Add(bareTypeName);
                    }

                    break;
            }
        }

        // Check if all leaf types are covered.
        var uncovered = new List<string>();
        foreach(var leafType in leafTypes)
        {
            if(!coveredDisplayNames.Contains(leafType.DisplayName))
            {
                uncovered.Add(leafType.DisplayName);
            }
        }

        return uncovered;
    }

    /// <summary>
    /// Recursively flattens a union type, collecting all non-union leaf constituent types.
    /// </summary>
    private static void FlattenUnionType(UnionType unionType, List<IType> leafTypes)
    {
        foreach(var constituent in unionType.Constituents)
        {
            if(constituent is UnionType nestedUnion)
            {
                FlattenUnionType(nestedUnion, leafTypes);
            }
            else
            {
                leafTypes.Add(constituent);
            }
        }
    }

    /// <summary>
    /// Extracts the display name from a <see cref="TypeReferenceNode"/> for matching
    /// against union constituent type display names.
    /// </summary>
    private static string? ExtractTypeDisplayName(TypeReferenceNode typeRef)
    {
        return typeRef.First switch
        {
            PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef => primitiveRef.PrimitiveType switch
            {
                IntTypePrimitiveTypeNode => "Int32",
                UintTypePrimitiveTypeNode => "UInt32",
                LongTypePrimitiveTypeNode => "Int64",
                UlongTypePrimitiveTypeNode => "UInt64",
                ShortTypePrimitiveTypeNode => "Int16",
                UshortTypePrimitiveTypeNode => "UInt16",
                ByteTypePrimitiveTypeNode => "Byte",
                SbyteTypePrimitiveTypeNode => "SByte",
                FloatTypePrimitiveTypeNode => "Single",
                DoubleTypePrimitiveTypeNode => "Double",
                DecimalTypePrimitiveTypeNode => "Decimal",
                BoolTypePrimitiveTypeNode => "Boolean",
                CharTypePrimitiveTypeNode => "Char",
                StringTypePrimitiveTypeNode => "String",
                RegexTypePrimitiveTypeNode => "Regex",
                _ => null,
            },
            NamedSingleTypeRefSingleTypeRefNode namedRef => namedRef.Name.IDENTIFIER.Lexeme,
            _ => null,
        };
    }

    /// <summary>
    /// Attempts to extract the single <see cref="PrimaryPatternNode"/> from a
    /// <see cref="PatternNode"/>, navigating through the Or → And → Unary chain.
    /// Returns <c>null</c> if the pattern is compound (has multiple alternatives or conjuncts).
    /// </summary>
    private static PrimaryPatternNode? TryGetSinglePrimaryPattern(PatternNode pattern)
    {
        var orPattern = pattern.OrPattern;

        // If there are multiple or-alternatives, this is not a simple pattern.
        if(orPattern.AndPatterns.Length > 0)
        {
            return null;
        }

        var andPattern = orPattern.Left;

        // If there are multiple and-conjuncts, this is not a simple pattern.
        if(andPattern.UnaryPatterns.Length > 0)
        {
            return null;
        }

        var unaryPattern = andPattern.Left;

        // Only direct patterns (not negated) can be catch-alls or simple patterns.
        if(unaryPattern is not DirectPatternUnaryPatternNode directPattern)
        {
            return null;
        }

        // Handle parenthesized patterns by recursing into the inner pattern.
        if(directPattern.Primary is ParenthesizedPatternPrimaryPatternNode parenthesized)
        {
            return TryGetSinglePrimaryPattern(parenthesized.Inner);
        }

        return directPattern.Primary;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type represents a boolean type.
    /// Matches both the legacy <c>"bool"</c> keyword type and the stdlib <c>"Boolean"</c> type.
    /// </summary>
    private static bool IsBoolType(IType type)
    {
        var name = type.DisplayName;
        return name is "bool" or "Boolean";
    }
}