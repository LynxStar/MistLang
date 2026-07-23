// Feature: variadics, task 12.2: property test for the shared binding rule over the CLASSIC
// (legacy Mist.Semantics) pipeline.
//
// Property 4: Binding determinism and totality.
// **Validates: Requirements 4, 3**
//
// For any declared slot list with AT MOST ONE Unbracketed_Pack and any argument list whose
// length is at least (fixed slots + Bracketed_Pack slots), the binding rule yields EXACTLY
// ONE assignment in which the leading fixed and bracketed slots take the leading arguments in
// order, the trailing fixed and bracketed slots take the trailing arguments in order, and the
// Unbracketed_Pack takes exactly the contiguous middle run (possibly empty); every argument
// list shorter than that minimum is rejected -- the binder returns
// PackBindingStatus.TooFewArguments, which SemanticModelBuilder surfaces as MIST_VAR_0005
// while resolving a variadic construction (the end-to-end diagnostic mapping is locked by the
// task 12.1 example test VariadicsSemanticModelBindingRuleTests and the task 12.4 edge cases).
//
// This is the CLASSIC-track counterpart of the ProgramModel-track property test
// (VariadicsBindingDeterminismPropertyTests, task 7.2). Where that test drives the ProgramModel
// PackBinder.Bind, this one drives the classic-track rule TypeCheckerAnalyzer.BindPackSlots
// DIRECTLY (design Option A) -- the pure, kind-driven function SemanticModelBuilder invokes
// while folding a flat use-site type-argument list into the canonical bracketed form. Driving
// the pure rule gives the crispest coverage of determinism/totality and the "unique assignment
// vs the too-few threshold" boundary. BindPackSlots and its types (PackBindingSlotKind,
// PackUseSiteArgument, PackSlotBinding, PackBindingOutcome, PackBindingStatus) are internal;
// the test assembly has InternalsVisibleTo. There are ZERO external process spawns: the binder
// is a pure function invoked in-process, and nothing is parsed, built, emitted, or run.
//
// Generator strategy (mirrors the ProgramModel-track test): draw a number of non-pack slots,
// each a Fixed or a Bracketed_Pack, and OPTIONALLY insert exactly one Unbracketed_Pack at a
// random position (leading, middle, or trailing) -- so every generated slot list carries at
// most one Unbracketed_Pack. Then draw an argument-list length: with a pack, in
// [0, nonPackCount + 4] so the run spans both the too-few branch and a bound pack absorbing a
// 0..4 element middle; without a pack, in [0, nonPackCount] so it spans too-few and the exact
// fill (the no-pack case has no slot to absorb surplus, which is outside this property's
// domain). Each argument carries a DISTINCT interned classic type so the argument-to-slot
// mapping ("in order", "contiguous middle") is checked by reference, not merely by count. The
// expected minimum and middle assignment are computed independently from Property 4's wording
// and compared against the binder's output.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated binding scenario, described with public data only (so the xUnit test method
/// signature stays public while the internal <see cref="TypeCheckerAnalyzer.PackBindingSlotKind"/>
/// / <see cref="TypeCheckerAnalyzer.PackUseSiteArgument"/> types are materialized inside the
/// test). Slot kinds are encoded as <c>0 = Fixed</c>, <c>1 = BracketedPack</c>,
/// <c>2 = UnbracketedPack</c>, with at most one <c>2</c>. <see cref="ArgumentCount"/> is the
/// length of the flat use-site argument list; each argument is a scalar carrying a distinct type.
/// </summary>
public sealed record SemanticBindingScenario(IReadOnlyList<int> SlotKinds, int ArgumentCount)
{
    public override string ToString()
    {
        var slotText = SlotKinds.Count == 0 ? "(none)" : string.Join(", ", SlotKinds.Select(SlotKindName));
        return $"slots=[{slotText}], args={ArgumentCount}";
    }

    private static string SlotKindName(int kind)
    {
        return kind switch
        {
            0 => "Fixed",
            1 => "BracketedPack",
            2 => "UnbracketedPack",
            _ => $"?{kind}",
        };
    }
}

/// <summary>
/// Property test for <b>Property 4 (Binding determinism and totality)</b> over the classic
/// (legacy <c>Mist.Semantics</c>) pipeline. Exercises the pure binding rule
/// <see cref="TypeCheckerAnalyzer.BindPackSlots"/> across generated slot lists (at most one
/// Unbracketed_Pack) and argument lists, asserting the exact too-few threshold, the unique
/// inward-from-both-ends assignment with the pack absorbing the contiguous middle, and that a
/// repeated call yields the identical assignment. <b>Validates: Requirements 4, 3.</b>
/// </summary>
public sealed class VariadicsSemanticModelBindingDeterminismPropertyTests
{
    // Slot-kind encoding shared between the generator and the materializer.
    private const int FixedKind = 0;
    private const int BracketedPackKind = 1;
    private const int UnbracketedPackKind = 2;

