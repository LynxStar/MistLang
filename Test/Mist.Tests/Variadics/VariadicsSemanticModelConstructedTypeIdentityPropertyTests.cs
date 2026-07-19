// Feature: variadics, task 9.4: property test for classic (legacy Mist.Semantics)
// constructed variadic type identity.
//
// Property 2: Type identity reflects the pack value.
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
//
// For any variadic family Foo and any two comptime Type[] values v1 and v2, the
// constructed types Foo<v1> and Foo<v2> are the SAME type if and only if v1 and v2 are
// equal Type[] values -- the same length and, at every index, elementwise-equal element
// types under the type-equality relation used for ordinary type arguments. Identity is
// therefore order-sensitive (R5.3) and arity-sensitive (R5.4), keyed by (family, Type[]
// value) (R5.1, R5.2).
//
// This is the CLASSIC-track counterpart of the ProgramModel-track property test
// (ConstructedVariadicTypeIdentityPropertyTests, task 4.4). Where that test drives the
// ProgramModel TypeInterner and compares interned reference identity, this one builds
// legacy Mist.Semantics ConstructedType instances directly -- the pack's comptime Type[]
// value riding on a DependentValueArgument.ForPack at the pack slot -- and compares the
// STRUCTURAL identity computed by TypeCheckerAnalyzer.TypesEqual (the legacy model does
// not intern constructed types). It mirrors the construction pattern of the classic smoke
// test (VariadicsSemanticModelConstructedTypeIdentitySmokeTests, task 9.3) and generalizes
// its six facts into a property over generated pack shapes.
//
// Everything runs entirely IN-PROCESS: nothing is parsed, built, emitted, or run, so there
// are ZERO external process spawns.
//
// Element types are drawn from a small FIXED pool of the reference-stable legacy primitive
// singletons (int, string, bool, double). Two properties of that pool make reference
// equality a faithful, INDEPENDENT oracle for "equal Type[] value under TypesEqual":
//   * the same pool entry is the same singleton, so it is reference-equal (and TypesEqual);
//   * distinct pool entries have pairwise-distinct display names and are not
//     primitive-equivalent, so TypesEqual is false between any two of them.
// Pack shapes are described as index lists into that pool, so two Type[] values are equal
// exactly when their index lists match elementwise -- which is what the oracle computes over
// the resolved element references (never from the generator mode).

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Analysis.Builtin;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated pair of pack shapes, each an ordered list of indices into a fixed
/// element-type pool. Two shapes denote equal <c>Type[]</c> values exactly when the index
/// lists have the same length and match at every position.
/// </summary>
public sealed record SemanticPackValuePair(IReadOnlyList<int> First, IReadOnlyList<int> Second)
{
    public override string ToString()
    {
        return $"[{string.Join(", ", First)}] vs [{string.Join(", ", Second)}]";
    }
}

/// <summary>
/// Property test for <b>Property 2 (Type identity reflects the pack value)</b> over the
/// classic (legacy <c>Mist.Semantics</c>) type system. Builds <c>Foo&lt;v1&gt;</c> and
/// <c>Foo&lt;v2&gt;</c> as <see cref="ConstructedType"/> instances whose sole pack slot binds
/// a <see cref="DependentValueArgument.ForPack"/> value, then asserts the biconditional
/// <c>TypeCheckerAnalyzer.TypesEqual(Foo&lt;v1&gt;, Foo&lt;v2&gt;) == (v1 and v2 are equal
/// Type[] values)</c> in both argument orders. The generator deliberately covers equal
/// pairs, order differences, arity differences, the empty pack, and fully independent draws
/// so both directions of the iff are exercised.
/// <b>Validates: Requirements 5.1, 5.2, 5.3, 5.4.</b>
/// </summary>
public sealed class VariadicsSemanticModelConstructedTypeIdentityPropertyTests
{
    // Small pool + short packs so that random equal sequences (and the empty pack) occur
    // naturally, keeping both branches of the iff well-populated.
    private const int PoolSize = 4;
    private const int MaxPackLength = 4;

    // Shared, reference-stable infrastructure mirroring the classic smoke test. The pack's
    // value domain is the Type[] metatype; the pack-slot placeholder is shared so the
    // ordinary type-argument comparison is reference-equal and identity is driven purely by
    // the pack's Type[] value.
    private static readonly IType PackDomain = new ArrayType(LegacyPrimitiveTypes.Type);

    private static readonly TypeParameterType PackSlot =
        new("TArgs", 0) { IsPack = true, IsUnbracketedPack = true };

