// Generator: AnonymousType instances for property-based testing.
// Produces AnonymousType values with random property counts (1–6),
// valid C# identifier names, and random leaf types (primitives).

using FsCheck;
using FsCheck.Fluent;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generator that produces <see cref="AnonymousType"/> instances with
/// random property names (valid C# identifiers) and types (primitives).
/// </summary>
public static class AnonymousTypeGenerator
{
    /// <summary>
    /// Leaf types used as anonymous type property types. These are lightweight
    /// singletons that carry a display name and a <see cref="TypeKind"/>.
    /// </summary>
    private static readonly IType[] LeafTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Char,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.String,
    ];

    /// <summary>
    /// Pool of valid C# identifier names used as anonymous type property names.
    /// All names are distinct to avoid duplicate property name issues.
    /// </summary>
    private static readonly string[] PropertyNamePool =
    [
        "Name", "Value", "Key", "Count", "Index",
        "Label", "Tag", "Score", "Width", "Height",
        "Age", "City", "Title", "Color", "Status",
        "X", "Y", "Z", "Id", "Version",
    ];

    /// <summary>
    /// Generates a random leaf <see cref="IType"/>.
    /// </summary>
    private static Gen<IType> GenLeafType()
    {
        return Gen.Elements(LeafTypes);
    }

    /// <summary>
    /// Generates a list of N distinct property names (N ≥ 1) by picking
    /// N indices without replacement from the name pool.
    /// </summary>
    private static Gen<string[]> GenDistinctNames(int count)
    {
        return Gen.ListOf(Gen.Choose(0, PropertyNamePool.Length - 1), count)
            .Select(indices => indices.Distinct().Take(count).Select(i => PropertyNamePool[i]).ToArray())
            .Where(names => names.Length == count);
    }

    /// <summary>
    /// Generates an <see cref="AnonymousType"/> with 1–6 properties,
    /// each having a distinct name and a random leaf type.
    /// </summary>
    public static Gen<AnonymousType> GenAnonymousType()
    {
        return from count in Gen.Choose(1, 6)
               from names in GenDistinctNames(count)
               from types in Gen.ListOf(GenLeafType(), count)
               select new AnonymousType(names.ToArray(), types.Select(t => t).ToArray());
    }

    /// <summary>
    /// Provides an <see cref="Arbitrary{AnonymousType}"/> for FsCheck discovery.
    /// </summary>
    public static Arbitrary<AnonymousType> ArbitraryAnonymousType()
    {
        return GenAnonymousType().ToArbitrary();
    }
}