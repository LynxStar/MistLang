// Feature: stack-only-types
// Validates Requirements 1.1 and 2.1 — the semantic model exposes a dedicated
// SpanType IType modeled field-for-field on ArrayType (the Array<T, Length>
// precedent), covering its three constructors (unknown / known / symbolic
// length), the mutable-vs-read-only DisplayName forms, and Kind == Struct.

using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for <see cref="SpanType"/> — the dedicated span IType for
/// <c>Standard.Span&lt;T, Length&gt;</c> / <c>Standard.ReadOnlySpan&lt;T, Length&gt;</c>.
/// Mirrors the shape of <see cref="ArrayType"/>: an element type, an optional
/// known length, an optional symbolic length identity, plus an IsReadOnly
/// discriminator and <see cref="TypeKind.Struct"/>.
/// </summary>
public class SpanTypeTests
{
    /// <summary>
    /// Minimal element-type stand-in with a stable <see cref="IType.DisplayName"/>
    /// so DisplayName assertions are deterministic and independent of the stdlib.
    /// </summary>
    private sealed class StubElementType : IType
    {
        public string DisplayName => "int";
        public TypeKind Kind => TypeKind.Struct;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor 1 — unknown length (elementType, isReadOnly)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UnknownLengthConstructor_Mutable_SetsExpectedState()
    {
        var element = new StubElementType();

        var span = new SpanType(element, isReadOnly: false);

        Assert.Same(element, span.ElementType);
        Assert.False(span.IsReadOnly);
        Assert.Null(span.KnownLength);
        Assert.Null(span.SymbolicLengthId);
    }

    [Fact]
    public void UnknownLengthConstructor_ReadOnly_SetsIsReadOnly()
    {
        var element = new StubElementType();

        var span = new SpanType(element, isReadOnly: true);

        Assert.Same(element, span.ElementType);
        Assert.True(span.IsReadOnly);
        Assert.Null(span.KnownLength);
        Assert.Null(span.SymbolicLengthId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor 2 — known length (elementType, knownLength, isReadOnly)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void KnownLengthConstructor_Mutable_SetsKnownLength()
    {
        var element = new StubElementType();

        var span = new SpanType(element, knownLength: 4L, isReadOnly: false);

        Assert.Same(element, span.ElementType);
        Assert.False(span.IsReadOnly);
        Assert.Equal(4L, span.KnownLength);
        Assert.Null(span.SymbolicLengthId);
    }

    [Fact]
    public void KnownLengthConstructor_ReadOnly_SetsKnownLengthAndIsReadOnly()
    {
        var element = new StubElementType();

        var span = new SpanType(element, knownLength: 7L, isReadOnly: true);

        Assert.True(span.IsReadOnly);
        Assert.Equal(7L, span.KnownLength);
        Assert.Null(span.SymbolicLengthId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor 3 — symbolic length (elementType, symbolicLengthId, isReadOnly)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SymbolicLengthConstructor_Mutable_SetsSymbolicId()
    {
        var element = new StubElementType();

        var span = new SpanType(element, symbolicLengthId: 42, isReadOnly: false);

        Assert.Same(element, span.ElementType);
        Assert.False(span.IsReadOnly);
        Assert.Null(span.KnownLength);
        Assert.Equal(42, span.SymbolicLengthId);
    }

    [Fact]
    public void SymbolicLengthConstructor_ReadOnly_SetsSymbolicIdAndIsReadOnly()
    {
        var element = new StubElementType();

        var span = new SpanType(element, symbolicLengthId: 3, isReadOnly: true);

        Assert.True(span.IsReadOnly);
        Assert.Null(span.KnownLength);
        Assert.Equal(3, span.SymbolicLengthId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // DisplayName — mutable / read-only × unknown / known / symbolic length
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DisplayName_Mutable_UnknownLength_OmitsLength()
    {
        var span = new SpanType(new StubElementType(), isReadOnly: false);

        Assert.Equal("Span<int>", span.DisplayName);
    }

    [Fact]
    public void DisplayName_ReadOnly_UnknownLength_OmitsLength()
    {
        var span = new SpanType(new StubElementType(), isReadOnly: true);

        Assert.Equal("ReadOnlySpan<int>", span.DisplayName);
    }

    [Fact]
    public void DisplayName_Mutable_KnownLength_IncludesLength()
    {
        var span = new SpanType(new StubElementType(), knownLength: 4L, isReadOnly: false);

        Assert.Equal("Span<int, 4>", span.DisplayName);
    }

    [Fact]
    public void DisplayName_ReadOnly_KnownLength_IncludesLength()
    {
        var span = new SpanType(new StubElementType(), knownLength: 16L, isReadOnly: true);

        Assert.Equal("ReadOnlySpan<int, 16>", span.DisplayName);
    }

    [Fact]
    public void DisplayName_Mutable_SymbolicLength_OmitsLength()
    {
        // A symbolic length leaves KnownLength null, so DisplayName uses the
        // unknown-length form (no length printed), exactly like the alias.
        var span = new SpanType(new StubElementType(), symbolicLengthId: 9, isReadOnly: false);

        Assert.Equal("Span<int>", span.DisplayName);
    }

    [Fact]
    public void DisplayName_ReadOnly_SymbolicLength_OmitsLength()
    {
        var span = new SpanType(new StubElementType(), symbolicLengthId: 9, isReadOnly: true);

        Assert.Equal("ReadOnlySpan<int>", span.DisplayName);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Kind — a span is always a struct (ref struct value type), regardless of
    // length state or read-only-ness.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Kind_IsStruct_ForAllConstructors()
    {
        var element = new StubElementType();

        Assert.Equal(TypeKind.Struct, new SpanType(element, isReadOnly: false).Kind);
        Assert.Equal(TypeKind.Struct, new SpanType(element, isReadOnly: true).Kind);
        Assert.Equal(TypeKind.Struct, new SpanType(element, knownLength: 4L, isReadOnly: false).Kind);
        Assert.Equal(TypeKind.Struct, new SpanType(element, symbolicLengthId: 1, isReadOnly: true).Kind);
    }
}
