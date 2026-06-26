// Feature: value-tuples, Property 2: TupleType DisplayName and Kind invariant
// **Validates: Requirements 5.1**

using FsCheck;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using Mist.Tests.Generators;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for <see cref="TupleType"/> invariants.
/// Verifies that <c>DisplayName</c>, <c>Kind</c>, and <c>Arity</c> are
/// always consistent with the element types and names used to construct
/// the instance.
/// </summary>
public class TupleTypePropertyTests
{
    /// <summary>
    /// Property 2: For any TupleType, Kind is always TypeKind.Struct.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool Kind_IsAlwaysStruct(TupleType tuple)
    {
        return tuple.Kind == TypeKind.Struct;
    }

    /// <summary>
    /// Property 2: For any TupleType, Arity equals ElementTypes.Count.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool Arity_EqualsElementTypesCount(TupleType tuple)
    {
        return tuple.Arity == tuple.ElementTypes.Count;
    }

    /// <summary>
    /// Property 2: For any TupleType with all unnamed elements (ElementNames is null),
    /// DisplayName matches the format "(T1, T2, ..., Tn)".
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool DisplayName_MatchesFormat(TupleType tuple)
    {
        var expected = BuildExpectedDisplayName(tuple.ElementTypes, tuple.ElementNames);
        return tuple.DisplayName == expected;
    }

    /// <summary>
    /// Property 2: DisplayName always starts with '(' and ends with ')'.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool DisplayName_IsParenthesized(TupleType tuple)
    {
        return tuple.DisplayName.StartsWith('(') && tuple.DisplayName.EndsWith(')');
    }

    /// <summary>
    /// Property 2: DisplayName contains each element's DisplayName (with optional
    /// name suffix) as a substring, confirming all elements are represented.
    ///
    /// **Validates: Requirements 5.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(TupleTypeGenerator) })]
    public bool DisplayName_ContainsAllElementDisplayNames(TupleType tuple)
    {
        for(var i = 0; i < tuple.ElementTypes.Count; i++)
        {
            var elementDisplay = tuple.ElementTypes[i].DisplayName;
            var name = tuple.ElementNames is not null && i < tuple.ElementNames.Count
                ? tuple.ElementNames[i]
                : null;
            var expectedPart = name is not null
                ? $"{elementDisplay} {name}"
                : elementDisplay;

            if(!tuple.DisplayName.Contains(expectedPart))
                return false;
        }

        return true;
    }

    // ─── Helper ──────────────────────────────────────────────────────────

    /// <summary>
    /// Independently computes the expected display name from element types and names,
    /// mirroring the specification format.
    /// </summary>
    private static string BuildExpectedDisplayName(
        IReadOnlyList<IType> elementTypes,
        IReadOnlyList<string?>? elementNames)
    {
        var parts = new string[elementTypes.Count];
        for(var i = 0; i < elementTypes.Count; i++)
        {
            var name = elementNames is not null && i < elementNames.Count
                ? elementNames[i]
                : null;
            parts[i] = name is not null
                ? $"{elementTypes[i].DisplayName} {name}"
                : elementTypes[i].DisplayName;
        }

        return $"({string.Join(", ", parts)})";
    }
}