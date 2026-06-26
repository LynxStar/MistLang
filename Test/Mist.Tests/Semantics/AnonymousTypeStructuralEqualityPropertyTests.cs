// Feature: anonymous-types, Property 3: Structural equality is determined by names, types, and order
// **Validates: Requirements 2.3, 2.4**

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for <see cref="AnonymousType.StructurallyEquals"/>.
/// Verifies that structural equality is determined by property names, types,
/// and order — and nothing else.
/// </summary>
public class AnonymousTypeStructuralEqualityPropertyTests
{
    // ─── Shared leaf types ─────────────────────────────────────────────────

    private static readonly IType[] LeafTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.String,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Char,
        LegacyPrimitiveTypes.Long,
    ];

    private static readonly string[] PropertyNamePool =
    [
        "Name", "Age", "Value", "Key", "Count",
        "Label", "Score", "Id", "Tag", "Level",
    ];

    // ─── Generators ───────────────────────────────────────────────────────

    private static Gen<IType> GenLeafType() => Gen.Elements(LeafTypes);

    private static Gen<string> GenPropertyName() => Gen.Elements(PropertyNamePool);

    private static Gen<string[]> GenDistinctNames(int count) =>
        Gen.ListOf(GenPropertyName(), count)
            .Select(names => names.ToArray())
            .Where(names => names.Distinct().Count() == names.Length);

    private static Gen<AnonymousType> GenAnonymousType() =>
        from count in Gen.Choose(1, 5)
        from names in GenDistinctNames(count)
        from types in Gen.ListOf(GenLeafType(), count).Select(x => x.ToArray())
        select new AnonymousType(names, types);

    public static Arbitrary<AnonymousType> ArbitraryAnonymousType() =>
        GenAnonymousType().ToArbitrary();

    // ─── Properties ───────────────────────────────────────────────────────

    /// <summary>
    /// Reflexivity: any AnonymousType is structurally equal to itself.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(AnonymousTypeStructuralEqualityPropertyTests) })]
    public bool StructurallyEquals_Reflexive(AnonymousType type)
    {
        return type.StructurallyEquals(type);
    }

    /// <summary>
    /// Two distinct instances with the same names, types, and order are structurally equal.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StructurallyEquals_SameShape_ReturnsTrue()
    {
        var gen =
            from count in Gen.Choose(1, 5)
            from names in GenDistinctNames(count)
            from types in Gen.ListOf(GenLeafType(), count).Select(x => x.ToArray())
            select (names, types);

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var a = new AnonymousType(pair.names, pair.types);
            var b = new AnonymousType(
                (IReadOnlyList<string>)pair.names.ToArray(),
                (IReadOnlyList<IType>)pair.types.ToArray());

            return a.StructurallyEquals(b);
        });
    }

    /// <summary>
    /// Symmetry: if A equals B, then B equals A.
    ///
    /// **Validates: Requirements 2.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StructurallyEquals_Symmetric()
    {
        var gen =
            from count in Gen.Choose(1, 5)
            from names in GenDistinctNames(count)
            from types in Gen.ListOf(GenLeafType(), count).Select(x => x.ToArray())
            select (names, types);

        return Prop.ForAll(gen.ToArbitrary(), pair =>
        {
            var a = new AnonymousType(pair.names, pair.types);
            var b = new AnonymousType(
                (IReadOnlyList<string>)pair.names.ToArray(),
                (IReadOnlyList<IType>)pair.types.ToArray());

            return a.StructurallyEquals(b) && b.StructurallyEquals(a);
        });
    }

    /// <summary>
    /// Different property names produce non-equal types.
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StructurallyEquals_DifferentName_ReturnsFalse()
    {
        var gen =
            from count in Gen.Choose(1, 5)
            from names in GenDistinctNames(count)
            from types in Gen.ListOf(GenLeafType(), count).Select(x => x.ToArray())
            from diffIndex in Gen.Choose(0, count - 1)
            from altName in GenPropertyName().Where(n => n != names[diffIndex])
            select (names, types, diffIndex, altName);

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            var altNames = data.names.ToArray();
            altNames[data.diffIndex] = data.altName;

            var a = new AnonymousType(data.names, data.types);
            var b = new AnonymousType(altNames, data.types);

            return !a.StructurallyEquals(b);
        });
    }

    /// <summary>
    /// Different property types produce non-equal types.
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StructurallyEquals_DifferentType_ReturnsFalse()
    {
        var gen =
            from count in Gen.Choose(1, 5)
            from names in GenDistinctNames(count)
            from types in Gen.ListOf(GenLeafType(), count).Select(x => x.ToArray())
            from diffIndex in Gen.Choose(0, count - 1)
            from altType in GenLeafType().Where(t => t != types[diffIndex])
            select (names, types, diffIndex, altType);

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            var altTypes = data.types.ToArray();
            altTypes[data.diffIndex] = data.altType;

            var a = new AnonymousType(data.names, data.types);
            var b = new AnonymousType(data.names, altTypes);

            return !a.StructurallyEquals(b);
        });
    }

    /// <summary>
    /// Different property order (same set of name–type pairs reordered) produces non-equal types.
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StructurallyEquals_DifferentOrder_ReturnsFalse()
    {
        var gen =
            from count in Gen.Choose(2, 5)
            from names in GenDistinctNames(count)
            from types in Gen.ListOf(GenLeafType(), count).Select(x => x.ToArray())
            from swapA in Gen.Choose(0, count - 1)
            from swapB in Gen.Choose(0, count - 1).Where(b => b != swapA)
            select (names, types, swapA, swapB);

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            var reorderedNames = data.names.ToArray();
            var reorderedTypes = data.types.ToArray();

            (reorderedNames[data.swapA], reorderedNames[data.swapB]) =
                (reorderedNames[data.swapB], reorderedNames[data.swapA]);
            (reorderedTypes[data.swapA], reorderedTypes[data.swapB]) =
                (reorderedTypes[data.swapB], reorderedTypes[data.swapA]);

            var a = new AnonymousType(data.names, data.types);
            var b = new AnonymousType(reorderedNames, reorderedTypes);

            return !a.StructurallyEquals(b);
        });
    }

    /// <summary>
    /// Different property count produces non-equal types.
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StructurallyEquals_DifferentCount_ReturnsFalse()
    {
        var gen =
            from countA in Gen.Choose(1, 4)
            from countB in Gen.Choose(1, 4).Where(b => b != countA)
            from namesA in GenDistinctNames(countA)
            from namesB in GenDistinctNames(countB)
            from typesA in Gen.ListOf(GenLeafType(), countA).Select(x => x.ToArray())
            from typesB in Gen.ListOf(GenLeafType(), countB).Select(x => x.ToArray())
            select (namesA, typesA, namesB, typesB);

        return Prop.ForAll(gen.ToArbitrary(), data =>
        {
            var a = new AnonymousType(data.namesA, data.typesA);
            var b = new AnonymousType(data.namesB, data.typesB);

            return !a.StructurallyEquals(b);
        });
    }
}