    /// <summary>Arbitrary registration for the FsCheck property below.</summary>
    public static Arbitrary<SemanticPackValuePair> PackValuePairs()
    {
        var genIndex = Gen.Choose(0, PoolSize - 1);

        var genIndexList =
            from length in Gen.Choose(0, MaxPackLength)
            from indices in Gen.ArrayOf(genIndex, length)
            select indices;

        var gen =
            from first in genIndexList
            from mode in Gen.Choose(0, 4)
            from independent in genIndexList
            from extra in genIndex
            select new SemanticPackValuePair(first, DeriveSecond(first, mode, independent, extra));

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Foo<v1> and Foo<v2> are the same type IFF v1 and v2 are equal
    // Type[] values (same length, elementwise-equal). Order- and arity-sensitive.
    // **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(VariadicsSemanticModelConstructedTypeIdentityPropertyTests) }, MaxTest = 200)]
    public bool ConstructedType_Identity_HoldsIff_PackValuesEqual(SemanticPackValuePair pair)
    {
        // Fixed, reference-stable element-type pool: reference identity is stable within this
        // iteration, so index equality corresponds exactly to elementwise Type equality.
        var pool = BuildElementPool();

        // One shared family instance: TypesEqual keys on ReferenceEquals(GenericDefinition),
        // so both constructions must reference the same family.
        var family = Family("Foo");

        var v1 = Resolve(pool, pair.First);
        var v2 = Resolve(pool, pair.Second);

        // Foo<v1> and Foo<v2>: the pack's comptime Type[] value rides on a
        // DependentValueArgument.ForPack at the single pack slot.
        var fooV1 = Variadic(family, v1);
        var fooV2 = Variadic(family, v2);

        // Independent oracle for "v1 and v2 are equal Type[] values" (R5.2): same length and,
        // at each index, reference-equal element types. For this pool, reference equality
        // coincides with TypesEqual on the elements (distinct pool entries have distinct
        // display names and are not primitive-equivalent), so this faithfully predicts the
        // structural pack comparison without re-deriving it from TypesEqual.
        var packValuesEqual = ElementwiseReferenceEqual(v1, v2);

        // The biconditional, checked in BOTH argument orders so the relation is exercised
        // symmetrically:
        //   equal pack values   => same constructed type
        //   unequal pack values  => distinct constructed types (order or arity differs)
        return TypeCheckerAnalyzer.TypesEqual(fooV1, fooV2) == packValuesEqual
            && TypeCheckerAnalyzer.TypesEqual(fooV2, fooV1) == packValuesEqual;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static ClassType Family(string name)
    {
        return new ClassType(name, new Scope(null), containingNamespace: null);
    }

    /// <summary>Builds <c>family&lt;[elements...]&gt;</c>: a single pack slot bound to the given Type[].</summary>
    private static ConstructedType Variadic(ClassType family, IReadOnlyList<IType> elements)
    {
        var pack = DependentValueArgument.ForPack(elements, PackDomain);
        return new ConstructedType(
            family,
            new IType[] { PackSlot },
            new DependentValueArgument?[] { pack });
    }

    // Distinct legacy primitive singletons: pairwise-distinct display names ("int",
    // "string", "bool", "double") and none primitive-equivalent to another, so TypesEqual on
    // two pool elements is true iff they are the same reference.
    private static IType[] BuildElementPool()
    {
        return
        [
            LegacyPrimitiveTypes.Int,
            LegacyPrimitiveTypes.String,
            LegacyPrimitiveTypes.Bool,
            LegacyPrimitiveTypes.Double,
        ];
    }

    private static IType[] Resolve(IReadOnlyList<IType> pool, IReadOnlyList<int> indices)
    {
        var result = new IType[indices.Count];
        for(var i = 0; i < indices.Count; i++)
        {
            result[i] = pool[indices[i]];
        }

        return result;
    }

    private static bool ElementwiseReferenceEqual(IReadOnlyList<IType> a, IReadOnlyList<IType> b)
    {
        if(a.Count != b.Count)
        {
            return false;
        }

        for(var i = 0; i < a.Count; i++)
        {
            if(!ReferenceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Derives the second pack shape from the first so that, across the mode space, the
    /// generator exercises every case the iff must cover. Correctness of the property does
    /// not depend on the mode: the expected result is computed from the actual resolved
    /// element references, not from the mode.
    /// </summary>
    private static int[] DeriveSecond(int[] first, int mode, int[] independent, int extra)
    {
        if(mode == 0)
        {
            // Identical contents -> equal Type[] values (forward direction of the iff).
            return (int[])first.Clone();
        }

        if(mode == 1)
        {
            // Reversed -> an order difference when length >= 2 and non-palindrome (R5.3);
            // length 0/1 or a palindrome collapses to an equal pair, still a valid case.
            var reversed = (int[])first.Clone();
            Array.Reverse(reversed);
            return reversed;
        }

        if(mode == 2)
        {
            // One extra element -> an arity difference; always distinct (R5.4).
            var longer = new int[first.Length + 1];
            Array.Copy(first, longer, first.Length);
            longer[first.Length] = extra;
            return longer;
        }

        if(mode == 3 && first.Length > 0)
        {
            // Drop the last element -> an arity difference (R5.4).
            var shorter = new int[first.Length - 1];
            Array.Copy(first, shorter, first.Length - 1);
            return shorter;
        }

        // mode == 4 (or mode == 3 over an empty first): a fully independent draw, giving a mix
        // of equal (including empty vs empty), order-different, and arity-different pairs so
        // neither branch of the iff is starved.
        return independent;
    }
}