    // =====================================================================
    // Property 4: the binding rule is deterministic and total -- below the minimum it
    // rejects (TooFewArguments / MIST_VAR_0005); at or above it yields the one assignment
    // where the ends bind inward and the single pack takes exactly the contiguous middle.
    // **Validates: Requirements 4, 3**
    // =====================================================================

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelBindingDeterminismPropertyTests) }, MaxTest = 200)]
    public bool BindingRule_IsDeterministicAndTotal_OverLegacyModel(SemanticBindingScenario scenario)
    {
        var slots = scenario.SlotKinds.Select(ToSlotKind).ToArray();

        // Distinct interned type per argument, so "leading/trailing in order" and "pack takes
        // the contiguous middle" are verifiable by reference identity, not just by count.
        var argumentTypes = new IType[scenario.ArgumentCount];
        var arguments = new TypeCheckerAnalyzer.PackUseSiteArgument[scenario.ArgumentCount];
        for(var i = 0; i < scenario.ArgumentCount; i++)
        {
            argumentTypes[i] = NamedType($"Arg{i}");
            arguments[i] = TypeCheckerAnalyzer.PackUseSiteArgument.Scalar(argumentTypes[i]);
        }

        // Independent expectations, derived straight from Property 4's wording rather than
        // from the binder's internals: the single pack (if any) splits the slot list into a
        // leading run and a trailing run, and the minimum is the count of non-pack slots.
        var packIndex = IndexOfUnbracketedPack(slots);
        var leadingCount = packIndex >= 0 ? packIndex : slots.Length;
        var trailingCount = packIndex >= 0 ? slots.Length - packIndex - 1 : 0;
        var minimumRequired = leadingCount + trailingCount;

        var result = TypeCheckerAnalyzer.BindPackSlots(slots, arguments);

        // Totality + the exact too-few threshold: one biconditional pins the boundary that the
        // resolver reports as MIST_VAR_0005. The binder always returns a result (never throws).
        var bound = result.Status == TypeCheckerAnalyzer.PackBindingStatus.Bound;
        Assert.Equal(scenario.ArgumentCount >= minimumRequired, bound);

        if(!bound)
        {
            Assert.Equal(TypeCheckerAnalyzer.PackBindingStatus.TooFewArguments, result.Status);
            Assert.Empty(result.Slots);

            // Determinism holds on the reject path too: a second call agrees.
            Assert.Equal(
                TypeCheckerAnalyzer.PackBindingStatus.TooFewArguments,
                TypeCheckerAnalyzer.BindPackSlots(slots, arguments).Status);
            return true;
        }

        // Exactly one binding per declared slot, in declaration order.
        Assert.Equal(slots.Length, result.Slots.Count);

        // Leading fixed/bracketed slots take the leading arguments one-to-one, in order.
        for(var i = 0; i < leadingCount; i++)
        {
            AssertArityOneSlot(result.Slots[i], slots[i], argumentTypes[i]);
        }

        // Trailing fixed/bracketed slots take the trailing arguments one-to-one, in order.
        for(var j = 0; j < trailingCount; j++)
        {
            var slotIndex = packIndex + 1 + j;
            var argumentIndex = scenario.ArgumentCount - trailingCount + j;
            AssertArityOneSlot(result.Slots[slotIndex], slots[slotIndex], argumentTypes[argumentIndex]);
        }

        if(packIndex >= 0)
        {
            // The single Unbracketed_Pack takes EXACTLY the contiguous middle run, in order
            // (possibly empty), and nothing else -- so every argument is accounted for.
            var packBinding = result.Slots[packIndex];
            Assert.True(packBinding.IsPack);

            var middleLength = scenario.ArgumentCount - trailingCount - leadingCount;
            Assert.Equal(middleLength, packBinding.Elements.Count);
            for(var k = 0; k < middleLength; k++)
            {
                Assert.Same(argumentTypes[leadingCount + k], packBinding.Elements[k]);
            }

            // Full, non-overlapping partition: leading run + pack middle + trailing run.
            Assert.Equal(scenario.ArgumentCount, leadingCount + middleLength + trailingCount);
        }
        else
        {
            // No pack: a Bound result means the arguments filled the fixed/bracketed slots
            // exactly (the generator never supplies surplus in the no-pack case).
            Assert.Equal(minimumRequired, scenario.ArgumentCount);
        }

        // Determinism: the rule yields EXACTLY ONE assignment, so a repeated call is identical.
        AssertSameAssignment(result, TypeCheckerAnalyzer.BindPackSlots(slots, arguments));
        return true;
    }

    // =====================================================================
    // Generator
    // =====================================================================

    /// <summary>Arbitrary registration for the property above.</summary>
    public static Arbitrary<SemanticBindingScenario> BindingScenarios()
    {
        return GenScenario().ToArbitrary();
    }

    private static Gen<SemanticBindingScenario> GenScenario()
    {
        return
            from nonPackCount in Gen.Choose(0, 6)
            from nonPackKinds in Gen.ArrayOf(Gen.Elements(FixedKind, BracketedPackKind), nonPackCount)
            from hasPack in Gen.Elements(true, false)
            from packPosition in Gen.Choose(0, nonPackCount)
            from argumentCount in hasPack
                ? Gen.Choose(0, nonPackCount + 4)
                : Gen.Choose(0, nonPackCount)
            select new SemanticBindingScenario(BuildSlotKinds(nonPackKinds, hasPack, packPosition), argumentCount);
    }

    /// <summary>
    /// Builds the encoded slot-kind list: the drawn Fixed/BracketedPack slots with, when
    /// <paramref name="hasPack"/> is set, exactly one UnbracketedPack spliced in at
    /// <paramref name="packPosition"/> (0 = leading, <c>nonPackCount</c> = trailing, anything
    /// in between = middle). At most one UnbracketedPack is ever produced.
    /// </summary>
    private static int[] BuildSlotKinds(int[] nonPackKinds, bool hasPack, int packPosition)
    {
        if(!hasPack)
        {
            return (int[])nonPackKinds.Clone();
        }

        var slots = new List<int>(nonPackKinds.Length + 1);
        slots.AddRange(nonPackKinds[..packPosition]);
        slots.Add(UnbracketedPackKind);
        slots.AddRange(nonPackKinds[packPosition..]);
        return slots.ToArray();
    }

    // =====================================================================
    // Assertion + materialization helpers
    // =====================================================================

    /// <summary>
    /// Asserts an arity-1 slot (a leading/trailing fixed scalar or bracketed pack) is bound to
    /// its single expected argument: a Fixed slot is not a pack, a BracketedPack slot is, and
    /// either way the slot carries exactly that one argument's type.
    /// </summary>
    private static void AssertArityOneSlot(
        TypeCheckerAnalyzer.PackSlotBinding binding,
        TypeCheckerAnalyzer.PackBindingSlotKind slot,
        IType expectedType)
    {
        Assert.Equal(slot != TypeCheckerAnalyzer.PackBindingSlotKind.Fixed, binding.IsPack);
        var element = Assert.Single(binding.Elements);
        Assert.Same(expectedType, element);
    }

    /// <summary>
    /// Asserts two binding outcomes are the identical assignment: same status, same slot count,
    /// and, per slot, the same pack flag and the same ordered element references.
    /// </summary>
    private static void AssertSameAssignment(
        TypeCheckerAnalyzer.PackBindingOutcome first,
        TypeCheckerAnalyzer.PackBindingOutcome second)
    {
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Slots.Count, second.Slots.Count);
        for(var i = 0; i < first.Slots.Count; i++)
        {
            Assert.Equal(first.Slots[i].IsPack, second.Slots[i].IsPack);
            Assert.Equal(first.Slots[i].Elements.Count, second.Slots[i].Elements.Count);
            for(var k = 0; k < first.Slots[i].Elements.Count; k++)
            {
                Assert.Same(first.Slots[i].Elements[k], second.Slots[i].Elements[k]);
            }
        }
    }

    private static int IndexOfUnbracketedPack(TypeCheckerAnalyzer.PackBindingSlotKind[] slots)
    {
        for(var i = 0; i < slots.Length; i++)
        {
            if(slots[i] == TypeCheckerAnalyzer.PackBindingSlotKind.UnbracketedPack)
            {
                return i;
            }
        }

        return -1;
    }

    private static TypeCheckerAnalyzer.PackBindingSlotKind ToSlotKind(int kind)
    {
        return kind switch
        {
            FixedKind => TypeCheckerAnalyzer.PackBindingSlotKind.Fixed,
            BracketedPackKind => TypeCheckerAnalyzer.PackBindingSlotKind.BracketedPack,
            UnbracketedPackKind => TypeCheckerAnalyzer.PackBindingSlotKind.UnbracketedPack,
            _ => throw new InvalidOperationException($"Unhandled slot kind code: {kind}"),
        };
    }

    /// <summary>Creates a fresh opaque classic carrier type with the given display name.</summary>
    private static IType NamedType(string name)
    {
        return new ClassType(name, new Scope(null), containingNamespace: null);
    }
}