// Generator: SpanType instances for property-based testing.
// Produces SpanType values with random element types (stampable primitives),
// random length states (unknown, known, symbolic), and random read-only flags.
// Feature: stack-only-types, Property 13: Length-erasing emission
// **Validates: Requirements 6.2, 6.3, 7.1, 7.2**

using FsCheck;
using FsCheck.Fluent;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Generators;

/// <summary>
/// FsCheck generator that produces <see cref="SpanType"/> instances with random
/// element types (stampable primitives that map through the Backing_Type_Map),
/// random length states (unknown, known literal, symbolic), and random IsReadOnly flags.
/// </summary>
public static class SpanTypeGenerator
{
    /// <summary>
    /// Stampable element types that have known CLR names via the Backing_Type_Map.
    /// These are the types that would realistically appear as span element types
    /// (stackalloc requires stampable elements).
    /// </summary>
    private static readonly IType[] StampableElementTypes =
    [
        LegacyPrimitiveTypes.Int,
        LegacyPrimitiveTypes.Uint,
        LegacyPrimitiveTypes.Long,
        LegacyPrimitiveTypes.Ulong,
        LegacyPrimitiveTypes.Short,
        LegacyPrimitiveTypes.Ushort,
        LegacyPrimitiveTypes.Byte,
        LegacyPrimitiveTypes.Sbyte,
        LegacyPrimitiveTypes.Float,
        LegacyPrimitiveTypes.Double,
        LegacyPrimitiveTypes.Decimal,
        LegacyPrimitiveTypes.Bool,
        LegacyPrimitiveTypes.Char,
    ];

    /// <summary>
    /// Stdlib Backend_Backed element types (the Standard.* form with explicit namespace).
    /// These exercise the fully-qualified Backing_Type_Map lookup path.
    /// </summary>
    private static readonly IType[] StdlibElementTypes = CreateStdlibElementTypes();

    private static IType[] CreateStdlibElementTypes()
    {
        var scope = new Scope(parent: null);
        return
        [
            new StructType("Int32", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("UInt32", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Int64", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("UInt64", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Int16", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("UInt16", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Byte", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("SByte", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Single", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Double", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Decimal", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Boolean", scope, containingNamespace: "Standard", isBackendBacked: true),
            new StructType("Char", scope, containingNamespace: "Standard", isBackendBacked: true),
        ];
    }

    /// <summary>Generates a random stampable element type (legacy or stdlib form).</summary>
    private static Gen<IType> GenElementType() =>
        Gen.OneOf(
            Gen.Elements(StampableElementTypes),
            Gen.Elements(StdlibElementTypes));

    /// <summary>Generates a random IsReadOnly flag.</summary>
    private static Gen<bool> GenIsReadOnly() => Gen.Elements(false, true);

    /// <summary>
    /// Generates a <see cref="SpanType"/> with unknown length (the alias / inbound projection form).
    /// </summary>
    private static Gen<SpanType> GenUnknownLengthSpan() =>
        from element in GenElementType()
        from isReadOnly in GenIsReadOnly()
        select new SpanType(element, isReadOnly);

    /// <summary>
    /// Generates a <see cref="SpanType"/> with a known compile-time length (literal stackalloc size).
    /// </summary>
    private static Gen<SpanType> GenKnownLengthSpan() =>
        from element in GenElementType()
        from length in Gen.Choose(0, 1024).Select(n => (long)n)
        from isReadOnly in GenIsReadOnly()
        select new SpanType(element, length, isReadOnly);

    /// <summary>
    /// Generates a <see cref="SpanType"/> with a symbolic length identity.
    /// </summary>
    private static Gen<SpanType> GenSymbolicLengthSpan() =>
        from element in GenElementType()
        from symbolicId in Gen.Choose(0, 100)
        from isReadOnly in GenIsReadOnly()
        select new SpanType(element, symbolicId, isReadOnly);

    /// <summary>
    /// Generates a random <see cref="SpanType"/> covering all three length states
    /// (unknown, known, symbolic) with equal probability.
    /// </summary>
    public static Gen<SpanType> GenSpanType() =>
        Gen.OneOf(
            GenUnknownLengthSpan(),
            GenKnownLengthSpan(),
            GenSymbolicLengthSpan());

    /// <summary>
    /// Provides an <see cref="Arbitrary{SpanType}"/> for FsCheck discovery.
    /// </summary>
    public static Arbitrary<SpanType> ArbitrarySpanType() =>
        GenSpanType().ToArbitrary();
}
