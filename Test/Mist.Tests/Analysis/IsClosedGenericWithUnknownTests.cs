// Feature: unknown-readonly-semantics (Task 1)
// **Validates: Requirements 2.2, 2.3, 8.1–8.5**
//
// Unit tests for the IsClosedGenericWithUnknown helper and its recursive
// sub-helpers (ContainsUnknownTypeArgument, ContainsOpenTypeParameter).
// These tests construct IType instances directly — no compilation or
// process spawning required.

using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Analysis;

/// <summary>
/// Tests for <see cref="ReadonlyAnalyzer.IsClosedGenericWithUnknown"/>,
/// <see cref="ReadonlyAnalyzer.ContainsUnknownTypeArgument"/>, and
/// <see cref="ReadonlyAnalyzer.ContainsOpenTypeParameter"/>.
/// </summary>
public class IsClosedGenericWithUnknownTests
{
    // ── Shared type definitions ──────────────────────────────────────────

    private static readonly ClassType ListDef = BuildGenericClass("List", arity: 1);
    private static readonly ClassType MapDef = BuildGenericClass("Map", arity: 2);
    private static readonly ClassType OptionalDef = BuildGenericClass("Optional", arity: 1);

    private static ClassType BuildGenericClass(string name, int arity)
    {
        var c = new ClassType(name, new Scope(parent: null), containingNamespace: "Test");
        var tps = new TypeParameterType[arity];
        for(var i = 0; i < arity; i++)
            tps[i] = new TypeParameterType($"T{i}", i);
        c.TypeParameters = tps;
        return c;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 2.2 — List<unknown> is a Closed_Generic_With_Unknown
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ListOfUnknown_ReturnsTrue()
    {
        var listOfUnknown = new ConstructedType(ListDef, new IType[] { UnknownType.Instance });

        Assert.True(ReadonlyAnalyzer.IsClosedGenericWithUnknown(listOfUnknown));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 2.2 — Map<string, unknown> is a Closed_Generic_With_Unknown
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOfStringUnknown_ReturnsTrue()
    {
        var mapOfStringUnknown = new ConstructedType(
            MapDef, new IType[] { LegacyPrimitiveTypes.String, UnknownType.Instance });

        Assert.True(ReadonlyAnalyzer.IsClosedGenericWithUnknown(mapOfStringUnknown));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 2.3 — Optional<List<unknown>> is detected (nested)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OptionalOfListOfUnknown_ReturnsTrue()
    {
        var listOfUnknown = new ConstructedType(ListDef, new IType[] { UnknownType.Instance });
        var optionalOfListOfUnknown = new ConstructedType(OptionalDef, new IType[] { listOfUnknown });

        Assert.True(ReadonlyAnalyzer.IsClosedGenericWithUnknown(optionalOfListOfUnknown));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 8.1 — List<int> is NOT a Closed_Generic_With_Unknown
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ListOfInt_ReturnsFalse()
    {
        var listOfInt = new ConstructedType(ListDef, new IType[] { LegacyPrimitiveTypes.Int });

        Assert.False(ReadonlyAnalyzer.IsClosedGenericWithUnknown(listOfInt));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 8.1–8.3 — List<T> is NOT a Closed_Generic_With_Unknown
    //                        (open type parameter)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ListOfT_ReturnsFalse()
    {
        var T = new TypeParameterType("T", 0);
        var listOfT = new ConstructedType(ListDef, new IType[] { T });

        Assert.False(ReadonlyAnalyzer.IsClosedGenericWithUnknown(listOfT));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Requirement 8.5 — Map<T, unknown> is NOT a Closed_Generic_With_Unknown
    //                    (has open type parameter alongside unknown)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MapOfTUnknown_ReturnsFalse()
    {
        var T = new TypeParameterType("T", 0);
        var mapOfTUnknown = new ConstructedType(
            MapDef, new IType[] { T, UnknownType.Instance });

        Assert.False(ReadonlyAnalyzer.IsClosedGenericWithUnknown(mapOfTUnknown));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge case — null type returns false
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NullType_ReturnsFalse()
    {
        Assert.False(ReadonlyAnalyzer.IsClosedGenericWithUnknown(null));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge case — non-ConstructedType (bare UnknownType) returns false
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void BareUnknownType_ReturnsFalse()
    {
        Assert.False(ReadonlyAnalyzer.IsClosedGenericWithUnknown(UnknownType.Instance));
    }
}