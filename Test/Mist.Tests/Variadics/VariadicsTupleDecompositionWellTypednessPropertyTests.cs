// Feature: variadics, task 14.4: property test for tuple-decomposition well-typedness
// (classic pipeline).
//
// Property 11: Tuple-decomposition well-typedness.
// **Validates: Requirements 7.5, 8.4**
//
// A value-side spread `x...` decomposes a tuple into ordered argument slots and is well-typed
// IF AND ONLY IF the tuple's ordered element types are positionally assignable to the target
// slots; otherwise the classic type checker emits MIST_VAR_0011 (Requirements 7.5, 8.4). The
// rule applies to EVERY tuple, so a value pack `args...` is simply the case where the tuple
// originated from a pack (Requirement 8.3) -- the pure rule decides identically regardless of
// origin.
//
// This drives the pure classic-track rule TypeCheckerAnalyzer.CheckValueSpreadDecomposition
// DIRECTLY, in-process, with ZERO external process spawns. Element and slot types are drawn
// from primitives whose identity is stable without the stdlib (int/bool/char); among these
// distinct singletons "positionally assignable" is exactly "same type at that position", so an
// independent reference-identity oracle pins the expected status (and, on a type mismatch, the
// offending 0-based index) without consulting the rule under test. The pure statuses map to
// the observable diagnostic as: SlotCountMismatch and SlotTypeMismatch are exactly the cases
// production surfaces as MIST_VAR_0011 (the end-to-end emission is locked by the task 14.1
// example tests VariadicsValueSpreadDecompositionTests).
//
// Scope. Only CONCRETE tuples are decided here (their element types are concretely known). A
// symbolic value pack `(TArgs...)` in an open generic body is recognized as a spread but
// DEFERRED to the generic-enforcement task; its eager decomposition is intentionally NOT
// asserted (see the task 14.1 scope note).

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated decomposition scenario: the ordered <see cref="Elements"/> of a concrete tuple
/// value together with the ordered target <see cref="Slots"/> the spread must fill. Both are
/// drawn from the stable primitive palette so positional assignability reduces to
/// same-type-at-position.
/// </summary>
public sealed record TupleDecompositionScenario(IReadOnlyList<IType> Elements, IReadOnlyList<IType> Slots)
{
    public override string ToString()
    {
        return $"({string.Join(", ", Elements.Select(t => t.DisplayName))}) -> "
            + $"[{string.Join(", ", Slots.Select(t => t.DisplayName))}]";
    }
}

/// <summary>
/// Property test for <b>Property 11 (Tuple-decomposition well-typedness)</b> over the classic
/// pipeline. Drives <see cref="TypeCheckerAnalyzer.CheckValueSpreadDecomposition"/> directly
/// and asserts that a decomposition is well-typed iff the tuple's element types are
/// positionally assignable to the target slots (else the MIST_VAR_0011 condition holds), for
/// every concrete tuple regardless of origin. <b>Validates: Requirements 7.5, 8.4.</b>
/// </summary>
public sealed class VariadicsTupleDecompositionWellTypednessPropertyTests
{
    // Note: a non-well-typed decomposition (SlotCountMismatch / SlotTypeMismatch) is exactly
    // what production surfaces as MIST_VAR_0011 (Requirements 7.5, 8.4); the end-to-end
    // emission is locked by the task 14.1 example tests VariadicsValueSpreadDecompositionTests.

