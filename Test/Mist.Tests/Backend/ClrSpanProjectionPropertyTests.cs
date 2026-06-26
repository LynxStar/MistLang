// Feature: stack-only-types, Property 15: Inbound CLR span projection
//
// **Validates: Requirements 7.3**
//
// In-process; drive TypeProjector with reflection Type objects for
// System.Span`1[E] / System.ReadOnlySpan`1[E] and assert the projected
// SpanType(project(E), isReadOnly) has unknown length.

using FsCheck;
using FsCheck.Fluent;
using Mist.Backends.CSharpTranspiler;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Backend;

/// <summary>
/// Property 15: Inbound CLR span projection.
/// For any stampable CLR element type E and span kind K (Span vs ReadOnlySpan),
/// projecting System.K`1[E] via TypeProjector.ProjectTypeReference produces a
/// SpanType with the correct element type, correct IsReadOnly, and null KnownLength
/// (unknown length).
///
/// **Validates: Requirements 7.3**
/// </summary>
public sealed class ClrSpanProjectionPropertyTests
{
    private readonly TypeProjector _projector;
    private readonly IStdlibTypeProvider _stdlib;

    /// <summary>
    /// Representative CLR element types for constructing span types via reflection.
    /// These are all stampable (value types / primitives), the valid element types
    /// for spans in practice.
    /// </summary>
    private static readonly Type[] ElementTypes =
    [
        typeof(int),
        typeof(byte),
        typeof(char),
        typeof(bool),
        typeof(float),
        typeof(double),
        typeof(long),
        typeof(short),
        typeof(uint),
        typeof(ulong),
        typeof(sbyte),
        typeof(ushort),
        typeof(decimal),
        typeof(nint),
        typeof(nuint),
    ];

    public ClrSpanProjectionPropertyTests()
    {
        var intrinsicMap = new IntrinsicTypeMap();
        var filter = new VisibilityFilter();
        _projector = new TypeProjector(intrinsicMap, filter);
        _stdlib = FallbackStdlibTypeProvider.Instance;
    }

    /// <summary>
    /// Constructs a CLR System.Span`1[E] or System.ReadOnlySpan`1[E] via reflection.
    /// </summary>
    private static Type MakeClrSpanType(Type elementType, bool isReadOnly)
    {
        var openGeneric = isReadOnly
            ? typeof(ReadOnlySpan<>)
            : typeof(Span<>);
        return openGeneric.MakeGenericType(elementType);
    }

    /// <summary>
    /// Generates a random (elementType, isReadOnly) pair for projection.
    /// </summary>
    private static Gen<(Type ElementType, bool IsReadOnly)> GenSpanInput()
    {
        return Gen.Choose(0, ElementTypes.Length - 1)
            .SelectMany(elemIdx => Gen.Elements(true, false)
                .Select(isReadOnly => (ElementType: ElementTypes[elemIdx], IsReadOnly: isReadOnly)));
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ── Property 15: Inbound CLR span projection ─────────────────────────
    // For any CLR element type E and span kind K, projecting System.K`1[E]
    // produces a SpanType with project(E) as element, IsReadOnly matching K,
    // and null KnownLength (unknown length).

    [Fact]
    public void P15_InboundClrSpanProjection_ProducesSpanTypeWithUnknownLength()
    {
        RunProperty(GenSpanInput(), input =>
        {
            var clrSpanType = MakeClrSpanType(input.ElementType, input.IsReadOnly);

            var projected = _projector.ProjectTypeReference(clrSpanType, _stdlib);

            // Must be a SpanType
            Assert.True(
                projected is SpanType,
                $"Expected SpanType for {clrSpanType.FullName} but got {projected.GetType().Name} " +
                $"(DisplayName: {projected.DisplayName})");

            var spanType = (SpanType)projected;

            // IsReadOnly must match the CLR span kind
            Assert.True(
                spanType.IsReadOnly == input.IsReadOnly,
                $"Expected IsReadOnly={input.IsReadOnly} for {clrSpanType.FullName} " +
                $"but got IsReadOnly={spanType.IsReadOnly}");

            // KnownLength must be null (unknown) — Requirement 7.3
            Assert.True(
                spanType.KnownLength is null,
                $"Expected unknown length (null KnownLength) for {clrSpanType.FullName} " +
                $"but got KnownLength={spanType.KnownLength}");

            // SymbolicLengthId must also be null (no fabricated length)
            Assert.True(
                spanType.SymbolicLengthId is null,
                $"Expected null SymbolicLengthId for {clrSpanType.FullName} " +
                $"but got SymbolicLengthId={spanType.SymbolicLengthId}");

            // The element type must be non-null and represent the projected element
            Assert.True(
                spanType.ElementType is not null,
                $"Expected non-null ElementType for {clrSpanType.FullName}");

            // Verify the element type by projecting the CLR element independently
            // and confirming it matches the span's element type.
            var independentlyProjectedElement = _projector.ProjectTypeReference(input.ElementType, _stdlib);
            Assert.True(
                spanType.ElementType.DisplayName == independentlyProjectedElement.DisplayName,
                $"Element type mismatch for {clrSpanType.FullName}: " +
                $"span.ElementType={spanType.ElementType.DisplayName}, " +
                $"independent projection={independentlyProjectedElement.DisplayName}");
        });
    }
}