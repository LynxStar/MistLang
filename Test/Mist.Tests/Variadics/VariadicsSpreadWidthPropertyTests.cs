// Feature: variadics, task 14.3: property test for spread width (classic pipeline).
//
// Property 5: Spread width equals source length.
// **Validates: Requirements 6.8, 8**
//
// Spreading is width-preserving: expanding an operand of length N yields exactly N ordered
// slots, and an EMPTY operand yields ZERO. Two spread flavors are exercised, both driven
// through the pure classic-track rules in-process (ZERO external process spawns):
//
//   * The TYPE-side spread (`TArgs...` over a comptime `Type[]`, Requirements 6.8 / 8.1) is
//     the single unbracketed pack absorbing the operand's elements: a lone
//     `UnbracketedPack` slot bound to a use-site group of N element types yields a pack
//     binding of exactly N elements in order, and the empty group yields zero. Both the
//     bracketed spelling (one `Group` argument) and the flat spelling (N `Scalar`
//     arguments) collect to the same width. Exercised via
//     TypeCheckerAnalyzer.BindPackSlots.
//   * The VALUE-side spread (`x...` tuple decomposition, Requirement 8.2) expands a tuple
//     of N elements into exactly N argument slots: the pure decomposition rule accepts the
//     tuple against exactly N positionally-matching slots and reports a SlotCountMismatch
//     against any other count -- so the decomposition width is precisely N (an empty tuple
//     against zero slots is well-typed; against any non-zero count it is a count mismatch).
//     Exercised via TypeCheckerAnalyzer.CheckValueSpreadDecomposition.
//
// The empty-operand edge (N = 0) and small arities are only reachable through these pure
// routines because the grammar has no empty or one-element concrete tuple literal. Element
// types are drawn from primitives whose identity is stable without the stdlib (int/bool/
// char), per the 13.x/14.x precedent; the value-side rule uses each element's own type as
// its matching slot so the count is the sole variable under test.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated spread-width scenario: an ordered list of <see cref="Elements"/> of length N
/// drawn from the stable primitive palette. The intended spread width is N by construction.
/// </summary>
public sealed record SpreadWidthScenario(IReadOnlyList<IType> Elements)
{
    public override string ToString()
    {
        return $"width={Elements.Count}: ({string.Join(", ", Elements.Select(t => t.DisplayName))})";
    }
}

/// <summary>
/// Property test for <b>Property 5 (Spread width equals source length)</b> over the classic
/// pipeline. Drives <see cref="TypeCheckerAnalyzer.BindPackSlots"/> (type-side spread width)
/// and <see cref="TypeCheckerAnalyzer.CheckValueSpreadDecomposition"/> (value-side spread
/// width) directly, asserting that spreading an operand of length N yields exactly N ordered
/// slots and that an empty operand yields zero. <b>Validates: Requirements 6.8, 8.</b>
/// </summary>
public sealed class VariadicsSpreadWidthPropertyTests
{
    // Primitive singletons whose resolution / DisplayName are stable without the stdlib.
    // Each is a distinct interned instance, so positional identity is checkable by reference.
    private static readonly IType[] Palette =
        [LegacyPrimitiveTypes.Int, LegacyPrimitiveTypes.Bool, LegacyPrimitiveTypes.Char];

    // ═══════════════════════════════════════════════════════════════════════
    // Property 5: spreading an operand of length N yields exactly N ordered slots,
    // and an empty operand yields zero. **Validates: Requirements 6.8, 8**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsSpreadWidthPropertyTests) }, MaxTest = 200)]
    public bool SpreadWidth_EqualsSourceLength(SpreadWidthScenario scenario)
    {
        var elements = scenario.Elements;
        var n = elements.Count;

        // ── TYPE-side spread (Requirements 6.8, 8.1): the single unbracketed pack absorbs
        //    the operand's elements. Both the bracketed group spelling and the flat scalar
        //    spelling expand to exactly N ordered elements (empty -> zero). ──
        var slots = new[] { TypeCheckerAnalyzer.PackBindingSlotKind.UnbracketedPack };

        var bracketed = TypeCheckerAnalyzer.BindPackSlots(
            slots,
            new[] { TypeCheckerAnalyzer.PackUseSiteArgument.Group(elements) });
        AssertPackWidth(bracketed, elements);

        var flatArgs = new TypeCheckerAnalyzer.PackUseSiteArgument[n];
        for(var i = 0; i < n; i++)
        {
            flatArgs[i] = TypeCheckerAnalyzer.PackUseSiteArgument.Scalar(elements[i]);
        }

        var flat = TypeCheckerAnalyzer.BindPackSlots(slots, flatArgs);
        AssertPackWidth(flat, elements);

        // ── VALUE-side spread (Requirement 8.2): a tuple of N elements decomposes into
        //    exactly N argument slots. Against exactly N positionally-matching slots the
        //    decomposition is well-typed; against any other count it is a SlotCountMismatch.
        //    Using each element's own type as its matching slot makes the count the only
        //    variable, so this asserts "width == N" and, at N = 0, "empty -> zero". ──
        Assert.Equal(
            TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.WellTyped,
            TypeCheckerAnalyzer.CheckValueSpreadDecomposition(elements, elements, model: null, out var wellTypedIndex));
        Assert.Equal(-1, wellTypedIndex);

        // N + 1 slots is a different width, so the decomposition does NOT fill it.
        var oneMoreSlot = elements.Append(LegacyPrimitiveTypes.Int).ToArray();
        Assert.Equal(
            TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotCountMismatch,
            TypeCheckerAnalyzer.CheckValueSpreadDecomposition(elements, oneMoreSlot, model: null, out _));

        // N - 1 slots (when N > 0) is likewise a different width.
        if(n > 0)
        {
            var oneFewerSlot = elements.Take(n - 1).ToArray();
            Assert.Equal(
                TypeCheckerAnalyzer.ValueSpreadDecompositionStatus.SlotCountMismatch,
                TypeCheckerAnalyzer.CheckValueSpreadDecomposition(elements, oneFewerSlot, model: null, out _));
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generator
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<SpreadWidthScenario> SpreadWidthScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<SpreadWidthScenario> GenScenario()
    {
        return
            from n in Gen.Choose(0, 8)
            from indices in Gen.ArrayOf(Gen.Choose(0, Palette.Length - 1), n)
            let elements = indices.Select(i => Palette[i]).ToArray()
            select new SpreadWidthScenario(elements);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Assertion helper
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Asserts that a bound pack outcome carries exactly one pack slot whose ordered elements
    /// equal <paramref name="expected"/> element-for-element (so the pack width equals the
    /// source length and the empty operand yields zero elements).
    /// </summary>
    private static void AssertPackWidth(
        TypeCheckerAnalyzer.PackBindingOutcome outcome,
        IReadOnlyList<IType> expected)
    {
        Assert.Equal(TypeCheckerAnalyzer.PackBindingStatus.Bound, outcome.Status);
        var binding = Assert.Single(outcome.Slots);
        Assert.True(binding.IsPack);
        Assert.Equal(expected.Count, binding.Elements.Count);
        for(var i = 0; i < expected.Count; i++)
        {
            Assert.Same(expected[i], binding.Elements[i]);
        }
    }
}