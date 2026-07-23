// variadics, task 4.4: property test for constructed variadic type identity.
//
// Property 2: Type identity reflects the pack value.
// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
//
// For any variadic family Foo and any two comptime Type[] values v1 and v2, the
// constructed types Foo<v1> and Foo<v2> are the SAME type if and only if v1 and v2
// are equal Type[] values -- the same length and, at every index, elementwise-equal
// element types under the type-equality relation the interner uses for ordinary type
// arguments (reference identity). Identity is therefore order-sensitive (R5.3) and
// arity-sensitive (R5.4), keyed by (family, Type[] value) (R5.1, R5.2).
//
// This test drives TypeInterner directly, entirely in-process (no process spawn),
// exactly as the task 4.3 smoke test does. Element types are drawn from a small
// FIXED pool of interned ClassType instances so their reference identity is stable
// within an iteration: that is what makes "int == int" hold across the two Type[]
// values (two independent NamedType("Int32") instances would NOT be reference-equal).
// The pack shapes are described as index lists into that pool, so two Type[] values
// are equal exactly when their index lists match elementwise.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Variadics;

/// <summary>
/// A generated pair of pack shapes, each an ordered list of indices into a fixed
/// element-type pool. Two shapes denote equal <c>Type[]</c> values exactly when the
/// index lists have the same length and match at every position.
/// </summary>
public sealed record PackValuePair(IReadOnlyList<int> First, IReadOnlyList<int> Second)
{
    public override string ToString()
    {
        return $"[{string.Join(", ", First)}] vs [{string.Join(", ", Second)}]";
    }
}

/// <summary>
/// Property test for <b>Property 2 (Type identity reflects the pack value)</b>.
/// Drives <see cref="TypeInterner"/> in-process: builds <c>Foo&lt;v1&gt;</c> and
/// <c>Foo&lt;v2&gt;</c> via <see cref="TypeInterner.GetOrCreateLiftedTypeArray"/> plus
/// <see cref="TypeInterner.GetOrCreateConstructedType"/>, then asserts the biconditional
/// <c>ReferenceEquals(Foo&lt;v1&gt;, Foo&lt;v2&gt;) == (v1 and v2 are equal Type[] values)</c>.
/// The generator deliberately covers equal pairs, order differences, arity differences,
/// the empty pack, and fully independent draws so both directions of the iff are exercised.
/// <b>Validates: Requirements 5.1, 5.2, 5.3, 5.4.</b>
/// </summary>
public sealed class ConstructedVariadicTypeIdentityPropertyTests
{
    // Small pool + short packs so that random equal sequences (and the empty pack)
    // occur naturally, keeping both branches of the iff well-populated.
    private const int PoolSize = 4;
    private const int MaxPackLength = 4;

    /// <summary>Arbitrary registration for the FsCheck property below.</summary>
    public static Arbitrary<PackValuePair> PackValuePairs()
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
            select new PackValuePair(first, DeriveSecond(first, mode, independent, extra));

        return gen.ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property 2: Foo<v1> and Foo<v2> are the same type IFF v1 and v2 are equal
    // Type[] values (same length, elementwise-equal). Order- and arity-sensitive.
    // **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    // ═══════════════════════════════════════════════════════════════════════

    [Property(Arbitrary = new[] { typeof(ConstructedVariadicTypeIdentityPropertyTests) }, MaxTest = 200)]
    public bool ConstructedType_Identity_HoldsIff_PackValuesEqual(PackValuePair pair)
    {
        var interner = new TypeInterner();

        // Fixed, interned element-type pool: reference identity is stable within this
        // iteration, so index equality corresponds exactly to elementwise Type equality.
        var pool = BuildElementPool();
        var family = NamedType("Foo<>");

        var v1 = Resolve(pool, pair.First);
        var v2 = Resolve(pool, pair.Second);

        // Foo<v1> and Foo<v2>: intern the lifted Type[] pack value, then the constructed
        // type whose sole type argument is that lifted value.
        var fooV1 = interner.GetOrCreateConstructedType(
            family, new IMistType[] { interner.GetOrCreateLiftedTypeArray(v1) });
        var fooV2 = interner.GetOrCreateConstructedType(
            family, new IMistType[] { interner.GetOrCreateLiftedTypeArray(v2) });

        // Independent oracle for "v1 and v2 are equal Type[] values" (R5.2): same length
        // and, at each index, reference-equal element types.
        var packValuesEqual = ElementwiseReferenceEqual(v1, v2);
        var sameConstructedType = ReferenceEquals(fooV1, fooV2);

        // The biconditional, both directions:
        //   equal pack values   => same interned constructed type
        //   unequal pack values  => distinct constructed types (order or arity differs)
        return sameConstructedType == packValuesEqual;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static ClassType NamedType(string name)
    {
        return new ClassType(name, "Standard", new Scope(null), null, null);
    }

    private static ClassType[] BuildElementPool()
    {
        return
        [
            NamedType("Int32"),
            NamedType("String"),
            NamedType("Boolean"),
            NamedType("Double"),
        ];
    }

    private static IMistType[] Resolve(IReadOnlyList<ClassType> pool, IReadOnlyList<int> indices)
    {
        var result = new IMistType[indices.Count];
        for(var i = 0; i < indices.Count; i++)
        {
            result[i] = pool[indices[i]];
        }

        return result;
    }

    private static bool ElementwiseReferenceEqual(IReadOnlyList<IMistType> a, IReadOnlyList<IMistType> b)
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
    /// Derives the second pack shape from the first so that, across the mode space,
    /// the generator exercises every case the iff must cover. Correctness of the
    /// property does not depend on the mode: the expected result is computed from the
    /// actual resolved element references, not from the mode.
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

        // mode == 4 (or mode == 3 over an empty first): a fully independent draw, giving
        // a mix of equal (including empty vs empty), order-different, and arity-different
        // pairs so neither branch of the iff is starved.
        return independent;
    }
}