    // Primitive singletons whose resolution / DisplayName are stable without the stdlib.
    // Among these distinct instances, "assignable" reduces to reference identity.
    private static readonly IType[] Palette =
        [LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Char];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 11: well-typed IFF positionally assignable, else the MIST_VAR_0011 condition.
    // **Validates: Requirements 7.5, 8.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsTupleDecompositionWellTypednessPropertyTests) }, MaxTest = 200)]
    public bool Decomposition_WellTypedIffPositionallyAssignable(TupleDecompositionScenario scenario)
    {
        var elements = scenario.Elements;
        var slots = scenario.Slots;

        // Independent oracle, computed from the definition rather than the rule under test:
        // equal count AND same type at every position => well-typed; a count difference is a
        // SlotCountMismatch; otherwise the first differing position is a SlotTypeMismatch.
        var (expectedStatus, expectedIndex) = ExpectedDecomposition(elements, slots);

        var actualStatus = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(
            elements, slots, model: null, out var actualIndex);

        Assert.Equal(expectedStatus, actualStatus);
        Assert.Equal(expectedIndex, actualIndex);

        // The IFF itself: well-typed exactly when positionally assignable.
        var positionallyAssignable = PositionallyAssignable(elements, slots);
        var wellTyped = actualStatus == TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped;
        Assert.Equal(positionallyAssignable, wellTyped);

        // "else MIST_VAR_0011": production emits the mismatch diagnostic exactly on a
        // non-well-typed decomposition (both SlotCountMismatch and SlotTypeMismatch), i.e.
        // exactly when the tuple is NOT positionally assignable to the slots.
        var wouldEmitMistVar0011 = actualStatus != TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped;
        Assert.Equal(!positionallyAssignable, wouldEmitMistVar0011);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property (Requirement 8.3): decomposition applies to EVERY tuple regardless of origin.
    // The pure rule is a function of the element types alone, so a tuple presented as an
    // ordinary value, as a fresh copy, and as a real TupleType's elements all decide
    // identically against the same slots. Only CONCRETE tuples are fed here; a symbolic pack
    // is a deferred case and is deliberately not asserted (task 14.1 scope note).
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsTupleDecompositionWellTypednessPropertyTests) }, MaxTest = 200)]
    public bool Decomposition_IsOriginAgnostic_ForEveryTuple(TupleDecompositionScenario scenario)
    {
        var slots = scenario.Slots;

        // Same concrete element types, presented three ways: a raw list (as if the caller
        // treated it as an ordinary tuple), a fresh copy, and the ElementTypes of a real
        // TupleType value (as if pack-originated). All must decide identically.
        var raw = scenario.Elements;
        var copy = raw.ToList();
        var tuple = new TupleType(raw.ToArray());

        var viaRaw = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(raw, slots, model: null, out var rawIndex);
        var viaCopy = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(copy, slots, model: null, out var copyIndex);
        var viaTuple = TypeCheckerAnalyzer.CheckValueSpreadDecomposition(tuple.ElementTypes, slots, model: null, out var tupleIndex);

        Assert.Equal(viaRaw, viaCopy);
        Assert.Equal(rawIndex, copyIndex);
        Assert.Equal(viaRaw, viaTuple);
        Assert.Equal(rawIndex, tupleIndex);

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the properties above.</summary>
    public static Arbitrary<TupleDecompositionScenario> TupleDecompositionScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<TupleDecompositionScenario> GenScenario()
    {
        return
            from elementCount in Gen.Choose(0, 6)
            from slotCount in Gen.Choose(0, 6)
            from elementIndices in Gen.ArrayOf(Gen.Choose(0, Palette.Length - 1), elementCount)
            from slotIndices in Gen.ArrayOf(Gen.Choose(0, Palette.Length - 1), slotCount)
            let elements = elementIndices.Select(i => Palette[i]).ToArray()
            let slots = slotIndices.Select(i => Palette[i]).ToArray()
            select new TupleDecompositionScenario(elements, slots);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Independent oracle (reference identity == assignability over the primitive palette)
    // ═══════════════════════════════════════════════════════════════════════

    private static (TypeCheckerAnalyzer.ValueSpreadDecompositionStatus Status, int Index) ExpectedDecomposition(
        IReadOnlyList<IType> elements,
        IReadOnlyList<IType> slots)
    {
        if(elements.Count != slots.Count)
        {
            return (TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotCountMismatch, -1);
        }

        for(var i = 0; i < elements.Count; i++)
        {
            if(!ReferenceEquals(elements[i], slots[i]))
            {
                return (TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotTypeMismatch, i);
            }
        }

        return (TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped, -1);
    }

    private static bool PositionallyAssignable(IReadOnlyList<IType> elements, IReadOnlyList<IType> slots)
    {
        if(elements.Count != slots.Count)
        {
            return false;
        }

        for(var i = 0; i < elements.Count; i++)
        {
            if(!ReferenceEquals(elements[i], slots[i]))
            {
                return false;
            }
        }

        return true;
    }
}