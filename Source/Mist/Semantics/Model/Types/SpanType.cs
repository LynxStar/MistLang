using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model.Types;

/// <summary>
/// A span type representing <c>Standard.Span&lt;T, Length&gt;</c> or
/// <c>Standard.ReadOnlySpan&lt;T, Length&gt;</c>. Spans are ref structs
/// (stack-only value types) in .NET.
/// </summary>
// Modeled field-for-field on ArrayType (the Array<T, Length> precedent), plus an
// IsReadOnly discriminator and Kind => TypeKind.Struct. Use-sites are routed to
// SpanType directly, just as Array<T, Length> use-sites are routed to ArrayType.
// The CSharpTypeMap handles SpanType by recursively mapping the element type and
// erasing the Length, lowering to System.Span<T> / System.ReadOnlySpan<T>.
public sealed class SpanType : IType
{
    public IType ElementType { get; }

    /// <summary>
    /// Whether this is a read-only span. False means <c>Span&lt;T, Length&gt;</c>,
    /// true means <c>ReadOnlySpan&lt;T, Length&gt;</c>.
    /// </summary>
    public bool IsReadOnly { get; }

    /// <summary>
    /// The span's Length. Null means unknown (alias, runtime stackalloc size,
    /// inbound CLR projection). When non-null and SymbolicLengthId is null, this
    /// is a concrete compile-time length (literal stackalloc size, literal Slice length).
    /// </summary>
    public long? KnownLength { get; }

    /// <summary>
    /// A unique identity for symbolic (unknown) lengths. Non-null when the span
    /// has an unknown length that can still be referenced symbolically. Mirrors
    /// ArrayType.SymbolicLengthId.
    /// </summary>
    public int? SymbolicLengthId { get; }

    public TypeKind Kind => TypeKind.Struct;

    public string DisplayName
    {
        get
        {
            var name = IsReadOnly ? "ReadOnlySpan" : "Span";
            return KnownLength.HasValue
                ? $"{name}<{ElementType.DisplayName}, {KnownLength.Value}>"
                : $"{name}<{ElementType.DisplayName}>";
        }
    }

    /// <summary>
    /// Creates a SpanType with unknown length.
    /// </summary>
    public SpanType(IType elementType, bool isReadOnly)
    {
        ElementType = elementType;
        IsReadOnly = isReadOnly;
        KnownLength = null;
        SymbolicLengthId = null;
    }

    /// <summary>
    /// Creates a SpanType with a concrete known length.
    /// </summary>
    public SpanType(IType elementType, long knownLength, bool isReadOnly)
    {
        ElementType = elementType;
        IsReadOnly = isReadOnly;
        KnownLength = knownLength;
        SymbolicLengthId = null;
    }

    /// <summary>
    /// Creates a SpanType with a symbolic length identity.
    /// </summary>
    public SpanType(IType elementType, int symbolicLengthId, bool isReadOnly)
    {
        ElementType = elementType;
        IsReadOnly = isReadOnly;
        KnownLength = null;
        SymbolicLengthId = symbolicLengthId;
    }
}
