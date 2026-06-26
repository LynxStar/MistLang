// Generator: TupleType instances for property-based testing.
// Produces TupleType values with random arities (2–6), random element types
// (primitives and nested tuples), and optional element names.

using FsCheck;
using FsCheck.Fluent;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generator that produces <see cref="TupleType"/> instances with
/// random element types (primitives and nested tuples) and optional names.
/// </summary>
public static class TupleTypeGenerator
{
    /// <summary>
    /// Leaf types used as tuple elements. These are lightweight singletons
    /// that carry a display name and a <see cref="TypeKind"/>.
    /// </summary>
    private static readonly IType[] PrimitiveLeafTypes =
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
    /// Valid identifier names used for optional tuple element names.
    /// </summary>
    private static readonly string[] SampleNames =
    [
        "x", "y", "z", "a", "b", "c",
        "first", "second", "value", "key", "item", "result",
    ];

    /// <summary>
    /// Generates a random leaf <see cref="IType"/> (one of the primitives).
    /// </summary>
    private static Gen<IType> GenLeafType()
    {
        return Gen.Elements(PrimitiveLeafTypes);
    }

    /// <summary>
    /// Generates a random <see cref="IType"/> that may be a primitive or a
    /// nested <see cref="TupleType"/> (depth-limited to avoid unbounded recursion).
    /// </summary>
    private static Gen<IType> GenElementType(int depth)
    {
        return depth <= 0
            ? GenLeafType()
            : Gen.Frequency(
                (3, GenLeafType()),
                (1, GenTupleType(depth - 1).Select(t => (IType)t)));
    }

    /// <summary>
    /// Generates an optional element name (null means unnamed).
    /// </summary>
    private static Gen<string?> GenOptionalName()
    {
        return Gen.Frequency(
            (1, Gen.Constant<string?>(null)),
            (1, Gen.Elements(SampleNames).Select(n => (string?)n)));
    }

    /// <summary>
    /// Generates a <see cref="TupleType"/> with arity 2–6, random element
    /// types, and optional element names.
    /// </summary>
    /// <param name="depth">Maximum nesting depth for nested tuple elements.</param>
    public static Gen<TupleType> GenTupleType(int depth = 1)
    {
        return from arity in Gen.Choose(2, 6)
               from types in Gen.ArrayOf(GenElementType(depth), arity)
               from names in Gen.ArrayOf(GenOptionalName(), arity)
               let elementTypes = (IReadOnlyList<IType>)types
               let elementNames = (IReadOnlyList<string?>)names
               let hasAnyName = elementNames.Any(n => n is not null)
               select new TupleType(
                   elementTypes,
                   hasAnyName ? elementNames : null);
    }

    /// <summary>
    /// Provides an <see cref="Arbitrary{TupleType}"/> for FsCheck discovery.
    /// </summary>
    public static Arbitrary<TupleType> ArbitraryTupleType()
    {
        return GenTupleType().ToArbitrary();
    }
}